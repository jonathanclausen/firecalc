using System.Security.Claims;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Auth;

public static class CurrentUser
{
    /// <summary>Finds the signed-in user's row, creating it on first sign-in.</summary>
    public static async Task<User> GetOrCreateUserAsync(this FireCalcDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var subject = principal.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no sub claim.");
        var email = principal.FindFirstValue("email") ?? "";
        var name = principal.FindFirstValue("name");

        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSubject == subject, ct);
        if (user is null)
        {
            user = new User { GoogleSubject = subject, Email = email, Name = name };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel request created the user first.
                db.ChangeTracker.Clear();
                user = await db.Users.SingleAsync(u => u.GoogleSubject == subject, ct);
            }
        }
        else if (user.Email != email || user.Name != name)
        {
            user.Email = email;
            user.Name = name;
            await db.SaveChangesAsync(ct);
        }

        return user;
    }
}
