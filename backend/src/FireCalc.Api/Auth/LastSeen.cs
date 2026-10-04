using System.Collections.Concurrent;
using System.Security.Claims;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Auth;

/// <summary>
/// Notes when a signed-in user last used the app, for the admin page. Written at most once an hour per
/// user, so ordinary requests don't each cost a database write.
/// </summary>
public sealed class LastSeenFilter(LastSeenThrottle throttle, FireCalcDbContext db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (context.HttpContext.User.FindFirstValue("sub") is { } subject && throttle.IsDue(subject))
        {
            var now = DateTimeOffset.UtcNow;
            var updated = await db.Users
                .Where(u => u.GoogleSubject == subject)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, now), context.HttpContext.RequestAborted);
            if (updated > 0) throttle.Mark(subject, now);
        }
        return result;
    }
}

public sealed class LastSeenThrottle
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _written = new();

    public bool IsDue(string subject) =>
        !_written.TryGetValue(subject, out var at) || DateTimeOffset.UtcNow - at >= Interval;

    public void Mark(string subject, DateTimeOffset at) => _written[subject] = at;
}
