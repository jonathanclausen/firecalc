using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Portfolio;

public record PositionValue(
    Guid InstrumentId,
    string Name,
    string? Isin,
    string? Symbol,
    string? Currency,
    decimal Quantity,
    decimal? Price,
    DateOnly? PriceDate,
    decimal Value,
    decimal CostBasis,
    decimal Gain,
    decimal? GainPct,
    decimal? DayChange,
    decimal Dividends,
    decimal RealizedGain,
    decimal WeightPct,
    /// <summary>True when no recent price was found; the value then falls back to what was paid.</summary>
    bool PriceMissing);

public record AccountPortfolio(
    Guid AccountId,
    string AccountName,
    bool Archived,
    decimal Value,
    decimal MarketValue,
    decimal Cash,
    decimal CostBasis,
    decimal UnrealizedGain,
    decimal RealizedGain,
    decimal Dividends,
    decimal NetDeposits,
    /// <summary>Value minus net deposits: what the market (not your savings) added.</summary>
    decimal Growth,
    decimal DayChange,
    DateOnly? PricesAsOf,
    int TransactionCount,
    List<PositionValue> Positions);

/// <summary>Values each investment account's positions with stored prices and rates on a date.</summary>
public sealed class PortfolioValuation(FireCalcDbContext db, PriceService prices)
{
    /// <summary>A price older than this is treated as missing rather than shown as current.</summary>
    private const int MaxPriceAgeDays = 30;

    public async Task<List<AccountPortfolio>> ValueAsync(Guid userId, string currency, DateOnly asOf, bool refresh, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && db.Transactions.Any(t => t.AccountId == a.Id))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        if (accounts.Count == 0) return [];

        var accountIds = accounts.Select(a => a.Id).ToList();
        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId))
            .ToListAsync(ct);

        var instrumentIds = transactions.Select(t => t.InstrumentId).OfType<Guid>().Distinct().ToList();
        if (refresh)
            await prices.RefreshAsync(instrumentIds, transactions.Min(t => t.Date), currency, ct);

        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var windowStart = asOf.AddDays(-MaxPriceAgeDays);
        var recentPrices = (await db.InstrumentPrices.AsNoTracking()
                .Where(p => instrumentIds.Contains(p.InstrumentId) && p.Date <= asOf && p.Date >= windowStart)
                .ToListAsync(ct))
            .GroupBy(p => p.InstrumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Date).Take(2).ToList());
        var currencies = instruments.Values.Select(i => i.Currency).OfType<string>().Where(c => c != currency).Distinct().ToList();
        var rates = (await db.FxRates.AsNoTracking()
                .Where(r => currencies.Contains(r.Currency) && r.QuoteCurrency == currency && r.Date <= asOf && r.Date >= windowStart)
                .ToListAsync(ct))
            .GroupBy(r => r.Currency)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Date)!.Rate);

        return accounts.Select(account =>
        {
            var own = transactions.Where(t => t.AccountId == account.Id).ToList();
            var holdings = PortfolioCalculator.Calculate(own, asOf);

            var positions = new List<PositionValue>();
            DateOnly? pricesAsOf = null;
            foreach (var h in holdings.Holdings.Where(h => h.Quantity > 0))
            {
                var instrument = instruments[h.InstrumentId];
                var closes = recentPrices.GetValueOrDefault(h.InstrumentId);
                decimal? fx = instrument.Currency is null || instrument.Currency == currency ? 1 : rates.GetValueOrDefault(instrument.Currency);
                if (fx == 0) fx = null;

                var latest = closes?.FirstOrDefault();
                var priced = latest is not null && fx is not null;
                var value = priced ? Math.Round(h.Quantity * latest!.Close * fx!.Value, 2) : h.CostBasis;
                decimal? dayChange = priced && closes!.Count > 1
                    ? Math.Round(h.Quantity * (latest!.Close - closes[1].Close) * fx!.Value, 2)
                    : null;
                if (priced && (pricesAsOf is null || latest!.Date > pricesAsOf)) pricesAsOf = latest!.Date;

                var gain = value - h.CostBasis;
                positions.Add(new PositionValue(
                    h.InstrumentId, instrument.Name, instrument.Isin, instrument.Symbol, instrument.Currency,
                    h.Quantity, latest?.Close, latest?.Date, value, h.CostBasis, gain,
                    h.CostBasis > 0 ? Math.Round(gain / h.CostBasis * 100, 2) : null,
                    dayChange, h.Dividends, h.RealizedGain, 0, !priced));
            }

            var marketValue = positions.Sum(p => p.Value);
            positions = positions
                .Select(p => p with { WeightPct = marketValue > 0 ? Math.Round(p.Value / marketValue * 100, 1) : 0 })
                .OrderByDescending(p => p.Value)
                .ToList();
            var cost = positions.Sum(p => p.CostBasis);
            var cash = Math.Round(holdings.Cash, 2);
            var total = marketValue + cash;

            return new AccountPortfolio(
                account.Id, account.Name, account.Archived, total, marketValue, cash, cost, marketValue - cost,
                holdings.RealizedGain, holdings.Dividends, holdings.NetDeposits, total - holdings.NetDeposits,
                positions.Sum(p => p.DayChange ?? 0), pricesAsOf, own.Count, positions);
        }).ToList();
    }
}
