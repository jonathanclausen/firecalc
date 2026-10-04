using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

/// <summary>
/// The admin page: who has signed up and how far they have come. Only counts, dates and yes/no leave
/// the API; no amounts, balances or goals. Everyone who isn't an admin gets 404, as if it didn't exist.
/// </summary>
public static class AdminEndpoints
{
    private const int Weeks = 12;

    /// <summary>The getting-started checklist, in its order. Same rules as the card on the overview.</summary>
    public record Steps(bool Account, bool Import, bool Goal, bool Profile, bool Scenario)
    {
        public int CountDone() => new[] { Account, Import, Goal, Profile, Scenario }.Count(x => x);
    }

    public record AdminUserDto(
        Guid Id, string Email, string? Name, string? SignInProvider, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt,
        bool Onboarded, bool HasDemo, Steps Steps, int StepsDone,
        int Accounts, int Homes, int Transactions, int Balances, int Scenarios);

    public record TotalsDto(int Users, int NewLast7Days, int NewLast30Days, int Activated, int ActiveLast7Days);
    public record WeekDto(DateOnly WeekStart, int Count);
    public record FunnelStepDto(string Key, int Count);
    public record OverviewDto(TotalsDto Totals, WeekDto[] SignUpsByWeek, FunnelStepDto[] Funnel, AdminUserDto[] Users);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                if (!http.RequestServices.GetRequiredService<AdminAccess>().IsAdmin(http.User))
                    return Results.NotFound();
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("FireCalc.Admin")
                    .LogInformation("Admin {Email} opened {Path}", http.User.FindFirstValue("email"), http.Request.Path);
                return await next(context);
            });

        // Lets the app show the "Admin" menu item.
        admin.MapGet("/access", () => Results.NoContent());

        admin.MapGet("/overview", async (FireCalcDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users.AsNoTracking()
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new { u.Id, u.Email, u.Name, u.SignInProvider, u.CreatedAt, u.LastSeenAt, u.OnboardedAt, u.BirthDate })
                .ToListAsync(ct);

            // Example data never counts as the user's own.
            var accounts = await db.Accounts.Where(a => !a.IsDemo)
                .GroupBy(a => a.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count(), Investment = g.Count(a => a.Type == AccountType.Investment) })
                .ToDictionaryAsync(x => x.UserId, ct);
            var transactions = await db.Transactions
                .Join(db.Accounts.Where(a => !a.IsDemo), t => t.AccountId, a => a.Id, (t, a) => a.UserId)
                .GroupBy(id => id)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
            var balances = await db.Balances
                .Join(db.Accounts.Where(a => !a.IsDemo), b => b.AccountId, a => a.Id, (b, a) => a.UserId)
                .GroupBy(id => id)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
            var homes = await db.Homes.Where(h => !h.IsDemo)
                .GroupBy(h => h.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
            var scenarios = await db.Scenarios.Where(s => !s.IsDemo)
                .GroupBy(s => s.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
            var goals = (await db.Goals.Where(g => !g.IsDemo).Select(g => g.UserId).ToListAsync(ct)).ToHashSet();
            var demo = (await db.Accounts.Where(a => a.IsDemo).Select(a => a.UserId).Distinct().ToListAsync(ct)).ToHashSet();

            var rows = users.Select(u =>
            {
                var acc = accounts.GetValueOrDefault(u.Id);
                var accountCount = acc?.Count ?? 0;
                var homeCount = homes.GetValueOrDefault(u.Id);
                var transactionCount = transactions.GetValueOrDefault(u.Id);
                var scenarioCount = scenarios.GetValueOrDefault(u.Id);
                var steps = new Steps(
                    Account: accountCount + homeCount > 0,
                    // Nothing to import without a share account, so then it counts as done.
                    Import: transactionCount > 0 || (accountCount > 0 && acc!.Investment == 0),
                    Goal: goals.Contains(u.Id),
                    Profile: u.BirthDate is not null,
                    Scenario: scenarioCount > 0);
                return new AdminUserDto(
                    u.Id, u.Email, u.Name, u.SignInProvider, u.CreatedAt, u.LastSeenAt, u.OnboardedAt is not null, demo.Contains(u.Id),
                    steps, steps.CountDone(), accountCount, homeCount, transactionCount, balances.GetValueOrDefault(u.Id), scenarioCount);
            }).ToArray();

            var now = DateTimeOffset.UtcNow;
            var totals = new TotalsDto(
                rows.Length,
                rows.Count(r => r.CreatedAt >= now.AddDays(-7)),
                rows.Count(r => r.CreatedAt >= now.AddDays(-30)),
                rows.Count(r => r.Steps.Account),
                rows.Count(r => r.LastSeenAt >= now.AddDays(-7)));

            // Weeks start on Monday (Danish weeks), oldest first, ending with this week.
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var weeks = Enumerable.Range(0, Weeks).Select(i => thisWeek.AddDays(7 * (i - Weeks + 1))).ToArray();
            var signUps = weeks.Select(start => new WeekDto(start, rows.Count(r =>
            {
                var day = DateOnly.FromDateTime(r.CreatedAt.UtcDateTime);
                return day >= start && day < start.AddDays(7);
            }))).ToArray();

            FunnelStepDto[] funnel =
            [
                new("signedUp", rows.Length),
                new("account", rows.Count(r => r.Steps.Account)),
                new("import", rows.Count(r => r.Steps.Import)),
                new("goal", rows.Count(r => r.Steps.Goal)),
                new("profile", rows.Count(r => r.Steps.Profile)),
                new("scenario", rows.Count(r => r.Steps.Scenario)),
            ];

            return new OverviewDto(totals, signUps, funnel, rows);
        });
    }
}
