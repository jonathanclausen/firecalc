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

/// <summary>
/// How one security did in one account over the period. <see cref="Gain"/> is the change in value less what
/// was bought and plus what was sold or paid out as dividend, so shares bought with new money are not a gain.
/// <see cref="Invested"/> is the value at the start plus what went in during the period, the base for a
/// percentage. A security sold during the period is still listed, with <see cref="Quantity"/> 0.
/// </summary>
public record SecurityPeriod(
    Guid AccountId, Guid InstrumentId, string Name, string? Symbol,
    decimal Quantity, decimal Value, decimal Gain, decimal Invested, decimal Dividends);

/// <summary>
/// One account over the period. <see cref="Gain"/> is measured like the combined one (value change less money
/// put in), so whatever its securities don't explain (interest, fees, cash) is the difference.
/// </summary>
public record AccountPeriod(Guid AccountId, string Name, decimal Value, decimal Gain);

public record PortfolioHistoryDto(
    string Currency, DateOnly? FirstDate, DateOnly From, DateOnly To, List<HistoryPoint> Points,
    List<AccountPeriod> Accounts, List<SecurityPeriod> Securities);

/// <summary>Values all investment accounts together for every day in a period, from stored prices and rates.</summary>
public sealed class PortfolioHistory(FireCalcDbContext db, PriceService prices)
{
    /// <summary>Longer periods are thinned to about this many points; the return is still worked out daily.</summary>
    private const int MaxPoints = 400;

    public async Task<PortfolioHistoryDto> BuildAsync(Guid userId, string currency, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = to is { } t && t < today ? t : today;
        var (days, period) = await RunAsync(userId, currency, from, end, trackPeriod: true, ct);
        if (days.Count == 0) return new(currency, null, from ?? end, end, [], [], []);

        var first = days[0].Date;
        var start = period!.Start;
        var startIndex = days.First(d => d.Date == start).Index;
        var points = days
            .Where(d => d.Date >= start)
            .Select(d => new HistoryPoint(d.Date, Math.Round(d.Value, 2), Math.Round(d.PutIn, 2), Math.Round((d.Index / startIndex - 1) * 100, 2)))
            .ToList();
        return new(currency, first, start, end, Thin(points), period.Accounts, period.Securities);
    }

    private sealed record Period(DateOnly Start, List<AccountPeriod> Accounts, List<SecurityPeriod> Securities);

    /// <summary>One entry per day from the first transaction to <paramref name="end"/>.</summary>
    public record Day(DateOnly Date, decimal Value, decimal PutIn, decimal Index);

    /// <summary>
    /// Every day's combined value, money put in so far, and the time-weighted return as an index that
    /// starts at 1. Empty when there are no transactions.
    /// </summary>
    public async Task<List<Day>> DailyAsync(Guid userId, string currency, DateOnly end, CancellationToken ct) =>
        (await RunAsync(userId, currency, null, end, trackPeriod: false, ct)).Days;

    /// <summary>
    /// The daily history and, when <paramref name="trackPeriod"/> is set, how each account and security did
    /// from the end of the period's first day to <paramref name="end"/>.
    /// </summary>
    private async Task<(List<Day> Days, Period? Period)> RunAsync(
        Guid userId, string currency, DateOnly? from, DateOnly end, bool trackPeriod, CancellationToken ct)
    {
        var transactions = await db.Transactions.AsNoTracking()
            .Where(x => db.Accounts.Any(a => a.Id == x.AccountId && a.UserId == userId && a.Type == AccountType.Investment))
            .ToListAsync(ct);
        if (transactions.Count == 0) return ([], null);

        var first = transactions.Min(x => x.Date);
        var start = from is { } f && f > first ? f : first;
        if (start > end) start = end;
        var instrumentIds = transactions.Select(x => x.InstrumentId).OfType<Guid>().Distinct().ToList();
        await prices.RefreshAsync(instrumentIds, first, currency, ct);

        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        // Instruments are shared between users, so prices from long before this person's first
        // transaction (stored for someone else) are left out; a month back covers weekends and holidays.
        var since = first.AddDays(-31);
        var closes = (await db.InstrumentPrices
                .Where(p => instrumentIds.Contains(p.InstrumentId) && p.Date >= since && p.Date <= end)
                .Select(p => new { p.InstrumentId, p.Date, p.Close })
                .ToListAsync(ct))
            .GroupBy(p => p.InstrumentId)
            .ToDictionary(g => g.Key, g => new Series(g.Select(p => (p.Date, p.Close))));
        var currencies = instruments.Values.Select(i => i.Currency).OfType<string>().Where(c => c != currency).Distinct().ToList();
        var rates = (await db.FxRates
                .Where(r => currencies.Contains(r.Currency) && r.QuoteCurrency == currency && r.Date >= since && r.Date <= end)
                .Select(r => new { r.Currency, r.Date, r.Rate })
                .ToListAsync(ct))
            .GroupBy(r => r.Currency)
            .ToDictionary(g => g.Key, g => new Series(g.Select(r => (r.Date, r.Rate))));

        var byDay = transactions.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.ToList());
        var replay = new PortfolioReplay();
        decimal index = 1, previousValue = 0, previousDeposits = 0, putIn = 0;
        var days = new List<Day>();

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

