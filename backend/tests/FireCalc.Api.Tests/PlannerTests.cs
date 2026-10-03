using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FireCalc.Api.Tests;

public class PlannerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Each test signs in as its own Google account so tests don't see each other's data.
    private HttpClient NewOwner() => factory.CreateClientFor(subject: Guid.NewGuid().ToString());

    private static async Task<string> CreateAccount(HttpClient client, string name, string type)
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new { name, type });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Accounts_can_be_created_renamed_and_archived()
    {
        var client = NewOwner();
        var id = await CreateAccount(client, "Nordnet depot", "investment");

        var put = await client.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Nordnet", type = "investment", archived = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var active = await client.GetFromJsonAsync<JsonElement>("/api/accounts");
        Assert.Equal(0, active.GetArrayLength());

        var all = await client.GetFromJsonAsync<JsonElement>("/api/accounts?includeArchived=true");
        Assert.Equal("Nordnet", all[0].GetProperty("name").GetString());
        Assert.True(all[0].GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Account_requires_a_name()
    {
        var res = await NewOwner().PostAsJsonAsync("/api/accounts", new { name = " ", type = "savings" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_accounts()
    {
        var alice = NewOwner();
        var id = await CreateAccount(alice, "Opsparing", "savings");

        var bob = NewOwner();
        Assert.Equal(0, (await bob.GetFromJsonAsync<JsonElement>("/api/accounts")).GetArrayLength());
        var res = await bob.PutAsJsonAsync($"/api/accounts/{id}", new { name = "x", type = "savings", archived = false });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Balances_are_kept_per_account_and_date()
    {
        var client = NewOwner();
        var savings = await CreateAccount(client, "Opsparing", "savings");

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/accounts/{savings}/balances", new { date = "2026-01-01", balance = 25000m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/accounts/{savings}/balances", new { date = "2026-02-01", balance = 26000m })).StatusCode);
        // The same date again replaces that day's balance.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/accounts/{savings}/balances", new { date = "2026-02-01", balance = 26500.5m })).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{savings}/balances");
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("2026-02-01", list[0].GetProperty("date").GetString());
        Assert.Equal(26500.5m, list[0].GetProperty("balance").GetDecimal());

        var account = (await client.GetFromJsonAsync<JsonElement>("/api/accounts"))[0];
        Assert.False(account.GetProperty("tracked").GetBoolean());
        Assert.Equal(26500.5m, account.GetProperty("balance").GetDecimal());
        Assert.Equal("2026-02-01", account.GetProperty("balanceDate").GetString());

        // The account now has history, so it can only be archived, not deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/accounts/{savings}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/accounts/{savings}/balances/2026-02-01")).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{savings}/balances")).GetArrayLength());
    }

    [Fact]
    public async Task Balances_reject_other_users_negative_amounts_future_dates_and_tracked_accounts()
    {
        var other = await CreateAccount(NewOwner(), "Not mine", "cash");
        var client = NewOwner();
        var mine = await CreateAccount(client, "Mine", "cash");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/accounts/{other}/balances", new { date = "2026-01-01", balance = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{mine}/balances", new { date = "2026-01-01", balance = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{mine}/balances", new { date = tomorrow, balance = 1m })).StatusCode);

        var depot = await CreateAccount(client, "Depot", "investment");
        await client.PostAsJsonAsync($"/api/accounts/{depot}/transactions", new { date = "2026-01-02", type = "deposit", amount = 1000 });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/accounts/{depot}/balances", new { date = "2026-01-03", balance = 1m })).StatusCode);
    }

    [Fact]
    public async Task Dashboard_adds_the_live_portfolio_to_the_latest_balances()
    {
        var client = NewOwner();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/goal")).StatusCode);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var d0 = today.AddMonths(-3);
        factory.MarketData.Prices["DASH.CO"] = new("DKK", [new(d0, 100m), new(today.AddDays(-1), 150m)]);

        var depot = await CreateAccount(client, "Depot", "investment");
        var savings = await CreateAccount(client, "Opsparing", "savings");
        // 1,000 shares at 100, now worth 150 each.
        await client.PutAsJsonAsync($"/api/accounts/{depot}/holdings", new { instrument = new { symbol = "DASH.CO" }, quantity = 1000, unitPrice = 100, date = d0 });
        await client.PutAsJsonAsync($"/api/accounts/{savings}/balances", new { date = d0, balance = 100000m });
        await client.PutAsJsonAsync($"/api/accounts/{savings}/balances", new { date = today.AddDays(-10), balance = 150000m });

        var goal = await client.PutAsJsonAsync("/api/goal", new { targetAmount = 3000000m, targetDate = "2040-01-01" });
        Assert.Equal(HttpStatusCode.OK, goal.StatusCode);

        var dash = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        var latest = dash.GetProperty("latest");
        Assert.Equal(today.ToString("yyyy-MM-dd"), latest.GetProperty("date").GetString());
        Assert.Equal(300000m, latest.GetProperty("total").GetDecimal());
        Assert.Equal(150000m, latest.GetProperty("byType").GetProperty("investment").GetDecimal());

        // The first point is the day it all started, then month ends, the balance entered, and today.
        var series = dash.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(d0.ToString("yyyy-MM-dd"), series[0].GetProperty("date").GetString());
        Assert.Equal(200000m, series[0].GetProperty("total").GetDecimal());
        Assert.True(series.Count >= 4);

        var accounts = dash.GetProperty("accounts").EnumerateArray().ToList();
        Assert.True(accounts[0].GetProperty("tracked").GetBoolean());
        Assert.Equal(150000m, accounts[0].GetProperty("value").GetDecimal());
        Assert.Equal(today.AddDays(-10).ToString("yyyy-MM-dd"), accounts[1].GetProperty("balanceDate").GetString());

        Assert.Equal(10.0m, dash.GetProperty("goal").GetProperty("progressPct").GetDecimal());
        Assert.Equal(2700000m, dash.GetProperty("goal").GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task Goal_requires_a_positive_target()
    {
        var res = await NewOwner().PutAsJsonAsync("/api/goal", new { targetAmount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
