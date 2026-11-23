using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FireCalc.Api.Tests;

/// <summary>The OAuth login for MCP clients and the read-only tools behind /mcp.</summary>
public class McpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Redirect = "https://claude.ai/api/mcp/auth_callback";
    private const string Verifier = "a-long-enough-code-verifier-for-pkce-0123456789abcdef";

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> RegisterAsync(string redirect = Redirect)
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/oauth/register", new { client_name = "Claude", redirect_uris = new[] { redirect } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("client_id").GetString()!;
    }

    /// <summary>Approves the client as <paramref name="user"/> and returns the code from the redirect.</summary>
    private static async Task<string> ApproveAsync(HttpClient user, string clientId, string verifier = Verifier)
    {
        var res = await user.PostAsJsonAsync("/api/oauth/authorize", new
        {
            clientId, redirectUri = Redirect, responseType = "code", codeChallenge = Challenge(verifier),
            codeChallengeMethod = "S256", state = "xyz", approve = true,
        });
        res.EnsureSuccessStatusCode();
        var url = new Uri((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!);
        Assert.StartsWith(Redirect, url.ToString());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("xyz", query["state"]);
        return query["code"]!;
    }

    private async Task<HttpResponseMessage> TokenAsync(params (string, string)[] form) =>
        await factory.CreateClient().PostAsync("/oauth/token", new FormUrlEncodedContent(form.Select(f => KeyValuePair.Create(f.Item1, f.Item2))));

    private async Task<(string Access, string Refresh, string ClientId)> ConnectAsync(HttpClient user)
    {
        var clientId = await RegisterAsync();
        var code = await ApproveAsync(user, clientId);
        var res = await TokenAsync(("grant_type", "authorization_code"), ("code", code), ("client_id", clientId),
            ("redirect_uri", Redirect), ("code_verifier", Verifier));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!, clientId);
    }

    private async Task<HttpResponseMessage> McpAsync(string? token, string method, object? @params = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = @params ?? new { } }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    /// <summary>The JSON-RPC result, from a plain JSON body or the data line of an event stream.</summary>
    private static async Task<JsonElement> ResultAsync(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{')
            ? text
            : text.Split('\n').Last(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
        return JsonDocument.Parse(json).RootElement.GetProperty("result");
    }

    private async Task<JsonElement> CallToolAsync(string token, string name, object? arguments = null)
    {
        var result = await ResultAsync(await McpAsync(token, "tools/call", new { name, arguments = arguments ?? new { } }));
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.ToString());
        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
    }

    [Fact]
    public async Task Without_a_token_mcp_answers_401_pointing_at_the_login()
    {
        var res = await McpAsync(null, "tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", res.Headers.WwwAuthenticate.ToString());

        var meta = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal("http://localhost/mcp", meta.GetProperty("resource").GetString());
        var server = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");
        Assert.Equal("http://localhost/planner/connect", server.GetProperty("authorization_endpoint").GetString());

        var firebase = await McpAsync("not-ours", "tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, firebase.StatusCode);
    }

    [Fact]
    public async Task The_public_address_comes_from_the_web_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-authorization-server");
        request.Headers.Add("X-Forwarded-Host", "okonomi.concensur.dk");
        request.Headers.Add("X-Forwarded-Proto", "https");
        var meta = await (await factory.CreateClient().SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://okonomi.concensur.dk", meta.GetProperty("issuer").GetString());
        Assert.Equal("https://okonomi.concensur.dk/oauth/token", meta.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task Registration_only_takes_https_or_loopback_return_addresses()
    {
        foreach (var bad in new[] { "http://evil.example/cb", "javascript:alert(1)", "https://x.example/cb#frag" })
        {
            var res = await factory.CreateClient().PostAsJsonAsync("/oauth/register", new { redirect_uris = new[] { bad } });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        await RegisterAsync("http://127.0.0.1:33418/callback");
    }

    [Fact]
    public async Task Approval_page_refuses_unknown_return_addresses()
    {
        var user = factory.CreateClientForNewUser();
        var clientId = await RegisterAsync();
        var info = await user.GetFromJsonAsync<JsonElement>($"/api/oauth/client?clientId={clientId}&redirectUri={Uri.EscapeDataString(Redirect)}");
        Assert.Equal("Claude", info.GetProperty("name").GetString());
        Assert.Equal("claude.ai", info.GetProperty("redirectHost").GetString());

        var other = await user.GetAsync($"/api/oauth/client?clientId={clientId}&redirectUri={Uri.EscapeDataString("https://evil.example/cb")}");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        var post = await user.PostAsJsonAsync("/api/oauth/authorize", new
        {
            clientId, redirectUri = "https://evil.example/cb", responseType = "code", codeChallenge = Challenge(Verifier),
            codeChallengeMethod = "S256", approve = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);

        // Declining goes back to the app with an error and no code.
        var declined = await user.PostAsJsonAsync("/api/oauth/authorize", new { clientId, redirectUri = Redirect, state = "s", approve = false });
        var url = (await declined.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!;
        Assert.Equal(Redirect + "?error=access_denied&state=s", url);

        // The approval page itself needs a signed-in user.
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/oauth/authorize", new { clientId, redirectUri = Redirect, approve = true });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_code_needs_the_right_verifier_and_works_once()
    {
        var user = factory.CreateClientForNewUser();
        var clientId = await RegisterAsync();

        var code = await ApproveAsync(user, clientId);
        var wrong = await TokenAsync(("grant_type", "authorization_code"), ("code", code), ("client_id", clientId),
            ("redirect_uri", Redirect), ("code_verifier", Verifier + "x"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        // The failed try used the code up.
        var again = await TokenAsync(("grant_type", "authorization_code"), ("code", code), ("client_id", clientId),
            ("redirect_uri", Redirect), ("code_verifier", Verifier));
        Assert.Equal("invalid_grant", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var otherClient = await RegisterAsync();
        var code2 = await ApproveAsync(user, clientId);
        var stolen = await TokenAsync(("grant_type", "authorization_code"), ("code", code2), ("client_id", otherClient),
            ("redirect_uri", Redirect), ("code_verifier", Verifier));
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
    }

    [Fact]
    public async Task Tools_read_only_the_connected_users_data()
    {
        var alice = factory.CreateClientForNewUser();
        var bob = factory.CreateClientForNewUser();
        foreach (var (client, name, balance) in new[] { (alice, "Alices opsparing", 100000m), (bob, "Bobs opsparing", 5m) })
        {
            var account = await (await client.PostAsJsonAsync("/api/accounts", new { name, type = "savings" })).Content.ReadFromJsonAsync<JsonElement>();
            (await client.PutAsJsonAsync($"/api/accounts/{account.GetProperty("id").GetString()}/balances",
                new { date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40), balance })).EnsureSuccessStatusCode();
        }
        await alice.PutAsJsonAsync("/api/goal", new { name = "FIRE", targetAmount = 1000000m });

        var (token, _, _) = await ConnectAsync(alice);

        var tools = await ResultAsync(await McpAsync(token, "tools/list"));
        var names = tools.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("get_overview", names);
        Assert.Contains("get_fire_plan", names);
        Assert.All(tools.GetProperty("tools").EnumerateArray(), t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));

        var overview = await CallToolAsync(token, "get_overview");
        Assert.Equal(100000m, overview.GetProperty("netWorth").GetDecimal());
        Assert.Equal("Alices opsparing", Assert.Single(overview.GetProperty("accounts").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(10m, overview.GetProperty("goal").GetProperty("progressPct").GetDecimal());

        // Bob's account id gets nothing through Alice's token.
        var bobAccounts = await bob.GetFromJsonAsync<JsonElement>("/api/accounts");
        var bobId = bobAccounts[0].GetProperty("id").GetString();
        var result = await ResultAsync(await McpAsync(token, "tools/call", new { name = "get_account_balances", arguments = new { accountId = bobId } }));
        Assert.True(result.GetProperty("isError").GetBoolean());

        var plan = await CallToolAsync(token, "get_fire_plan");
        Assert.Equal(100000m, plan.GetProperty("netWorthToday").GetDecimal());
    }

    [Fact]
    public async Task Fire_plan_projects_each_scenario_like_the_app()
    {
        var user = factory.CreateClientForNewUser();
        var birth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-30);
        (await user.PutAsJsonAsync("/api/me/profile", new { birthDate = birth })).EnsureSuccessStatusCode();
        var account = await (await user.PostAsJsonAsync("/api/accounts", new { name = "Depot", type = "savings" })).Content.ReadFromJsonAsync<JsonElement>();
        await user.PutAsJsonAsync($"/api/accounts/{account.GetProperty("id").GetString()}/balances",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow), balance = 100000m });
        await user.PostAsJsonAsync("/api/scenarios", new
        {
            name = "Basis", monthlySavings = 10000m, investmentReturnPct = 0m, savingsReturnPct = 0m, homeGrowthPct = 0m,
            inflationPct = 0m, fireAge = 40m, yearlySpending = 0m, withdrawalPct = 4m, events = Array.Empty<object>(),
        });

        var (token, _, _) = await ConnectAsync(user);
        var plan = await CallToolAsync(token, "get_fire_plan");
        Assert.Equal(30.0, plan.GetProperty("age").GetDouble());
        var projection = plan.GetProperty("scenarios")[0].GetProperty("projection").GetProperty("nominal");
        // No growth: 100,000 plus 10,000 a month for 10 years until 40, then 4 % of that taken out the first year.
        Assert.Equal(1300000, projection.GetProperty("atFireAge").GetDouble());
        Assert.Equal(52000, projection.GetProperty("firstYearWithdrawal").GetDouble());
        Assert.Equal(700000, projection.GetProperty("in5Years").GetDouble());
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_disconnecting_stops_access()
    {
        var user = factory.CreateClientForNewUser();
        var (access, refresh, clientId) = await ConnectAsync(user);

        var res = await TokenAsync(("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", clientId));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var newAccess = body.GetProperty("access_token").GetString()!;

        // The old pair is retired.
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(access, "tools/list")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await TokenAsync(("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", clientId))).StatusCode);
        await ResultAsync(await McpAsync(newAccess, "tools/list"));

        var connections = await user.GetFromJsonAsync<JsonElement>("/api/oauth/connections");
        var connection = Assert.Single(connections.EnumerateArray());
        Assert.Equal("Claude", connection.GetProperty("clientName").GetString());

        // Someone else can't disconnect it.
        var other = factory.CreateClientForNewUser();
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/oauth/connections/{connection.GetProperty("id").GetString()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/oauth/connections/{connection.GetProperty("id").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(newAccess, "tools/list")).StatusCode);
    }

    [Fact]
    public async Task Mcp_tokens_do_not_open_the_apps_own_api()
    {
        var user = factory.CreateClientForNewUser();
        var (access, _, _) = await ConnectAsync(user);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
    }
}
