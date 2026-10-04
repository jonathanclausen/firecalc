using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;

namespace FireCalc.Api.Endpoints;

public static class MeEndpoints
{
    public record MeDto(string Email, string? Name, string Currency, DateOnly? BirthDate);
    public record SaveProfileRequest(DateOnly? BirthDate);

    private static MeDto ToDto(User user) => new(user.Email, user.Name, user.Currency, user.BirthDate);

    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            return ToDto(user);
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
            return Results.Ok(ToDto(user));
        });
    }
}
