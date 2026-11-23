namespace FireCalc.Api.Mcp;

/// <summary>
/// The address people and apps use to reach FireCalc, e.g. https://okonomi.concensur.dk. The API sits
/// behind the web server, which passes the address it was called on in X-Forwarded-Host and -Proto.
/// <c>Mcp:PublicUrl</c> overrides it.
/// </summary>
public static class PublicUrl
{
    public static string Of(HttpRequest request)
    {
        if (request.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Mcp:PublicUrl"] is { Length: > 0 } configured)
            return configured.TrimEnd('/');
        var host = First(request.Headers["X-Forwarded-Host"]) ?? request.Host.Value;
        var scheme = First(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;
        return $"{scheme}://{host}";
    }

    public static string McpResource(HttpRequest request) => Of(request) + "/mcp";

    public static string ResourceMetadata(HttpRequest request) => Of(request) + "/.well-known/oauth-protected-resource/mcp";

    private static string? First(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Count > 0 && values[0] is { Length: > 0 } v ? v.Split(',')[0].Trim() : null;
}
