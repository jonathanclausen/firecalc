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

    /// <summary>Finds missing symbols and fetches prices and rates back to <paramref name="from"/>.</summary>
    public async Task RefreshAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly from, string currency, CancellationToken ct)
    {
        if (instrumentIds.Count == 0) return;
        await state.Lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var instruments = await db.Instruments.Where(i => instrumentIds.Contains(i.Id)).ToListAsync(ct);
            foreach (var instrument in instruments)
            {
                var firstStored = await db.InstrumentPrices.Where(p => p.InstrumentId == instrument.Id).MinAsync(p => (DateOnly?)p.Date, ct);
                var missingHistory = firstStored is null || firstStored > from.AddDays(7);
                // Adding older transactions clears PricesCheckedAt, so missing history is fetched right away.
                if (instrument.PricesCheckedAt is not null && now - instrument.PricesCheckedAt < MaxAge) continue;

                instrument.PricesCheckedAt = now;
                instrument.Symbol ??= await FindSymbolAsync(instrument, ct);
                if (instrument.Symbol is null) continue;

                var lastStored = await db.InstrumentPrices.Where(p => p.InstrumentId == instrument.Id).MaxAsync(p => (DateOnly?)p.Date, ct);
                var fetchFrom = missingHistory || lastStored is null ? from : lastStored.Value.AddDays(-7);
                var history = await market.GetDailyClosesAsync(instrument.Symbol, fetchFrom, ct);
                if (history is null) continue;

                instrument.Currency = history.Currency;
                await db.InstrumentPrices.Where(p => p.InstrumentId == instrument.Id && p.Date >= fetchFrom).ExecuteDeleteAsync(ct);
                db.InstrumentPrices.AddRange(history.Closes
                    .Where(c => c.Date >= fetchFrom)
                    .DistinctBy(c => c.Date)
                    .Select(c => new InstrumentPrice { InstrumentId = instrument.Id, Date = c.Date, Close = c.Close }));
            }
            await db.SaveChangesAsync(ct);

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
        var firstStored = await db.FxRates.Where(r => r.Currency == fx && r.QuoteCurrency == quote).MinAsync(r => (DateOnly?)r.Date, ct);
        var missingHistory = firstStored is null || firstStored > from.AddDays(7);
        if (state.FxCheckedAt.TryGetValue(key, out var checkedAt) && now - checkedAt < (missingHistory ? TimeSpan.FromMinutes(5) : MaxAge)) return;
        state.FxCheckedAt[key] = now;

        var lastStored = await db.FxRates.Where(r => r.Currency == fx && r.QuoteCurrency == quote).MaxAsync(r => (DateOnly?)r.Date, ct);
        var fetchFrom = missingHistory || lastStored is null ? from : lastStored.Value.AddDays(-7);
        var rates = await market.GetFxRatesAsync(fx, quote, fetchFrom, ct);
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
    public ConcurrentDictionary<string, DateTimeOffset> FxCheckedAt { get; } = new();
}
