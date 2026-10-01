using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FireCalc.Api.Auth;

public class AuthOptions
{
    public const string Google = "https://accounts.google.com";

    /// <summary>OpenID Connect issuer whose ID tokens are accepted. Google unless overridden for local testing.</summary>
    public string Authority { get; set; } = Google;

    /// <summary>OAuth client id of the Google project; Google ID tokens must be issued for it.</summary>
    public string GoogleClientId { get; set; } = "";

    /// <summary>Google accounts allowed to use the app. Empty means nobody gets in.</summary>
    public string[] AllowedEmails { get; set; } = [];
}

public static class AuthSetup
{
    public const string OwnerPolicy = "Owner";

    public static IServiceCollection AddGoogleAuth(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // The frontend signs in with Google Identity Services and sends the ID token as a bearer token.
                o.Authority = options.Authority;
                o.RequireHttpsMetadata = options.Authority.StartsWith("https://", StringComparison.Ordinal);
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidIssuers = options.Authority == AuthOptions.Google
                    ? [AuthOptions.Google, "accounts.google.com"]
                    : [options.Authority];
                o.TokenValidationParameters.ValidAudience = options.GoogleClientId;
                o.TokenValidationParameters.NameClaimType = "email";
            });

        var allowed = options.AllowedEmails.Select(e => e.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsAllowed(ctx.User, allowed)));

        return services;
    }

    private static bool IsAllowed(ClaimsPrincipal user, HashSet<string> allowed)
    {
        var email = user.FindFirstValue("email");
        var verified = user.FindFirstValue("email_verified");
        return email is not null
            && string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase)
            && allowed.Contains(email);
    }
}
