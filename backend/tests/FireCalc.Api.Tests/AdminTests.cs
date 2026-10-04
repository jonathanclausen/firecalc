using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FireCalc.Api.Tests;

public class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Admin() => factory.CreateClientFor(subject: "admin-sub");

    [Fact]
    public async Task Only_admins_see_the_admin_pages()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Admin().GetAsync("/api/admin/access")).StatusCode);

        // Signed in and allowed to use the app, but not an admin: the pages look like they don't exist.
        var member = factory.CreateClientFor(email: ApiFactory.MemberEmail, subject: "member-only");
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/admin/access")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/admin/overview")).StatusCode);

        // An admin email that isn't verified doesn't count.
        var unverified = factory.CreateClientFor(subject: "admin-unverified", emailVerified: false);
        Assert.Equal(HttpStatusCode.NotFound, (await unverified.GetAsync("/api/admin/overview")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task The_overview_shows_progress_and_counts_but_no_amounts()
    {
        var saver = factory.CreateClientFor(email: ApiFactory.MemberEmail, subject: "saver");
        var created = await saver.PostAsJsonAsync("/api/accounts", new { name = "Opsparing", type = "savings" });
        var accountId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await saver.PutAsJsonAsync($"/api/accounts/{accountId}/balances", new { date = "2026-09-30", balance = 123_456.78m });
        await saver.PutAsJsonAsync("/api/goal", new { targetAmount = 7_654_321m });

        var tourist = factory.CreateClientFor(email: ApiFactory.MemberEmail, subject: "tourist");
        await tourist.PostAsync("/api/demo", null);

        var res = await Admin().GetAsync("/api/admin/overview");
        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("123456", raw);
        Assert.DoesNotContain("7654321", raw);

        var overview = JsonDocument.Parse(raw).RootElement;
        var users = overview.GetProperty("users").EnumerateArray().ToList();
        var saverRow = users.Single(u => u.GetProperty("accounts").GetInt32() == 1);
        var steps = saverRow.GetProperty("steps");
        Assert.True(steps.GetProperty("account").GetBoolean());
        Assert.True(steps.GetProperty("import").GetBoolean()); // no share account, so nothing to import
        Assert.True(steps.GetProperty("goal").GetBoolean());
        Assert.False(steps.GetProperty("profile").GetBoolean());
        Assert.Equal(3, saverRow.GetProperty("stepsDone").GetInt32());
        Assert.Equal(1, saverRow.GetProperty("balances").GetInt32());

        // Example data is not the user's own, so the tourist hasn't started.
        var touristRow = Assert.Single(users, u => u.GetProperty("hasDemo").GetBoolean());
        Assert.Equal(0, touristRow.GetProperty("stepsDone").GetInt32());
        Assert.Equal(0, touristRow.GetProperty("accounts").GetInt32());

        var totals = overview.GetProperty("totals");
        Assert.Equal(users.Count, totals.GetProperty("users").GetInt32());
        Assert.Equal(1, totals.GetProperty("activated").GetInt32());
        Assert.Equal(12, overview.GetProperty("signUpsByWeek").GetArrayLength());
        Assert.Equal(users.Count, overview.GetProperty("signUpsByWeek").EnumerateArray().Sum(w => w.GetProperty("count").GetInt32()));
        Assert.Equal("signedUp", overview.GetProperty("funnel")[0].GetProperty("key").GetString());

        // Using the app notes when the user was last seen.
        Assert.NotEqual(JsonValueKind.Null, saverRow.GetProperty("lastSeenAt").ValueKind);
        Assert.True(totals.GetProperty("activeLast7Days").GetInt32() >= 2);
    }
}
