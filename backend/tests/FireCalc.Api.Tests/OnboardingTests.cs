using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FireCalc.Api.Tests;

public class OnboardingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient NewOwner() => factory.CreateClientFor(subject: Guid.NewGuid().ToString());

    [Fact]
    public async Task A_new_user_starts_the_guide_and_can_finish_it_and_hide_the_checklist()
    {
        var client = NewOwner();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.False(me.GetProperty("onboarded").GetBoolean());
        Assert.False(me.GetProperty("checklistHidden").GetBoolean());
        Assert.False(me.GetProperty("hasDemo").GetBoolean());

        var res = await client.PutAsJsonAsync("/api/me/onboarding", new { onboarded = true });
        me = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(me.GetProperty("onboarded").GetBoolean());
        Assert.False(me.GetProperty("checklistHidden").GetBoolean());

        // Fields left out stay as they are.
        res = await client.PutAsJsonAsync("/api/me/onboarding", new { checklistHidden = true });
        me = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(me.GetProperty("onboarded").GetBoolean());
        Assert.True(me.GetProperty("checklistHidden").GetBoolean());
    }

    [Fact]
    public async Task Example_data_is_loaded_once_and_removed_without_touching_own_data()
    {
        var client = NewOwner();
        await client.PostAsJsonAsync("/api/accounts", new { name = "Min opsparing", type = "savings" });

        var res = await client.PostAsync("/api/demo", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasDemo").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/demo", null)).StatusCode);

        var accounts = await client.GetFromJsonAsync<JsonElement>("/api/accounts");
        Assert.Equal(6, accounts.GetArrayLength());
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.True(dashboard.GetProperty("series").GetArrayLength() >= 24);
        Assert.Equal(7_500_000m, dashboard.GetProperty("goal").GetProperty("goal").GetProperty("targetAmount").GetDecimal());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/scenarios")).GetArrayLength());

        res = await client.DeleteAsync("/api/demo");
        Assert.False((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasDemo").GetBoolean());
        accounts = await client.GetFromJsonAsync<JsonElement>("/api/accounts");
        Assert.Equal("Min opsparing", Assert.Single(accounts.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/scenarios")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/goal")).StatusCode);
    }

    [Fact]
    public async Task A_goal_saved_by_the_user_survives_removing_the_example_data()
    {
        var client = NewOwner();
        await client.PostAsync("/api/demo", null);
        await client.PutAsJsonAsync("/api/goal", new { targetAmount = 5_000_000m });
        await client.DeleteAsync("/api/demo");
        var goal = await client.GetFromJsonAsync<JsonElement>("/api/goal");
        Assert.Equal(5_000_000m, goal.GetProperty("targetAmount").GetDecimal());
    }
}
