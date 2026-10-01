using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FireCalc.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FireCalc.Api.Tests;

/// <summary>
/// Runs the API against a throwaway Postgres database and swaps Google's signing keys for a local test key,
/// so tests can mint ID tokens that go through the real issuer, audience and allow-list checks.
/// Set FIRECALC_TEST_POSTGRES to point at a server (defaults to postgres/postgres on localhost).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ClientId = "test-client.apps.googleusercontent.com";
    public const string OwnerEmail = "owner@example.com";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly string _connectionString;

    public ApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("FIRECALC_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        _connectionString = $"{server};Database=firecalc_test_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Auth:GoogleClientId", ClientId);
        builder.UseSetting("Auth:AllowedEmails:0", OwnerEmail);

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = null;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }

    public HttpClient CreateClientFor(string email = OwnerEmail, string subject = "google-sub-1", bool emailVerified = true, string audience = ClientId)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("name", "Test Owner")]),
            Claims = new Dictionary<string, object> { ["email"] = email, ["email_verified"] = emailVerified },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<FireCalcDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
    }
}
