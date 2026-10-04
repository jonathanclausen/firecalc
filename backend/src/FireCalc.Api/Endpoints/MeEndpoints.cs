using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class MeEndpoints
{
    public record MeDto(string Email, string? Name, string Currency);

    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            return new MeDto(user.Email, user.Name, user.Currency);
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
