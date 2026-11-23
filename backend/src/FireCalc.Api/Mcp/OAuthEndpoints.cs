using System.Security.Claims;
using System.Text.Json.Serialization;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Mcp;

/// <summary>
/// A small OAuth 2.1 authorization server so MCP clients such as Claude can connect on a user's behalf.
/// Firebase can't act as one, so FireCalc hands out its own tokens: a client registers itself
/// (dynamic client registration), sends the person to the approval page in the app (/planner/connect),
/// where they sign in as usual and approve, and then swaps the one-time code for tokens with PKCE.
/// Tokens are random strings stored as hashes; a connection is one <see cref="OAuthGrant"/> row, which
/// the person can delete on "Din konto".
/// </summary>
public static class OAuthEndpoints
{
    public const string RateLimitPolicy = "oauth";
    private const int MaxRedirectUris = 5;
    private const int MaxRedirectUriLength = 500;

    public record RegisterRequest(
        [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris,
        [property: JsonPropertyName("client_name")] string? ClientName);

    public record RegisterResponse(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_id_issued_at")] long IssuedAt,
        [property: JsonPropertyName("client_name")] string ClientName,
        [property: JsonPropertyName("redirect_uris")] List<string> RedirectUris,
        [property: JsonPropertyName("token_endpoint_auth_method")] string AuthMethod,
        [property: JsonPropertyName("grant_types")] string[] GrantTypes,
        [property: JsonPropertyName("response_types")] string[] ResponseTypes);

    public record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("scope")] string Scope);

    /// <summary>What the approval page shows: the app's own name and where it will send the person back to.</summary>
    public record ClientInfo(string Name, string RedirectHost);

    /// <summary>The query of the authorization request, passed on by the approval page.</summary>
    public record AuthorizeRequest(
        string? ClientId, string? RedirectUri, string? ResponseType, string? CodeChallenge, string? CodeChallengeMethod,
        string? State, bool Approve);

    public record AuthorizeResult(string RedirectUrl);

    public record ConnectionDto(Guid Id, string ClientName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

    public const string Scope = "read";

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app, RouteGroupBuilder api)
    {
        // RFC 9728: where the MCP endpoint's tokens come from. Clients ask at both the root and the /mcp suffix.
        IResult ProtectedResource(HttpRequest request) => Results.Json(new Dictionary<string, object>
        {
            ["resource"] = PublicUrl.McpResource(request),
            ["authorization_servers"] = new[] { PublicUrl.Of(request) },
            ["bearer_methods_supported"] = new[] { "header" },
            ["scopes_supported"] = new[] { Scope },
            ["resource_name"] = "FireCalc",
        });
        app.MapGet("/.well-known/oauth-protected-resource", ProtectedResource);
        app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResource);

        // RFC 8414 metadata for this authorization server.
        app.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
        {
            var issuer = PublicUrl.Of(request);
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["authorization_endpoint"] = issuer + "/planner/connect",
                ["token_endpoint"] = issuer + "/oauth/token",
                ["registration_endpoint"] = issuer + "/oauth/register",
                ["response_types_supported"] = new[] { "code" },
                ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "none" },
                ["scopes_supported"] = new[] { Scope },
            });
        });

        app.MapPost("/oauth/register", async (RegisterRequest req, FireCalcDbContext db, CancellationToken ct) =>
        {
            var uris = req.RedirectUris ?? [];
            if (uris.Count is 0 or > MaxRedirectUris || !uris.All(IsAllowedRedirect))
                return Error("invalid_redirect_uri", "redirect_uris must be 1 to 5 https (or http on localhost) addresses.");

            // Registrations nobody went on to use are cleared after a while, so open registration can't pile up.
            var stale = DateTimeOffset.UtcNow.AddDays(-30);
            await db.OAuthClients
                .Where(c => c.CreatedAt < stale && !db.OAuthGrants.Any(g => g.ClientId == c.Id))
                .ExecuteDeleteAsync(ct);
            await db.OAuthCodes.Where(c => c.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync(ct);

            var name = string.IsNullOrWhiteSpace(req.ClientName) ? "MCP client" : req.ClientName.Trim();
            var client = new OAuthClient
            {
                Id = OAuthTokens.NewClientId(),
                Name = name[..Math.Min(name.Length, 100)],
                RedirectUris = uris,
            };
            db.OAuthClients.Add(client);
            await db.SaveChangesAsync(ct);
            return Results.Json(
                new RegisterResponse(client.Id, client.CreatedAt.ToUnixTimeSeconds(), client.Name, client.RedirectUris,
                    "none", ["authorization_code", "refresh_token"], ["code"]),
                statusCode: StatusCodes.Status201Created);
        }).RequireRateLimiting(RateLimitPolicy);

        app.MapPost("/oauth/token", async (HttpContext http, FireCalcDbContext db, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.HasFormContentType) return Error("invalid_request", "Send the request as a form.");
            var form = await http.Request.ReadFormAsync(ct);
            var clientId = form["client_id"].ToString();
            var now = DateTimeOffset.UtcNow;

            switch (form["grant_type"].ToString())
            {
                case "authorization_code":
                {
                    var hash = OAuthTokens.Hash(form["code"].ToString());
                    var code = await db.OAuthCodes.SingleOrDefaultAsync(c => c.CodeHash == hash, ct);
                    if (code is null) return Error("invalid_grant", "The code is unknown or already used.");
                    // A code works once, whatever happens next.
                    db.OAuthCodes.Remove(code);
                    await db.SaveChangesAsync(ct);
                    if (code.ExpiresAt <= now
                        || code.ClientId != clientId
                        || code.RedirectUri != form["redirect_uri"].ToString()
                        || !OAuthTokens.VerifierMatches(form["code_verifier"].ToString(), code.CodeChallenge))
                        return Error("invalid_grant", "The code, client, redirect address or code verifier does not match.");

                    var client = await db.OAuthClients.AsNoTracking().SingleAsync(c => c.Id == code.ClientId, ct);
                    var grant = new OAuthGrant
                    {
                        UserId = code.UserId,
                        ClientId = client.Id,
                        ClientName = client.Name,
                        AccessTokenHash = "",
                        RefreshTokenHash = "",
                    };
                    var tokens = Issue(grant, now);
                    db.OAuthGrants.Add(grant);
                    await db.SaveChangesAsync(ct);
                    return Results.Json(tokens);
                }
                case "refresh_token":
                {
                    var hash = OAuthTokens.Hash(form["refresh_token"].ToString());
                    var grant = await db.OAuthGrants.SingleOrDefaultAsync(g => g.RefreshTokenHash == hash, ct);
                    if (grant is null || grant.RefreshExpiresAt <= now || grant.ClientId != clientId)
                        return Error("invalid_grant", "The refresh token is unknown, expired or for another client.");
                    // Each refresh hands out a new refresh token and retires the old one.
                    var tokens = Issue(grant, now);
                    await db.SaveChangesAsync(ct);
                    return Results.Json(tokens);
                }
                default:
                    return Error("unsupported_grant_type", "Use authorization_code or refresh_token.");
            }
        }).DisableAntiforgery().RequireRateLimiting(RateLimitPolicy);

        // The approval page, signed in with Firebase like the rest of the app.
        var oauth = api.MapGroup("/oauth");

        oauth.MapGet("/client", async (string? clientId, string? redirectUri, FireCalcDbContext db, CancellationToken ct) =>
        {
            var client = await FindClientAsync(db, clientId, redirectUri, ct);
            return client is null
                ? Results.Problem("The app or its return address is not registered.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(new ClientInfo(client.Name, new Uri(redirectUri!).Host));
        });

        oauth.MapPost("/authorize", async (AuthorizeRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            // Never send the person on to an address the app didn't register.
            var client = await FindClientAsync(db, req.ClientId, req.RedirectUri, ct);
            if (client is null)
                return Results.Problem("The app or its return address is not registered.", statusCode: StatusCodes.Status400BadRequest);

            string Back(params (string Key, string? Value)[] query) =>
                Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(req.RedirectUri!,
                    query.Append((Key: "state", Value: req.State)).Where(q => q.Value is not null).Select(q => KeyValuePair.Create(q.Key, q.Value)));

            if (!req.Approve) return Results.Ok(new AuthorizeResult(Back(("error", "access_denied"))));
            if (req.ResponseType != "code")
                return Results.Ok(new AuthorizeResult(Back(("error", "unsupported_response_type"))));
            if (req.CodeChallengeMethod != "S256" || string.IsNullOrEmpty(req.CodeChallenge) || req.CodeChallenge.Length > 128)
                return Results.Ok(new AuthorizeResult(Back(("error", "invalid_request"), ("error_description", "PKCE with S256 is required."))));

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var code = OAuthTokens.New(OAuthTokens.CodePrefix);
            db.OAuthCodes.Add(new OAuthCode
            {
                CodeHash = OAuthTokens.Hash(code),
                ClientId = client.Id,
                UserId = user.Id,
                RedirectUri = req.RedirectUri!,
                CodeChallenge = req.CodeChallenge,
                ExpiresAt = DateTimeOffset.UtcNow + OAuthTokens.CodeLifetime,
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new AuthorizeResult(Back(("code", code))));
        });

        // "Forbundne apps" on Din konto.
        oauth.MapGet("/connections", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var now = DateTimeOffset.UtcNow;
            return await db.OAuthGrants.AsNoTracking()
                .Where(g => g.UserId == user.Id && g.RefreshExpiresAt > now)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new ConnectionDto(g.Id, g.ClientName, g.CreatedAt, g.LastUsedAt))
                .ToListAsync(ct);
        });

        oauth.MapDelete("/connections/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var deleted = await db.OAuthGrants.Where(g => g.Id == id && g.UserId == user.Id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static TokenResponse Issue(OAuthGrant grant, DateTimeOffset now)
    {
        var access = OAuthTokens.New(OAuthTokens.AccessPrefix);
        var refresh = OAuthTokens.New(OAuthTokens.RefreshPrefix);
        grant.AccessTokenHash = OAuthTokens.Hash(access);
        grant.AccessExpiresAt = now + OAuthTokens.AccessLifetime;
        grant.RefreshTokenHash = OAuthTokens.Hash(refresh);
        grant.RefreshExpiresAt = now + OAuthTokens.RefreshLifetime;
        return new TokenResponse(access, "Bearer", (int)OAuthTokens.AccessLifetime.TotalSeconds, refresh, Scope);
    }

    private static async Task<OAuthClient?> FindClientAsync(FireCalcDbContext db, string? clientId, string? redirectUri, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri)) return null;
        var client = await db.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId, ct);
        return client is not null && client.RedirectUris.Contains(redirectUri) ? client : null;
    }

    /// <summary>https anywhere, or plain http back to this machine for desktop apps. No fragments.</summary>
    public static bool IsAllowedRedirect(string uri) =>
        uri.Length <= MaxRedirectUriLength
        && Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && string.IsNullOrEmpty(u.Fragment)
        && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    private static IResult Error(string error, string description) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description },
            statusCode: StatusCodes.Status400BadRequest);
}