        var tracker = trackPeriod ? new PeriodTracker(start, Worth) : null;

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
                tracker?.ApplyDay(todays, day);
            }
            if (day == start) tracker?.Snapshot(day, atEnd: false);
            if (day == end) tracker?.Snapshot(day, atEnd: true);

            var value = replay.Cash + replay.Holdings.Sum(h => Worth(h.InstrumentId, h.Quantity, h.Cost, day));

            // Money in or out that day is not a gain: the day's return is measured on what was there before.
            var flow = replay.NetDeposits - previousDeposits + moved;
            putIn += flow;
            if (previousValue > 1) index *= (value - flow) / previousValue;
            days.Add(new Day(day, value, putIn, index));
            previousValue = value;
            previousDeposits = replay.NetDeposits;
        }

        if (tracker is null) return (days, null);
        var accountIds = tracker.AccountIds.ToList();
        var names = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var (accounts, securities) = tracker.Result(
            id => names.GetValueOrDefault(id, ""),
            id => (instruments[id].Name, instruments[id].Symbol ?? instruments[id].Isin));
        return (days, new Period(start, accounts, securities));
    }

    /// <summary>
    /// Replays each account on its own, and adds up per account and per security what went in and out during
    /// a period, so their gains can be told apart. Values are taken on the period's first and last day.
    /// </summary>
    private sealed class PeriodTracker(DateOnly start, Func<Guid, decimal, decimal, DateOnly, decimal> worth)
    {
        private readonly Dictionary<Guid, PortfolioReplay> replays = [];
        private readonly Dictionary<(Guid Account, Guid Instrument), Flow> securities = [];
        private readonly Dictionary<Guid, Flow> accounts = [];

        public IEnumerable<Guid> AccountIds => replays.Keys;

        private sealed class Flow
        {
            public decimal StartValue, EndValue, In, Out, Quantity, Dividends;
            public bool Seen;
        }

        private Flow Security(Guid account, Guid instrument) =>
            securities.TryGetValue((account, instrument), out var f) ? f : securities[(account, instrument)] = new Flow();

        private Flow Account(Guid account) => accounts.TryGetValue(account, out var f) ? f : accounts[account] = new Flow();

        public void ApplyDay(IEnumerable<PortfolioTransaction> todays, DateOnly day)
        {
            foreach (var group in todays.GroupBy(t => t.AccountId))
            {
                if (!replays.TryGetValue(group.Key, out var replay)) replays[group.Key] = replay = new PortfolioReplay();
                var counts = day > start;
                var account = Account(group.Key);

                // Shares moved in or out count at their value that day, like money put in or taken out.
                decimal Moved(PortfolioTransaction t)
                {
                    var held = replay.Holdings.FirstOrDefault(h => h.InstrumentId == t.InstrumentId);
                    var averageCost = held.Quantity > 0 ? held.Cost / held.Quantity : 0;
                    return worth(t.InstrumentId!.Value, t.Quantity, t.Quantity * averageCost, day);
                }

                var outs = group.Where(t => t.Type == TransactionType.SecurityOut && t.InstrumentId is not null)
                    .Select(t => (t, worth: Moved(t))).ToList();
                var depositsBefore = replay.NetDeposits;
                replay.ApplyDay(group);
                var ins = group.Where(t => t.Type == TransactionType.SecurityIn && t.InstrumentId is not null)
                    .Select(t => (t, worth: Moved(t))).ToList();
                if (!counts) continue;

                account.In += replay.NetDeposits - depositsBefore + ins.Sum(x => x.worth) - outs.Sum(x => x.worth);
                foreach (var t in group.Where(t => t.InstrumentId is not null))
                {
                    var security = Security(group.Key, t.InstrumentId!.Value);
                    security.Seen = true;
                    // Money paid for the security goes in; money it paid back (sales, dividends) comes out.
                    var cash = -t.Amount;
                    if (t.Type is TransactionType.Dividend or TransactionType.Tax) security.Dividends += t.Amount;
                    if (cash > 0) security.In += cash; else security.Out -= cash;
                }
                foreach (var (t, w) in ins) Security(group.Key, t.InstrumentId!.Value).In += w;
                foreach (var (t, w) in outs) Security(group.Key, t.InstrumentId!.Value).Out += w;
            }
        }

        public void Snapshot(DateOnly day, bool atEnd)
        {
            foreach (var (accountId, replay) in replays)
            {
                decimal total = replay.Cash;
                foreach (var (instrumentId, quantity, cost) in replay.Holdings)
                {
                    var value = worth(instrumentId, quantity, cost, day);
                    total += value;
                    var security = Security(accountId, instrumentId);
                    security.Seen = true;
                    if (atEnd) { security.EndValue = value; security.Quantity = quantity; }
                    else security.StartValue = value;
                }
                var account = Account(accountId);
                if (atEnd) account.EndValue = total; else account.StartValue = total;
            }
        }

        public (List<AccountPeriod>, List<SecurityPeriod>) Result(Func<Guid, string> accountName, Func<Guid, (string Name, string? Symbol)> instrument)
        {
            var accountList = accounts
                .Select(kv => new AccountPeriod(kv.Key, accountName(kv.Key), Math.Round(kv.Value.EndValue, 2),
                    Math.Round(kv.Value.EndValue - kv.Value.StartValue - kv.Value.In, 2)))
                .OrderByDescending(a => a.Value)
                .ToList();
            var securityList = securities
                .Where(kv => kv.Value.Seen)
                .Select(kv =>
                {
                    var (name, symbol) = instrument(kv.Key.Instrument);
                    var f = kv.Value;
                    return new SecurityPeriod(kv.Key.Account, kv.Key.Instrument, name, symbol, f.Quantity,
                        Math.Round(f.EndValue, 2), Math.Round(f.EndValue - f.StartValue - f.In + f.Out, 2),
                        Math.Round(f.StartValue + f.In, 2), Math.Round(f.Dividends, 2));
                })
                .OrderByDescending(x => x.Value)
                .ToList();
            return (accountList, securityList);
        }
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
