using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using FireCalc.Api.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class DashboardEndpoints
{
    public record SeriesPoint(DateOnly Date, decimal Total, Dictionary<AccountType, decimal> ByType);
    public record GoalProgress(GoalEndpoints.GoalDto Goal, decimal Current, decimal Remaining, decimal ProgressPct);

    /// <summary>
    /// An account's part of today's net worth. Tracked accounts are valued live from their transactions;
    /// the others count with the balance entered on <see cref="BalanceDate"/>. A home counts with its equity
    /// (value less loan) and a loan with what is owed, as a negative value. For a home, HomeValue and HomeLoan
    /// are the two parts of its equity, so a projection can grow the value on its own.
    /// </summary>
    public record AccountValue(Guid Id, string Name, AccountType Type, decimal Value, bool Tracked, DateOnly? BalanceDate, decimal? HomeValue = null, decimal? HomeLoan = null);

    /// <summary>
    /// How much was put aside per month lately, from up to the last 12 months: money put into the investment
    /// accounts and the growth of savings and cash balances. Months where money went out (a sale or a drop
    /// in balance) count as zero rather than negative. Each part is null until it has a month of history.
    /// </summary>
    public record SavingPace(DateOnly Since, decimal? InvestedPerMonth, decimal? SavedPerMonth);

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
                .Where(a => a.UserId == user.Id && (includeHome != false || (a.Type != AccountType.Property && !a.PartOfHome)))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);
            var accountIds = accounts.Select(a => a.Id).ToList();
            var balances = (await db.Balances.AsNoTracking().Where(b => accountIds.Contains(b.AccountId)).ToListAsync(ct))
                .GroupBy(b => b.AccountId)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Date).ToList());

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
                return balance is null ? null : account.Type switch
                {
                    AccountType.Property => balance.Balance - (balance.Loan ?? 0),
                    AccountType.Loan => -balance.Balance,
                    _ => balance.Balance,
                };
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
                return new SeriesPoint(day, byType.Values.Sum(), byType);
            }

            var starts = balances.Values.Select(l => l[0].Date).Concat(days.Keys.Take(1)).ToList();
            if (starts.Count == 0)
                return new DashboardDto(user.Currency, null, null, null, [], [], Progress(goal, null));
            var first = starts.Min();

            // A point at every balance entered and at each month end, so the investments show between them.
            var dates = new SortedSet<DateOnly>(balances.Values.SelectMany(l => l.Select(b => b.Date)).Where(d => d < today)) { first, today };
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
                    : new AccountValue(a.Id, a.Name, a.Type, BalanceOn(a, today) ?? 0, false, balances.GetValueOrDefault(a.Id)?[^1].Date,
                        a.Type == AccountType.Property ? balances.GetValueOrDefault(a.Id)?[^1].Balance : null,
                        a.Type == AccountType.Property ? balances.GetValueOrDefault(a.Id)?[^1].Loan ?? 0 : null))
                .ToList();

            return new DashboardDto(user.Currency, latest, change, since, series, values, Progress(goal, latest), Pace());

            // Per month over the last year, or over the shorter time there is data for.
            SavingPace Pace()
            {
                var yearAgo = today.AddYears(-1);
                static decimal? PerMonth(decimal change, DateOnly from, DateOnly to)
                {
                    var months = (to.DayNumber - from.DayNumber) / (365.2425m / 12);
                    return months >= 1 ? Math.Round(change / months, 2) : null;
                }
                // Only the rises count, so a one-off sale or withdrawal (say for a house) doesn't turn the
                // pace negative; ordinary saving is what the projection carries forward.
                static decimal Ups(IEnumerable<decimal> values) =>
                    values.Zip(values.Skip(1), (a, b) => Math.Max(0, b - a)).Sum();

                decimal? invested = null;
                if (daily.Count > 0)
                {
                    var start = daily.LastOrDefault(d => d.Date <= yearAgo) ?? daily[0];
                    var monthEnds = daily.Where(d => d.Date > start.Date)
                        .GroupBy(d => (d.Date.Year, d.Date.Month))
                        .Select(g => g.Last().PutIn);
                    invested = PerMonth(Ups(monthEnds.Prepend(start.PutIn)), start.Date, today);
                }

                // Each savings account from its balance a year ago, or from its first balance if it is newer,
                // so an account entered recently doesn't count its whole balance as saved.
                decimal? saved = null;
                foreach (var a in manual.Where(a => a.Type is AccountType.Savings or AccountType.Cash && !a.Archived))
                {
                    if (!balances.TryGetValue(a.Id, out var list)) continue;
                    var start = list.LastOrDefault(b => b.Date <= yearAgo) ?? list[0];
                    var since = list.Where(b => b.Date >= start.Date).Select(b => b.Balance);
                    if (PerMonth(Ups(since), start.Date, today) is { } perMonth)
                        saved = (saved ?? 0) + perMonth;
                }
                return new SavingPace(yearAgo, invested, saved);
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
