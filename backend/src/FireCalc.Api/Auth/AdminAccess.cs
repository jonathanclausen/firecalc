using System.Security.Claims;

namespace FireCalc.Api.Auth;

/// <summary>
/// Who may open the admin pages: the verified emails in <c>Auth:AdminEmails</c>. Kept apart from sign-up
/// (<c>Auth:AllowedEmails</c>), so opening sign-up never makes anyone an admin.
/// </summary>
public sealed class AdminAccess
{
    private readonly HashSet<string> _emails;

    public AdminAccess(IConfiguration config)
    {
        var emails = config.GetSection("Auth:AdminEmails").Get<string[]>() ?? [];
        _emails = emails.Select(e => e.Trim()).Where(e => e.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAdmin(ClaimsPrincipal user) =>
        user.FindFirstValue("email") is { } email
        && string.Equals(user.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)
        && _emails.Contains(email);
}
