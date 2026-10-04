using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FireCalc.Api.Tests;

public class ScenarioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient NewOwner() => factory.CreateClientFor(subject: Guid.NewGuid().ToString());

    private static object Scenario(string name, params object[] events) => new
    {
        name,
        monthlySavings = 10000m,
        investmentReturnPct = 7m,
        savingsReturnPct = 1m,
        homeGrowthPct = 2m,
        inflationPct = 2m,
        fireAge = 50m,
        yearlySpending = 300000m,
        events,
    };

    [Fact]
    public async Task Scenarios_are_saved_with_their_events_by_age()
    {
        var client = NewOwner();
        var created = await client.PostAsJsonAsync("/api/scenarios", Scenario(
            "Sabbatår",
            new { kind = "lumpSum", age = 40m, amount = -200000m, years = 3m },
            new { kind = "break", age = 33m, years = 1m, amount = 5m }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/scenarios");
        var events = list[0].GetProperty("events");
        // Sorted by age, and each kind keeps only the fields it uses.
        Assert.Equal("break", events[0].GetProperty("kind").GetString());
        Assert.Equal(33m, events[0].GetProperty("age").GetDecimal());
        Assert.Equal(1m, events[0].GetProperty("years").GetDecimal());
        Assert.Equal(JsonValueKind.Null, events[0].GetProperty("amount").ValueKind);
        Assert.Equal(-200000m, events[1].GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, events[1].GetProperty("years").ValueKind);

        var put = await client.PutAsJsonAsync($"/api/scenarios/{id}", Scenario("Senere FIRE"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        list = await client.GetFromJsonAsync<JsonElement>("/api/scenarios");
        Assert.Equal("Senere FIRE", list[0].GetProperty("name").GetString());
        Assert.Equal(0, list[0].GetProperty("events").GetArrayLength());

        // Other users can't touch it.
        Assert.Equal(HttpStatusCode.NotFound, (await NewOwner().DeleteAsync($"/api/scenarios/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/scenarios/{id}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/scenarios")).GetArrayLength());
    }

    [Fact]
    public async Task A_break_needs_a_length_and_a_name_is_required()
    {
        var client = NewOwner();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/scenarios", Scenario(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/scenarios", Scenario("x", new { kind = "break", age = 33m }))).StatusCode);
    }

    [Fact]
    public async Task Birth_date_is_stored_on_the_profile()
    {
        var client = NewOwner();
        var res = await client.PutAsJsonAsync("/api/me/profile", new { birthDate = "1993-05-17" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("1993-05-17", me.GetProperty("birthDate").GetString());

        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/me/profile", new { birthDate = future })).StatusCode);
    }
}
