using System.Collections.Concurrent;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Portfolio;

/// <summary>
/// Keeps stored daily prices and exchange rates up to date. Prices are fetched when a portfolio is
/// looked at and the stored ones are more than a few hours old, so no scheduled job is needed.
/// </summary>
public sealed class PriceService(FireCalcDbContext db, IMarketData market, PriceRefreshState state, ILogger<PriceService> log)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private const int MaxParallelFetches = 8;

    /// <summary>Finds missing symbols and fetches prices and rates back to <paramref name="from"/>.</summary>
    public async Task RefreshAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly from, string currency, CancellationToken ct)
    {
        if (instrumentIds.Count == 0) return;
        await state.Lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var instruments = await db.Instruments.Where(i => instrumentIds.Contains(i.Id)).ToListAsync(ct);
            // Adding older transactions clears PricesCheckedAt, so missing history is fetched right away.
            var stale = instruments.Where(i => i.PricesCheckedAt is null || now - i.PricesCheckedAt >= MaxAge).ToList();
            if (stale.Count > 0)
            {
                var staleIds = stale.Select(i => i.Id).ToList();
                var stored = await db.InstrumentPrices
                    .Where(p => staleIds.Contains(p.InstrumentId))
                    .GroupBy(p => p.InstrumentId)
                    .Select(g => new { g.Key, First = g.Min(p => p.Date), Last = g.Max(p => p.Date) })
                    .ToDictionaryAsync(x => x.Key, x => (x.First, x.Last), ct);

                // Each instrument is a separate request to the price source, so they run a few at a time
                // rather than one after another. Only the network calls run side by side; the database
                // context is used again once they are all back.
                using var gate = new SemaphoreSlim(MaxParallelFetches);
                var fetched = await Task.WhenAll(stale.Select(async instrument =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        var symbol = instrument.Symbol ?? await FindSymbolAsync(instrument, ct);
                        if (symbol is null) return (instrument, symbol, From: from, History: (PriceHistory?)null);
                        var (first, last) = stored.TryGetValue(instrument.Id, out var range) ? range : ((DateOnly?)null, (DateOnly?)null);
                        var missingHistory = first is null || first > from.AddDays(7);
                        var fetchFrom = missingHistory || last is null ? from : last.Value.AddDays(-7);
                        return (instrument, symbol, From: fetchFrom, History: await market.GetDailyClosesAsync(symbol, fetchFrom, ct));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));

                foreach (var (instrument, symbol, fetchFrom, history) in fetched)
                {
                    instrument.PricesCheckedAt = now;
                    instrument.Symbol = symbol;
                    if (history is null) continue;

                    instrument.Currency = history.Currency;
                    await db.InstrumentPrices.Where(p => p.InstrumentId == instrument.Id && p.Date >= fetchFrom).ExecuteDeleteAsync(ct);
                    db.InstrumentPrices.AddRange(history.Closes
                        .Where(c => c.Date >= fetchFrom)
                        .DistinctBy(c => c.Date)
                        .Select(c => new InstrumentPrice { InstrumentId = instrument.Id, Date = c.Date, Close = c.Close }));
                }
                await db.SaveChangesAsync(ct);
            }

            var currencies = instruments.Select(i => i.Currency).OfType<string>().Where(c => c != currency).Distinct();
            foreach (var fx in currencies)
                await RefreshFxAsync(fx, currency, from, now, ct);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task<List<SymbolMatch>> SearchAsync(string query, CancellationToken ct) => await market.SearchAsync(query, ct);

    /// <summary>The latest close for a symbol, so people can check a search result against their broker.</summary>
    public async Task<(string Currency, DailyClose Close)?> QuoteAsync(string symbol, CancellationToken ct)
    {
        var history = await market.GetDailyClosesAsync(symbol, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10), ct);
        return history?.Closes.LastOrDefault() is { } last ? (history.Currency, last) : null;
    }

    /// <summary>Forgets an instrument's stored prices, e.g. after its symbol was corrected.</summary>
    public async Task ResetPricesAsync(Instrument instrument, CancellationToken ct)
    {
        await db.InstrumentPrices.Where(p => p.InstrumentId == instrument.Id).ExecuteDeleteAsync(ct);
        instrument.PricesCheckedAt = null;
        instrument.Currency = null;
    }

    private async Task<string?> FindSymbolAsync(Instrument instrument, CancellationToken ct)
    {
        if (instrument.Isin is null) return null;
        var matches = await market.SearchAsync(instrument.Isin, ct);
        // Prefer the home market: Copenhagen for Danish ISINs, and so on.
        var home = instrument.Isin[..2] switch
        {
            "DK" => ".CO",
            "SE" => ".ST",
            "NO" => ".OL",
            "FI" => ".HE",
            _ => null,
        };
        var pick = (home is null ? null : matches.FirstOrDefault(m => m.Symbol.EndsWith(home, StringComparison.OrdinalIgnoreCase)))
            ?? matches.FirstOrDefault();
        if (pick is null) log.LogInformation("No price symbol found for {Isin}", instrument.Isin);
        return pick?.Symbol;
    }

    private async Task RefreshFxAsync(string fx, string quote, DateOnly from, DateTimeOffset now, CancellationToken ct)
    {
        var key = $"{fx}/{quote}";
        // A check only counts if it reached back as far as this one needs. When the source had no rates
        // that far back, it is asked again sooner in case they turn up.
        if (state.FxCheckedAt.TryGetValue(key, out var checkedAt) && checkedAt.From <= from
            && now - checkedAt.At < (checkedAt.Complete ? MaxAge : TimeSpan.FromMinutes(5))) return;

        var stored = await db.FxRates.Where(r => r.Currency == fx && r.QuoteCurrency == quote)
            .GroupBy(r => 1)
            .Select(g => new { First = g.Min(r => r.Date), Last = g.Max(r => r.Date) })
            .SingleOrDefaultAsync(ct);
        var missingHistory = stored is null || stored.First > from.AddDays(7);
        var fetchFrom = missingHistory || stored is null ? from : stored.Last.AddDays(-7);
        var rates = await market.GetFxRatesAsync(fx, quote, fetchFrom, ct);
        var first = new[] { stored?.First, rates.Count > 0 ? rates.Min(r => r.Date) : null }.Min();
        state.FxCheckedAt[key] = (now, from, first is not null && first <= from.AddDays(7));
        if (rates.Count == 0) return;

        await db.FxRates.Where(r => r.Currency == fx && r.QuoteCurrency == quote && r.Date >= fetchFrom).ExecuteDeleteAsync(ct);
        db.FxRates.AddRange(rates.DistinctBy(r => r.Date).Select(r => new FxRate { Currency = fx, QuoteCurrency = quote, Date = r.Date, Rate = r.Close }));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>App-wide refresh bookkeeping: one refresh at a time, and when each rate was last asked for.</summary>
public sealed class PriceRefreshState
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public ConcurrentDictionary<string, (DateTimeOffset At, DateOnly From, bool Complete)> FxCheckedAt { get; } = new();
}
