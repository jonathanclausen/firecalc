using System.Net;
using System.Text.Json;
using FireCalc.Api.Monitoring;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FireCalc.Api.Tests;

public class TimedApiFactory : ApiFactory
{
    public StringWriter Lines { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Monitoring:RequestLog", "true");
        builder.ConfigureTestServices(s => s.AddSingleton(new RequestTimingLog(Lines)));
    }
}

public class RequestTimingTests(TimedApiFactory factory) : IClassFixture<TimedApiFactory>
{
    [Fact]
    public async Task Each_api_request_is_logged_with_its_route_template_and_latency()
    {
        var client = factory.CreateClientForNewUser();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{Guid.NewGuid()}/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/dashboard")).StatusCode);

        var lines = factory.Lines.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .ToList();
        Assert.All(lines, l => Assert.Equal("request-timing", l.GetProperty("kind").GetString()));
        Assert.All(lines, l => Assert.True(l.GetProperty("latencyMs").GetDouble() >= 0));

        var portfolio = Assert.Single(lines, l => l.GetProperty("route").GetString() == "/api/portfolio");
        Assert.Equal("GET", portfolio.GetProperty("method").GetString());
        Assert.Equal(200, portfolio.GetProperty("status").GetInt32());
        Assert.Equal("INFO", portfolio.GetProperty("severity").GetString());

        // Requests for different accounts share one route, so they count together.
        var transactions = Assert.Single(lines, l => l.GetProperty("route").GetString()!.StartsWith("/api/accounts/{accountId:guid}/transactions"));
        Assert.Equal(404, transactions.GetProperty("status").GetInt32());

        Assert.Equal(401, Assert.Single(lines, l => l.GetProperty("route").GetString() == "/api/dashboard").GetProperty("status").GetInt32());
    }
}
