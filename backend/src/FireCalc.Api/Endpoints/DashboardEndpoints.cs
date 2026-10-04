using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using FireCalc.Api.Homes;
using FireCalc.Api.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class DashboardEndpoints
{
    public record SeriesPoint(DateOnly Date, decimal Total, Dictionary<AccountType, decimal> ByType);
    public record GoalProgress(GoalEndpoints.GoalDto Goal, decimal Current, decimal Remaining, decimal ProgressPct);

    /// <summary>
    /// An account's part of today's net worth. Tracked accounts are valued live from their transactions;
    /// the others count with the balance entered on <see cref="BalanceDate"/>. A home (type property) counts with
    /// its equity; <see cref="HomeValue"/> and <see cref="Loans"/> are its two parts, so a projection can grow the
    /// value and pay the loans down on their own.
    /// </summary>
    public record AccountValue(Guid Id, string Name, AccountType Type, decimal Value, bool Tracked, DateOnly? BalanceDate,
        decimal? HomeValue = null, decimal? HomeLoan = null, List<LoanTerms>? Loans = null);

    /// <summary>A loan on a home: what is owed today and the terms it is paid down by.</summary>
    public record LoanTerms(string Name, decimal Owed, decimal? InterestPct, decimal? ContributionPct, DateOnly? EndDate, DateOnly? InterestOnlyUntil);

    /// <summary>
    /// How much was put aside per month lately, from up to the last 12 months: money put into the investment
    /// accounts and the growth of savings and cash balances. Only <see cref="Entries"/> with money going in
    /// count, so a sale or a drop in balance counts as zero rather than negative. Each part is null until it
    /// has a month of history.
    /// </summary>
    public record SavingPace(DateOnly Since, decimal? InvestedPerMonth, decimal? SavedPerMonth, List<PaceEntry> Entries);

    /// <summary>
    /// Money in or out behind the pace: a day's net deposits into the investment accounts (shares moved in
    /// count at their value), or the change from one entered savings balance to the next.
    /// </summary>
    public record PaceEntry(DateOnly Date, PaceKind Kind, string Account, decimal Amount);

    public enum PaceKind { Investment, Savings }

    public record DashboardDto(
        string Currency,
        SeriesPoint? Latest,
        decimal? Change,
        DateOnly? ChangeSince,
        List<SeriesPoint> Series,
        List<AccountValue> Accounts,
        GoalProgress? Goal,
        SavingPace? Pace = null);

    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        // includeHome=false leaves homes, and loans taken for them, out of everything: totals, chart, change and goal progress.
        api.MapGet("/dashboard", async (bool? includeHome, ClaimsPrincipal principal, FireCalcDbContext db, PortfolioHistory history, PortfolioValuation valuation, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var goal = await db.Goals.AsNoTracking().SingleOrDefaultAsync(g => g.UserId == user.Id, ct);
            var accounts = await db.Accounts.AsNoTracking()
                .Where(a => a.UserId == user.Id && a.Type != AccountType.Property)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);
            var accountIds = accounts.Select(a => a.Id).ToList();
            var balances = (await db.Balances.AsNoTracking().Where(b => accountIds.Contains(b.AccountId)).ToListAsync(ct))
                .GroupBy(b => b.AccountId)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Date).ToList());
            // Homes count with their value less their loans; archived homes and loans don't count.
            var homes = includeHome == false ? [] : await db.Homes.AsNoTracking()
                .Where(h => h.UserId == user.Id && !h.Archived)
                .OrderBy(h => h.CreatedAt)
                .ToListAsync(ct);
            var homeData = await HomeData.LoadAsync(db, homes.Select(h => h.Id).ToList(), ct);
            List<Mortgage> LoansOf(Home h) => homeData.Loans.GetValueOrDefault(h.Id, []).Where(m => !m.Archived).ToList();
            decimal Owed(Mortgage m, DateOnly day) =>
                MortgageMath.OwedOn(homeData.Statements.GetValueOrDefault(m.Id, []), MortgageMath.Terms.Of(m), day) ?? 0;
            decimal? ValueOn(Home h, DateOnly day) => homeData.Values.GetValueOrDefault(h.Id)?.LastOrDefault(v => v.Date <= day)?.Value;
            decimal? EquityOn(Home h, DateOnly day) =>
                ValueOn(h, day) is { } value ? value - LoansOf(h).Sum(m => Owed(m, day)) : null;

            // Accounts with transactions are valued from them: daily for the history, live for today.
            var daily = await history.DailyAsync(user.Id, user.Currency, today, ct);
            var days = daily.ToDictionary(d => d.Date, d => d.Value);
            var live = (await valuation.ValueAsync(user.Id, user.Currency, today, refresh: false, ct)).ToDictionary(a => a.AccountId, a => a.Value);
            var manual = accounts.Where(a => !live.ContainsKey(a.Id)).ToList();

            // A balance counts until a newer one; an archived account stops counting after its last one.
            decimal? BalanceOn(Account account, DateOnly day)
            {
                if (!balances.TryGetValue(account.Id, out var list)) return null;
                if (account.Archived && day > list[^1].Date) return null;
                var balance = list.LastOrDefault(b => b.Date <= day);
                return balance is null ? null : account.Type == AccountType.Loan ? -balance.Balance : balance.Balance;
            }

            SeriesPoint PointOn(DateOnly day)
            {
                var byType = new Dictionary<AccountType, decimal>();
                void Add(AccountType type, decimal amount) => byType[type] = Math.Round(byType.GetValueOrDefault(type) + amount, 2);

                if (day == today)
                    foreach (var a in accounts.Where(a => live.ContainsKey(a.Id))) Add(a.Type, live[a.Id]);
                else if (days.TryGetValue(day, out var invested))
                    Add(AccountType.Investment, invested);
                foreach (var a in manual)
                    if (BalanceOn(a, day) is { } balance) Add(a.Type, balance);
                foreach (var h in homes)
                    if (EquityOn(h, day) is { } equity) Add(AccountType.Property, equity);
                return new SeriesPoint(day, byType.Values.Sum(), byType);
            }

            var homeDates = homeData.Values.Values.SelectMany(l => l.Select(v => v.Date))
                .Concat(homeData.Statements.Values.SelectMany(l => l.Select(b => b.Date)))
                .ToList();
            var starts = balances.Values.Select(l => l[0].Date).Concat(days.Keys.Take(1)).Concat(homeData.Values.Values.Select(l => l[0].Date)).ToList();
            if (starts.Count == 0)
                return new DashboardDto(user.Currency, null, null, null, [], [], Progress(goal, null));
            var first = starts.Min();

            // A point at every balance entered and at each month end, so the investments show between them.
            var dates = new SortedSet<DateOnly>(balances.Values.SelectMany(l => l.Select(b => b.Date)).Concat(homeDates).Where(d => d >= first && d < today)) { first, today };
            for (var monthEnd = new DateOnly(first.Year, first.Month, 1).AddMonths(1).AddDays(-1); monthEnd < today; monthEnd = monthEnd.AddDays(1).AddMonths(1).AddDays(-1))
                if (monthEnd >= first) dates.Add(monthEnd);
            var series = dates.Select(PointOn).ToList();
            var latest = series[^1];

            var monthAgo = today.AddMonths(-1);
            var (change, since) = monthAgo >= first ? (latest.Total - PointOn(monthAgo).Total, monthAgo) : ((decimal?)null, (DateOnly?)null);

            var values = accounts
                .Where(a => !a.Archived)
                .Select(a => live.TryGetValue(a.Id, out var value)
                    ? new AccountValue(a.Id, a.Name, a.Type, value, true, null)
                    : new AccountValue(a.Id, a.Name, a.Type, BalanceOn(a, today) ?? 0, false, balances.GetValueOrDefault(a.Id)?[^1].Date))
                .Concat(homes.Where(h => homeData.Values.ContainsKey(h.Id)).Select(h =>
                {
                    var loans = LoansOf(h).Select(m => new LoanTerms(m.Name, Owed(m, today), m.InterestPct, m.ContributionPct, m.EndDate, m.InterestOnlyUntil)).ToList();
                    var value = ValueOn(h, today) ?? 0;
                    var owed = loans.Sum(l => l.Owed);
                    return new AccountValue(h.Id, h.Name, AccountType.Property, value - owed, false, homeData.Values[h.Id][^1].Date, value, owed, loans);
                }))
                .ToList();

            // Which accounts the money behind each day's change in net deposits went in or out of.
            var paceFrom = today.AddYears(-1).AddDays(-1);
            var moves = await db.Transactions.AsNoTracking()
                .Where(x => x.Date >= paceFrom && (x.Type == TransactionType.Deposit || x.Type == TransactionType.Withdrawal
                    || x.Type == TransactionType.SecurityIn || x.Type == TransactionType.SecurityOut))
                .Join(db.Accounts.Where(a => a.UserId == user.Id), x => x.AccountId, a => a.Id, (x, a) => new { x.Date, a.Name })
                .ToListAsync(ct);
            var movedOn = moves.GroupBy(m => m.Date).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(m => m.Name).Distinct()));

            return new DashboardDto(user.Currency, latest, change, since, series, values, Progress(goal, latest), Pace());

            // Per month over the last year, or over the shorter time there is data for.
            SavingPace Pace()
            {
                var yearAgo = today.AddYears(-1);
                var entries = new List<PaceEntry>();
                static decimal? PerMonth(decimal change, DateOnly from, DateOnly to)
                {
                    var months = (to.DayNumber - from.DayNumber) / (365.2425m / 12);
                    return months >= 1 ? Math.Round(change / months, 2) : null;
                }
                // Only money going in counts, so a one-off sale or withdrawal (say for a house) doesn't turn
                // the pace negative; ordinary saving is what the projection carries forward.
                static decimal In(IEnumerable<PaceEntry> list) => list.Where(e => e.Amount > 0).Sum(e => e.Amount);

                decimal? invested = null;
                if (daily.Count > 0)
                {
                    var start = daily.LastOrDefault(d => d.Date <= yearAgo) ?? daily[0];
                    var mine = daily.SkipWhile(d => d.Date < start.Date).Zip(daily.SkipWhile(d => d.Date <= start.Date))
                        .Where(p => Math.Round(p.Second.PutIn - p.First.PutIn, 2) != 0)
                        .Select(p => new PaceEntry(p.Second.Date, PaceKind.Investment, movedOn.GetValueOrDefault(p.Second.Date, ""),
                            Math.Round(p.Second.PutIn - p.First.PutIn, 2)))
                        .ToList();
                    entries.AddRange(mine);
                    invested = PerMonth(In(mine), start.Date, today);
                }

                // Each savings account from its balance a year ago, or from its first balance if it is newer,
                // so an account entered recently doesn't count its whole balance as saved.
                decimal? saved = null;
                foreach (var a in manual.Where(a => a.Type is AccountType.Savings or AccountType.Cash && !a.Archived))
                {
                    if (!balances.TryGetValue(a.Id, out var list)) continue;
                    var start = list.LastOrDefault(b => b.Date <= yearAgo) ?? list[0];
                    var since = list.Where(b => b.Date >= start.Date).ToList();
                    var mine = since.Zip(since.Skip(1))
                        .Where(p => p.Second.Balance != p.First.Balance)
                        .Select(p => new PaceEntry(p.Second.Date, PaceKind.Savings, a.Name, p.Second.Balance - p.First.Balance))
                        .ToList();
                    if (PerMonth(In(mine), start.Date, today) is { } perMonth)
                    {
                        entries.AddRange(mine);
                        saved = (saved ?? 0) + perMonth;
                    }
                }
                return new SavingPace(yearAgo, invested, saved, entries.OrderByDescending(e => e.Date).ToList());
            }
        });
    }

    private static GoalProgress? Progress(Goal? goal, SeriesPoint? latest)
    {
        if (goal is null) return null;
        var current = latest?.Total ?? 0;
        return new GoalProgress(
            GoalEndpoints.ToDto(goal),
            current,
            Math.Max(0, goal.TargetAmount - current),
            Math.Round(current / goal.TargetAmount * 100, 1));
    }
}
