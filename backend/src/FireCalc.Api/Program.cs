using System.Text.Json;
using System.Text.Json.Serialization;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using FireCalc.Api.Endpoints;
using FireCalc.Api.Portfolio;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Retries cover Neon's free tier: it suspends an idle database and drops open connections,
// so the first query after a pause can fail once while the database wakes up.
builder.Services.AddDbContext<FireCalcDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default"), npgsql => npgsql.EnableRetryOnFailure()));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddHttpClient<IMarketData, YahooMarketData>(c =>
{
    // Yahoo refuses requests without a browser-like user agent.
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FireCalc/1.0)");
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<PriceRefreshState>();
builder.Services.AddScoped<PriceService>();
builder.Services.AddScoped<PortfolioValuation>();
builder.Services.AddScoped<PortfolioHistory>();

builder.Services.AddProblemDetails();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddSingleton<AdminAccess>();
builder.Services.AddSingleton<LastSeenThrottle>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FireCalcDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok"));

// The sign-in page calls this while the person picks a login, so a cold start of this service and
// a suspended Neon database are paid before the first real request rather than after it. It also
// loads Firebase's signing keys, which the first token check would otherwise fetch.
app.MapGet("/api/warmup", async (FireCalcDbContext db, IOptionsMonitor<JwtBearerOptions> jwt, CancellationToken ct) =>
{
    var keys = jwt.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager?.GetConfigurationAsync(ct)
        ?? Task.CompletedTask;
    await db.Database.CanConnectAsync(ct);
    try { await keys; } catch { /* the first real token check tries again */ }
    return Results.NoContent();
});

var api = app.MapGroup("/api").RequireAuthorization(AuthSetup.OwnerPolicy).AddEndpointFilter<LastSeenFilter>();
api.MapMeEndpoints();
api.MapAccountEndpoints();
api.MapHomeEndpoints();
api.MapGoalEndpoints();
api.MapDashboardEndpoints();
api.MapPortfolioEndpoints();
api.MapScenarioEndpoints();
api.MapDemoEndpoints();
app.MapAdminEndpoints();

app.Run();

public partial class Program;
