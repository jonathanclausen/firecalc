using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Auth;

/// <summary>
/// Notes when a signed-in user last used the app and how they signed in, for the admin page. Written at
/// most once an hour per user and login, so ordinary requests don't each cost a database write.
/// </summary>
public sealed class LastSeenFilter(LastSeenThrottle throttle, FireCalcDbContext db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var principal = context.HttpContext.User;
        if (principal.FindFirstValue("sub") is { } subject)
        {
            var provider = SignInProvider(principal);
            var key = $"{subject}|{provider}";
            if (throttle.IsDue(key))
            {
                var now = DateTimeOffset.UtcNow;
                var updated = await db.Users
                    .Where(u => u.AuthSubject == subject)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.LastSeenAt, now)
                        .SetProperty(u => u.SignInProvider, u => provider ?? u.SignInProvider),
                        context.HttpContext.RequestAborted);
                if (updated > 0) throttle.Mark(key, now);
            }
        }
        return result;
    }

    /// <summary>Firebase's <c>firebase.sign_in_provider</c>: google.com, facebook.com or password.</summary>
    private static string? SignInProvider(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("firebase") is not { } json) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("sign_in_provider", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()![..Math.Min(p.GetString()!.Length, 40)]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class LastSeenThrottle
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _written = new();

    public bool IsDue(string key) =>
        !_written.TryGetValue(key, out var at) || DateTimeOffset.UtcNow - at >= Interval;

    public void Mark(string key, DateTimeOffset at) => _written[key] = at;
}
