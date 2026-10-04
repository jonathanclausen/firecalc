using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class MeEndpoints
{
    public record MeDto(string Email, string? Name, string Currency, DateOnly? BirthDate, bool Onboarded, bool ChecklistHidden, bool HasDemo);
    public record SaveProfileRequest(DateOnly? BirthDate);
    /// <summary>Only the fields sent are changed.</summary>
    public record SaveOnboardingRequest(bool? Onboarded, bool? ChecklistHidden);

    internal static async Task<MeDto> ToDtoAsync(FireCalcDbContext db, User user, CancellationToken ct) => new(
        user.Email, user.Name, user.Currency, user.BirthDate,
        user.OnboardedAt is not null, user.ChecklistHiddenAt is not null,
        await db.Accounts.AnyAsync(a => a.UserId == user.Id && a.IsDemo, ct));

    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            return await ToDtoAsync(db, user, ct);
        });

        api.MapPut("/me/profile", async (SaveProfileRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var v = new Validation()
                .Check(req.BirthDate is null || (req.BirthDate <= today && req.BirthDate > today.AddYears(-120)), "birthDate", "Birth date must be in the past.");
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            user.BirthDate = req.BirthDate;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ToDtoAsync(db, user, ct));
        });

        api.MapPut("/me/onboarding", async (SaveOnboardingRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var now = DateTimeOffset.UtcNow;
            if (req.Onboarded is { } onboarded) user.OnboardedAt = onboarded ? user.OnboardedAt ?? now : null;
            if (req.ChecklistHidden is { } hidden) user.ChecklistHiddenAt = hidden ? user.ChecklistHiddenAt ?? now : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await ToDtoAsync(db, user, ct));
        });

        // "Slet min konto": removes the user and everything they entered. The login itself lives in
        // Firebase and is deleted by the browser.
        api.MapDelete("/me", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            // The retrying execution strategy only allows a transaction it can replay as a whole.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                // Old snapshot entries point at accounts without cascading, so they go first.
                await db.Snapshots.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
                await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            });
            return Results.NoContent();
        });
    }
}
