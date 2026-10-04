using System.Security.Claims;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Auth;

public static class CurrentUser
{
    /// <summary>
    /// Finds the signed-in user's row, creating it on first sign-in. Firebase already joins Google,
    /// Facebook and password logins with the same email into one Firebase user. A row we don't know
    /// by its Firebase id yet but whose verified email matches (an account from before Firebase, or a
    /// Firebase user that was deleted and made again) is taken over instead of starting empty.
    /// </summary>
    public static async Task<User> GetOrCreateUserAsync(this FireCalcDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var subject = principal.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no sub claim.");
        var email = principal.FindFirstValue("email") ?? "";
        var name = principal.FindFirstValue("name");

        var user = await db.Users.SingleOrDefaultAsync(u => u.AuthSubject == subject, ct);
        if (user is null && AuthSetup.HasVerifiedEmail(principal))
        {
            user = await db.Users
                .Where(u => u.Email.ToLower() == email.ToLower())
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (user is not null) user.AuthSubject = subject;
        }

        if (user is null)
        {
            user = new User { AuthSubject = subject, Email = email, Name = name };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel request created the user first.
                db.ChangeTracker.Clear();
                user = await db.Users.SingleAsync(u => u.AuthSubject == subject, ct);
            }
            return user;
        }

        if (user.Email != email || (name is not null && user.Name != name))
        {
            user.Email = email;
            user.Name = name ?? user.Name;
        }
        if (db.ChangeTracker.HasChanges())
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel request took the row over first.
                db.ChangeTracker.Clear();
                user = await db.Users.SingleAsync(u => u.AuthSubject == subject, ct);
            }
        }

        return user;
    }
}
