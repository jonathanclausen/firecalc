using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class ScenarioEndpoints
{
    public record ScenarioDto(
        Guid Id,
        string Name,
        decimal MonthlySavings,
        decimal InvestmentReturnPct,
        decimal SavingsReturnPct,
        decimal HomeGrowthPct,
        decimal InflationPct,
        decimal FireAge,
        decimal YearlySpending,
        List<ScenarioEvent> Events);

    public record SaveScenarioRequest(
        string? Name,
        decimal MonthlySavings,
        decimal InvestmentReturnPct,
        decimal SavingsReturnPct,
        decimal HomeGrowthPct,
        decimal InflationPct,
        decimal FireAge,
        decimal YearlySpending,
        List<ScenarioEvent>? Events);

    private const int MaxEvents = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void MapScenarioEndpoints(this RouteGroupBuilder api)
    {
        var scenarios = api.MapGroup("/scenarios");

        scenarios.MapGet("/", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var list = await db.Scenarios.AsNoTracking()
                .Where(s => s.UserId == user.Id)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            return list.Select(ToDto).ToList();
        });

        scenarios.MapPost("/", async (SaveScenarioRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = Validate(req);
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var s = new Scenario { UserId = user.Id, Name = "" };
            Apply(s, req);
            db.Scenarios.Add(s);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/scenarios/{s.Id}", ToDto(s));
        });

        scenarios.MapPut("/{id:guid}", async (Guid id, SaveScenarioRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = Validate(req);
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var s = await db.Scenarios.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct);
            if (s is null) return Results.NotFound();
            Apply(s, req);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(s));
        });

        scenarios.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var deleted = await db.Scenarios.Where(x => x.Id == id && x.UserId == user.Id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static Validation Validate(SaveScenarioRequest req)
    {
        static bool Pct(decimal value) => value is >= -50 and <= 50;
        var events = req.Events ?? [];
        return new Validation()
            .Check(!string.IsNullOrWhiteSpace(req.Name) && req.Name.Trim().Length <= 100, "name", "Name is required and must be at most 100 characters.")
            .Check(req.MonthlySavings is >= 0 and < 100_000_000, "monthlySavings", "Monthly savings must be zero or more.")
            .Check(req.YearlySpending is >= 0 and < 1_000_000_000, "yearlySpending", "Yearly spending must be zero or more.")
            .Check(Pct(req.InvestmentReturnPct) && Pct(req.SavingsReturnPct) && Pct(req.HomeGrowthPct) && Pct(req.InflationPct), "rates", "Rates must be between -50 and 50 %.")
            .Check(req.FireAge is >= 0 and <= 120, "fireAge", "FIRE age must be between 0 and 120.")
            .Check(events.Count <= MaxEvents, "events", $"At most {MaxEvents} events.")
            .Check(events.All(e => e.Age is >= 0 and <= 120), "events", "Each event needs an age between 0 and 120.")
            .Check(events.All(e => e.Kind != ScenarioEventKind.Break || e.Years is > 0 and <= 100), "events", "A break needs a length in years.")
            .Check(events.All(e => e.Kind == ScenarioEventKind.Break || e.Amount is not null), "events", "The event needs an amount.")
            .Check(events.All(e => e.Kind != ScenarioEventKind.Savings || e.Amount >= 0), "events", "Monthly savings must be zero or more.");
    }

    private static void Apply(Scenario s, SaveScenarioRequest req)
    {
        s.Name = req.Name!.Trim();
        s.MonthlySavings = req.MonthlySavings;
        s.InvestmentReturnPct = req.InvestmentReturnPct;
        s.SavingsReturnPct = req.SavingsReturnPct;
        s.HomeGrowthPct = req.HomeGrowthPct;
        s.InflationPct = req.InflationPct;
        s.FireAge = req.FireAge;
        s.YearlySpending = req.YearlySpending;
        // Keep only what each kind uses, ordered by age.
        var events = (req.Events ?? [])
            .Select(e => e with
            {
                Years = e.Kind == ScenarioEventKind.Break ? e.Years : null,
                Amount = e.Kind == ScenarioEventKind.Break ? null : e.Amount,
            })
            .OrderBy(e => e.Age)
            .ToList();
        s.Events = JsonSerializer.Serialize(events, Json);
    }

    private static ScenarioDto ToDto(Scenario s) => new(
        s.Id, s.Name, s.MonthlySavings, s.InvestmentReturnPct, s.SavingsReturnPct, s.HomeGrowthPct,
        s.InflationPct, s.FireAge, s.YearlySpending,
        JsonSerializer.Deserialize<List<ScenarioEvent>>(s.Events, Json) ?? []);
}
