using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FireCalc.Api.Auth;

public class AuthOptions
{
    /// <summary>
    /// Firebase (Identity Platform) project the users sign in to. Its ID tokens are issued by
    /// https://securetoken.google.com/&lt;project id&gt; for the project id as audience.
    /// </summary>
    public string FirebaseProjectId { get; set; } = "";

    /// <summary>Token issuer. Firebase's for the project unless overridden for local testing.</summary>
    public string? Authority { get; set; }

    /// <summary>When true anyone with a verified email can sign up; otherwise only <see cref="AllowedEmails"/>.</summary>
    public bool OpenSignUp { get; set; }

    /// <summary>Accounts allowed in while sign-up is closed, by verified email. Empty means nobody gets in.</summary>
    public string[] AllowedEmails { get; set; } = [];

    public string Issuer => Authority ?? $"https://securetoken.google.com/{FirebaseProjectId}";
}

public static class AuthSetup
{
    public const string OwnerPolicy = "Owner";

    public static IServiceCollection AddFirebaseAuth(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // The frontend signs in with Firebase (Google, Facebook or email and password) and sends
                // the Firebase ID token as a bearer token. Firebase links logins that share an email to
                // one Firebase user, so the token's sub is the same whichever way the person signed in.
                o.Authority = options.Issuer;
                o.RequireHttpsMetadata = options.Issuer.StartsWith("https://", StringComparison.Ordinal);
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidIssuer = options.Issuer;
                o.TokenValidationParameters.ValidAudience = options.FirebaseProjectId;
                o.TokenValidationParameters.NameClaimType = "email";
            });

        var allowed = options.AllowedEmails.Select(e => e.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => HasVerifiedEmail(ctx.User) && (options.OpenSignUp || allowed.Contains(ctx.User.FindFirstValue("email")!))));

        return services;
    }

    /// <summary>
    /// The email must be verified: Google does that itself, a password or Facebook login has to
    /// click Firebase's verification link first. Otherwise anyone could claim someone else's address.
    /// </summary>
    public static bool HasVerifiedEmail(ClaimsPrincipal user) =>
        user.FindFirstValue("email") is not null
        && string.Equals(user.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
}
