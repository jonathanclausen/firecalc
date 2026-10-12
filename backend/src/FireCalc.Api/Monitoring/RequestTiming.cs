using System.Diagnostics;
using System.Text.Json;

namespace FireCalc.Api.Monitoring;

/// <summary>
/// Writes one line per API request with its route and how long it took, in the JSON shape Cloud Logging
/// reads from stdout. A log-based metric in Cloud Monitoring turns these lines into latency percentiles
/// per endpoint (see deploy/monitoring). The route is the template, such as
/// <c>/api/accounts/{accountId:guid}/transactions</c>, so requests for different accounts count together.
/// </summary>
public sealed class RequestTimingLog(TextWriter output)
{
    /// <summary>Marks the lines, so the metric's filter picks out these and nothing else.</summary>
    public const string Kind = "request-timing";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Lock _lock = new();

    public void Write(string method, string route, int status, double latencyMs)
    {
        var line = JsonSerializer.Serialize(new
        {
            Severity = status >= 500 ? "ERROR" : "INFO",
            Message = $"{method} {route} {status} {latencyMs:0} ms",
            Kind,
            Method = method,
            Route = route,
            Status = status,
            LatencyMs = Math.Round(latencyMs, 1),
        }, Json);
        lock (_lock) output.WriteLine(line);
    }
}

public static class RequestTimingExtensions
{
    /// <summary>Times every request that matched an endpoint, when Monitoring:RequestLog is on.</summary>
    public static void UseRequestTiming(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Monitoring:RequestLog", false)) return;
        var log = app.Services.GetService<RequestTimingLog>() ?? new RequestTimingLog(Console.Out);
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                if (context.GetEndpoint() is RouteEndpoint endpoint)
                    log.Write(context.Request.Method, endpoint.RoutePattern.RawText ?? context.Request.Path, context.Response.StatusCode,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
    }
}
