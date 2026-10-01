using System.Net;
using System.Net.Http.Json;

namespace FireCalc.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_check_needs_no_login()
    {
        var res = await factory.CreateClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_requests_without_a_token()
    {
        var res = await factory.CreateClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_google_accounts_not_on_the_allow_list()
    {
        var res = await factory.CreateClientFor(email: "stranger@example.com", subject: "other").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_unverified_email()
    {
        var res = await factory.CreateClientFor(emailVerified: false).GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_tokens_issued_for_another_client()
    {
        var res = await factory.CreateClientFor(audience: "someone-elses-app").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Owner_is_created_on_first_request()
    {
        var me = await factory.CreateClientFor().GetFromJsonAsync<Dictionary<string, string>>("/api/me");
        Assert.Equal(ApiFactory.OwnerEmail, me!["email"]);
        Assert.Equal("DKK", me["currency"]);
    }
}
