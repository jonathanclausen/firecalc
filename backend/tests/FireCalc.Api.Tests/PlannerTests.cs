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
    public async Task Snapshots_record_balances_per_date_and_can_be_edited()
    {
        var client = NewOwner();
        var depot = await CreateAccount(client, "Depot", "investment");
        var savings = await CreateAccount(client, "Opsparing", "savings");

        var create = await client.PostAsJsonAsync("/api/snapshots", new
        {
            date = "2026-01-01",
            entries = new[] { new { accountId = depot, balance = 100000.50m }, new { accountId = savings, balance = 25000m } },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var snapshot = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(125000.50m, snapshot.GetProperty("total").GetDecimal());

        var id = snapshot.GetProperty("id").GetString();
        var edit = await client.PutAsJsonAsync($"/api/snapshots/{id}", new
        {
            date = "2026-01-02",
            note = "after bonus",
            entries = new[] { new { accountId = depot, balance = 110000m } },
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-01-02", edited.GetProperty("date").GetString());
        Assert.Equal(110000m, edited.GetProperty("total").GetDecimal());
        Assert.Equal(1, edited.GetProperty("entries").GetArrayLength());

        // The account now has history, so it can only be archived, not deleted.
        var delete = await client.DeleteAsync($"/api/accounts/{depot}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Only_one_snapshot_per_date()
    {
        var client = NewOwner();
        var depot = await CreateAccount(client, "Depot", "investment");
        var body = new { date = "2026-03-01", entries = new[] { new { accountId = depot, balance = 1m } } };

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/snapshots", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/snapshots", body)).StatusCode);
    }

    [Fact]
    public async Task Snapshot_rejects_accounts_of_another_user_and_negative_balances()
    {
        var other = await CreateAccount(NewOwner(), "Not mine", "cash");
        var client = NewOwner();
        var mine = await CreateAccount(client, "Mine", "cash");

        var foreign = await client.PostAsJsonAsync("/api/snapshots", new { date = "2026-01-01", entries = new[] { new { accountId = other, balance = 1m } } });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var negative = await client.PostAsJsonAsync("/api/snapshots", new { date = "2026-01-01", entries = new[] { new { accountId = mine, balance = -1m } } });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task Dashboard_shows_history_and_goal_progress()
    {
        var client = NewOwner();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/goal")).StatusCode);

        var depot = await CreateAccount(client, "Depot", "investment");
        var savings = await CreateAccount(client, "Opsparing", "savings");
        await client.PostAsJsonAsync("/api/snapshots", new { date = "2026-01-01", entries = new[] { new { accountId = depot, balance = 400000m }, new { accountId = savings, balance = 100000m } } });
        await client.PostAsJsonAsync("/api/snapshots", new { date = "2026-06-01", entries = new[] { new { accountId = depot, balance = 600000m }, new { accountId = savings, balance = 150000m } } });

        var goal = await client.PutAsJsonAsync("/api/goal", new { targetAmount = 5000000m, targetDate = "2040-01-01" });
        Assert.Equal(HttpStatusCode.OK, goal.StatusCode);

        var dash = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Equal(2, dash.GetProperty("series").GetArrayLength());
        Assert.Equal(750000m, dash.GetProperty("latest").GetProperty("total").GetDecimal());
        Assert.Equal(600000m, dash.GetProperty("latest").GetProperty("byType").GetProperty("investment").GetDecimal());
        Assert.Equal(250000m, dash.GetProperty("changeSincePrevious").GetDecimal());
        Assert.Equal(15.0m, dash.GetProperty("goal").GetProperty("progressPct").GetDecimal());
        Assert.Equal(4250000m, dash.GetProperty("goal").GetProperty("remaining").GetDecimal());
        Assert.Equal("FIRE", dash.GetProperty("goal").GetProperty("goal").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Goal_requires_a_positive_target()
    {
        var res = await NewOwner().PutAsJsonAsync("/api/goal", new { targetAmount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
