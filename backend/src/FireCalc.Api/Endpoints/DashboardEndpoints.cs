using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class DashboardEndpoints
{
    public record SeriesPoint(DateOnly Date, decimal Total, Dictionary<AccountType, decimal> ByType);
    public record GoalProgress(GoalEndpoints.GoalDto Goal, decimal Current, decimal Remaining, decimal ProgressPct);
    public record DashboardDto(
        string Currency,
        SeriesPoint? Latest,
        decimal? ChangeSincePrevious,
        List<SeriesPoint> Series,
        GoalProgress? Goal);

    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var snapshots = await SnapshotEndpoints.Query(db, user.Id).OrderBy(s => s.Date).ToListAsync(ct);
            var goal = await db.Goals.AsNoTracking().SingleOrDefaultAsync(g => g.UserId == user.Id, ct);

            var series = snapshots
                .Select(s => new SeriesPoint(
                    s.Date,
                    s.Entries.Sum(x => x.Balance),
                    s.Entries.GroupBy(x => x.Account.Type).ToDictionary(g => g.Key, g => g.Sum(x => x.Balance))))
                .ToList();

            var latest = series.LastOrDefault();
            decimal? change = series.Count >= 2 ? latest!.Total - series[^2].Total : null;

            GoalProgress? progress = null;
            if (goal is not null)
            {
                var current = latest?.Total ?? 0;
                progress = new GoalProgress(
                    GoalEndpoints.ToDto(goal),
                    current,
                    Math.Max(0, goal.TargetAmount - current),
                    Math.Round(current / goal.TargetAmount * 100, 1));
            }

            return new DashboardDto(user.Currency, latest, change, series, progress);
        });
    }
}
