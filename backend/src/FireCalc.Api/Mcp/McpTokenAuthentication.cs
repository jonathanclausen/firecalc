using System.Security.Claims;
using System.Text.Encodings.Web;
using FireCalc.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FireCalc.Api.Mcp;

/// <summary>
/// Signs in MCP calls with an access token from <see cref="OAuthEndpoints"/>. The principal carries only
/// the FireCalc user id (<c>uid</c>), so a tool can only ever reach that user's data. A missing or expired
/// token answers 401 with the address of the resource metadata, which is how an MCP client finds where to log in.
/// </summary>
public sealed class McpTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, FireCalcDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpToken";
    public const string UserIdClaim = "uid";
    public const string GrantIdClaim = "grant";

    /// <summary>"Last used" on the connected-apps list is kept to within this, to spare a write per call.</summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(10);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (!token.StartsWith(OAuthTokens.AccessPrefix, StringComparison.Ordinal)) return AuthenticateResult.NoResult();

        var hash = OAuthTokens.Hash(token);
        var now = DateTimeOffset.UtcNow;
        var grant = await db.OAuthGrants.AsNoTracking().SingleOrDefaultAsync(g => g.AccessTokenHash == hash, Context.RequestAborted);
        if (grant is null || grant.AccessExpiresAt <= now) return AuthenticateResult.Fail("The access token is unknown or expired.");

        if (grant.LastUsedAt is null || grant.LastUsedAt < now - LastUsedPrecision)
            await db.OAuthGrants.Where(g => g.Id == grant.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastUsedAt, now), Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [new Claim(UserIdClaim, grant.UserId.ToString()), new Claim(GrantIdClaim, grant.Id.ToString())],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        var error = result.Failure is null ? "" : ", error=\"invalid_token\"";
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{PublicUrl.ResourceMetadata(Request)}\"{error}";
    }
}
