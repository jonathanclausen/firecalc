using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Portfolio;

/// <summary>
/// One day of the combined portfolio. <see cref="ReturnPct"/> is the time-weighted return since the start
/// of the requested period: deposits and withdrawals don't count as gains or losses, so it shows how the
/// investments did, not how much money went in. <see cref="NetDeposits"/> is all money put in less money
/// taken out, with shares moved in from elsewhere counted at their value on the day they arrived.
/// </summary>
public record HistoryPoint(DateOnly Date, decimal Value, decimal NetDeposits, decimal ReturnPct);

public record PortfolioHistoryDto(string Currency, DateOnly? FirstDate, DateOnly From, DateOnly To, List<HistoryPoint> Points);

/// <summary>Values all investment accounts together for every day in a period, from stored prices and rates.</summary>
public sealed class PortfolioHistory(FireCalcDbContext db, PriceService prices)
{
    /// <summary>Longer periods are thinned to about this many points; the return is still worked out daily.</summary>
    private const int MaxPoints = 400;

    public async Task<PortfolioHistoryDto> BuildAsync(Guid userId, string currency, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = to is { } t && t < today ? t : today;
        var transactions = await db.Transactions.AsNoTracking()
            .Where(x => db.Accounts.Any(a => a.Id == x.AccountId && a.UserId == userId && a.Type == AccountType.Investment))
            .ToListAsync(ct);
        if (transactions.Count == 0) return new(currency, null, from ?? end, end, []);

        var first = transactions.Min(x => x.Date);
        var start = from is { } f && f > first ? f : first;
        if (start > end) start = end;

        var instrumentIds = transactions.Select(x => x.InstrumentId).OfType<Guid>().Distinct().ToList();
        await prices.RefreshAsync(instrumentIds, first, currency, ct);

        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var closes = (await db.InstrumentPrices.AsNoTracking()
                .Where(p => instrumentIds.Contains(p.InstrumentId) && p.Date <= end)
                .ToListAsync(ct))
            .GroupBy(p => p.InstrumentId)
            .ToDictionary(g => g.Key, g => new Series(g.Select(p => (p.Date, p.Close))));
        var currencies = instruments.Values.Select(i => i.Currency).OfType<string>().Where(c => c != currency).Distinct().ToList();
        var rates = (await db.FxRates.AsNoTracking()
                .Where(r => currencies.Contains(r.Currency) && r.QuoteCurrency == currency && r.Date <= end)
                .ToListAsync(ct))
            .GroupBy(r => r.Currency)
            .ToDictionary(g => g.Key, g => new Series(g.Select(r => (r.Date, r.Rate))));

        var byDay = transactions.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.ToList());
        var replay = new PortfolioReplay();
        var points = new List<HistoryPoint>();
        decimal index = 1, startIndex = 1, previousValue = 0, previousDeposits = 0, putIn = 0;

        // Market value of shares on a day; before the first known price (or rate) they count at what they cost.
        decimal Worth(Guid id, decimal quantity, decimal cost, DateOnly day)
        {
            var instrument = instruments[id];
            var close = closes.GetValueOrDefault(id)?.At(day);
            decimal? fx = instrument.Currency is null || instrument.Currency == currency ? 1 : rates.GetValueOrDefault(instrument.Currency)?.At(day);
            return close is not null && fx is not null ? quantity * close.Value * fx.Value : cost;
        }

        // A share move valued at the holding's average cost when no price is known.
        decimal MovedWorth(PortfolioTransaction t, DateOnly day)
        {
            if (t.InstrumentId is not { } id) return 0;
            var held = replay.Holdings.FirstOrDefault(h => h.InstrumentId == id);
            var averageCost = held.Quantity > 0 ? held.Cost / held.Quantity : 0;
            return Worth(id, t.Quantity, t.Quantity * averageCost, day);
        }

        for (var day = first; day <= end; day = day.AddDays(1))
        {
            // Shares moved in or out (from another broker, say) are money brought in or taken out too,
            // not a gain or loss; they count at their value that day.
            decimal moved = 0;
            if (byDay.TryGetValue(day, out var todays))
            {
                moved -= todays.Where(x => x.Type == TransactionType.SecurityOut).Sum(x => MovedWorth(x, day));
                replay.ApplyDay(todays);
                moved += todays.Where(x => x.Type == TransactionType.SecurityIn).Sum(x => MovedWorth(x, day));
            }

            var value = replay.Cash + replay.Holdings.Sum(h => Worth(h.InstrumentId, h.Quantity, h.Cost, day));

            // Money in or out that day is not a gain: the day's return is measured on what was there before.
            var flow = replay.NetDeposits - previousDeposits + moved;
            putIn += flow;
            if (previousValue > 1) index *= (value - flow) / previousValue;
            if (day == start) startIndex = index;
            if (day >= start)
                points.Add(new HistoryPoint(day, Math.Round(value, 2), Math.Round(putIn, 2), Math.Round((index / startIndex - 1) * 100, 2)));
            previousValue = value;
            previousDeposits = replay.NetDeposits;
        }

        return new(currency, first, start, end, Thin(points));
    }

    /// <summary>Keeps about <see cref="MaxPoints"/> evenly spaced points, always with the first and last.</summary>
    private static List<HistoryPoint> Thin(List<HistoryPoint> points)
    {
        if (points.Count <= MaxPoints) return points;
        var step = (int)Math.Ceiling(points.Count / (double)MaxPoints);
        var kept = points.Where((_, i) => i % step == 0).ToList();
        if (kept[^1] != points[^1]) kept.Add(points[^1]);
        return kept;
    }

    /// <summary>Dated values, read forward in date order: the latest value on or before a day.</summary>
    private sealed class Series
    {
        private readonly (DateOnly Date, decimal Value)[] items;
        private int next;
        private decimal? current;

        public Series(IEnumerable<(DateOnly, decimal)> values) => items = values.OrderBy(v => v.Item1).ToArray();

        public decimal? At(DateOnly day)
        {
            while (next < items.Length && items[next].Date <= day) current = items[next++].Value;
            return current;
        }
    }
}
