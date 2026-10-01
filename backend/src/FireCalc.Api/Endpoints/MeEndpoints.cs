using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;

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
    }
}
