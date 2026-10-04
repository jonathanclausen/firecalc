using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class GoalEndpoints
{
    public record GoalDto(string Name, decimal TargetAmount, DateOnly? TargetDate, decimal? ExpectedAnnualReturnPct);
    public record SaveGoalRequest(string? Name, decimal TargetAmount, DateOnly? TargetDate, decimal? ExpectedAnnualReturnPct);

    public static void MapGoalEndpoints(this RouteGroupBuilder api)
    {
        var goal = api.MapGroup("/goal");

        goal.MapGet("/", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var g = await db.Goals.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            return g is null ? Results.NoContent() : Results.Ok(ToDto(g));
        });

        goal.MapPut("/", async (SaveGoalRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = new Validation()
                .Check(req.TargetAmount > 0, "targetAmount", "Target amount must be greater than zero.")
                .Check(req.Name is null || req.Name.Trim().Length <= 100, "name", "Name must be at most 100 characters.")
                .Check(req.ExpectedAnnualReturnPct is null or >= -100 and <= 100, "expectedAnnualReturnPct", "Expected return must be between -100 and 100.");
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var g = await db.Goals.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (g is null)
            {
                g = new Goal { UserId = user.Id };
                db.Goals.Add(g);
            }

            g.Name = string.IsNullOrWhiteSpace(req.Name) ? "FIRE" : req.Name.Trim();
            g.TargetAmount = req.TargetAmount;
            g.TargetDate = req.TargetDate;
            g.ExpectedAnnualReturnPct = req.ExpectedAnnualReturnPct;
            g.IsDemo = false;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(g));
        });

        goal.MapDelete("/", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            await db.Goals.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }

    internal static GoalDto ToDto(Goal g) => new(g.Name, g.TargetAmount, g.TargetDate, g.ExpectedAnnualReturnPct);
}
