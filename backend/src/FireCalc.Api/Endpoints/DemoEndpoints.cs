using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

/// <summary>
/// "Prøv med eksempeldata": a made-up household with two years of history, so a new user can look around
/// before typing in their own numbers. Everything it adds is flagged and removed in one go.
/// </summary>
public static class DemoEndpoints
{
    private const int Months = 24;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private record Names(string Prefix, string Depot, string Savings, string Cash, string Home, string CarLoan, string Scenario);

    private static readonly Names Danish = new("Eksempel", "Aktiedepot", "Opsparingskonto", "Lønkonto", "Lejlighed", "Billån", "Som nu");
    private static readonly Names English = new("Example", "Share account", "Savings account", "Current account", "Flat", "Car loan", "As now");

    public static void MapDemoEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/demo", async (string? lang, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            if (await db.Accounts.AnyAsync(a => a.UserId == user.Id && a.IsDemo, ct))
                return Results.Problem("The example data is already loaded.", statusCode: StatusCodes.Status409Conflict);

            var n = lang == "en" ? English : Danish;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            // Month ends going back two years, ending today.
            var dates = Enumerable.Range(0, Months)
                .Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i - Months + 1).AddDays(-1))
                .Append(today)
                .ToList();

            Account Add(string name, AccountType type, bool partOfHome = false)
            {
                var a = new Account { UserId = user.Id, Name = $"{n.Prefix} · {name}", Type = type, PartOfHome = partOfHome, IsDemo = true };
                db.Accounts.Add(a);
                return a;
            }

            void Balances(Account a, Func<int, decimal> balance, Func<int, decimal?>? loan = null)
            {
                for (var i = 0; i < dates.Count; i++)
                    db.Balances.Add(new AccountBalance
                    {
                        AccountId = a.Id,
                        Date = dates[i],
                        Balance = Math.Round(balance(i), 0),
                        Loan = loan?.Invoke(i) is { } l ? Math.Round(l, 0) : null,
                    });
            }

            // A depot growing about 7 % a year with 6.000 kr. added each month, with a dip in the middle.
            var depot = 180_000m;
            var depotValues = new List<decimal>();
            for (var i = 0; i < dates.Count; i++)
            {
                if (i > 0) depot = depot * (i is 9 or 10 ? 0.97m : 1.0057m) + 6_000m;
                depotValues.Add(depot);
            }
            Balances(Add(n.Depot, AccountType.Investment), i => depotValues[i]);
            Balances(Add(n.Savings, AccountType.Savings), i => 60_000m + 1_500m * i);
            Balances(Add(n.Cash, AccountType.Cash), i => 22_000m + (i % 3) * 4_000m);
            Balances(Add(n.Home, AccountType.Property), i => 2_600_000m + 4_300m * i, i => 2_050_000m - 3_800m * i);
            Balances(Add(n.CarLoan, AccountType.Loan), i => Math.Max(0, 120_000m - 2_500m * i));

            if (!await db.Goals.AnyAsync(g => g.UserId == user.Id, ct))
                db.Goals.Add(new Goal { UserId = user.Id, TargetAmount = 7_500_000m, ExpectedAnnualReturnPct = 7m, IsDemo = true });

            db.Scenarios.Add(new Scenario
            {
                UserId = user.Id,
                Name = $"{n.Prefix} · {n.Scenario}",
                MonthlySavings = 7_500m,
                InvestmentReturnPct = 7m,
                SavingsReturnPct = 1.5m,
                HomeGrowthPct = 2m,
                InflationPct = 2m,
                FireAge = 55m,
                YearlySpending = 300_000m,
                WithdrawalPct = 4m,
                Events = JsonSerializer.Serialize(new List<ScenarioEvent>(), Json),
                IsDemo = true,
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(await MeEndpoints.ToDtoAsync(db, user, ct));
        });

        api.MapDelete("/demo", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            // Balances and transactions go with their accounts (cascade).
            await db.Accounts.Where(a => a.UserId == user.Id && a.IsDemo).ExecuteDeleteAsync(ct);
            await db.Goals.Where(g => g.UserId == user.Id && g.IsDemo).ExecuteDeleteAsync(ct);
            await db.Scenarios.Where(s => s.UserId == user.Id && s.IsDemo).ExecuteDeleteAsync(ct);
            return Results.Ok(await MeEndpoints.ToDtoAsync(db, user, ct));
        });
    }
}
