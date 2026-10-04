using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
    public async Task Api_rejects_accounts_not_on_the_allow_list()
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
    public async Task Api_rejects_tokens_from_another_issuer()
    {
        var res = await factory.CreateClientFor(issuer: "https://accounts.google.com").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task New_login_with_the_same_verified_email_takes_over_the_existing_user()
    {
        var before = factory.CreateClientFor(email: ApiFactory.OwnerEmail, subject: "old-google-sub");
        var created = await before.PostAsJsonAsync("/api/accounts", new { name = "Opsparing", type = "savings" });
        created.EnsureSuccessStatusCode();

        // Same person, new Firebase id, email in different case.
        var after = factory.CreateClientFor(email: ApiFactory.OwnerEmail.ToUpperInvariant(), subject: "firebase-uid-1");
        var accounts = await after.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/accounts");
        Assert.Contains(accounts!, a => a["name"].ToString() == "Opsparing");
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_user_and_their_data()
    {
        var client = factory.CreateClientForNewUser();
        // The example data fills accounts, balances, trades, a home and scenarios.
        (await client.PostAsync("/api/demo", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me")).StatusCode);

        // Signing in again starts empty.
        var accounts = await client.GetFromJsonAsync<List<object>>("/api/accounts");
        Assert.Empty(accounts!);
    }

    [Fact]
    public async Task Owner_is_created_on_first_request()
    {
        var me = await factory.CreateClientFor().GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(ApiFactory.OwnerEmail, me.GetProperty("email").GetString());
        Assert.Equal("DKK", me.GetProperty("currency").GetString());
    }
}

public class OpenSignUpTests(OpenSignUpTests.Factory factory) : IClassFixture<OpenSignUpTests.Factory>
{
    public class Factory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:OpenSignUp", "true");
        }
    }

    [Fact]
    public async Task Anyone_with_a_verified_email_gets_in()
    {
        var res = await factory.CreateClientFor(email: "stranger@example.com", subject: "stranger").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Unverified_emails_still_wait_for_verification()
    {
        var res = await factory.CreateClientFor(email: "new@example.com", subject: "new", emailVerified: false).GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
