using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FireCalc.Api.Data;
using FireCalc.Api.Homes;

namespace FireCalc.Api.Tests;

public class HomeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient NewOwner() => factory.CreateClientForNewUser();

    [Fact]
    public void An_annuity_loan_pays_the_same_each_month_and_is_paid_off_at_the_end()
    {
        // 1.200.000 at 4 % over 30 years: a ydelse of about 5.729 a month before bidrag.
        var terms = new MortgageMath.Terms(4m, 0.5m, new DateOnly(2056, 1, 1), null);
        var first = MortgageMath.Payment(1200000m, terms, new DateOnly(2026, 1, 1));
        Assert.Equal(4000m, first.Interest);
        Assert.Equal(500m, first.Contribution);
        Assert.InRange(first.Interest + first.Repayment, 5728m, 5730m);

        var statements = new List<MortgageBalance> { new() { Date = new DateOnly(2026, 1, 1), Balance = 1200000m } };
        Assert.Equal(1200000m, MortgageMath.OwedOn(statements, terms, new DateOnly(2026, 1, 31)));
        Assert.Equal(Math.Round(1200000m - first.Repayment, 2), MortgageMath.OwedOn(statements, terms, new DateOnly(2026, 2, 1)));
        Assert.Equal(0m, MortgageMath.OwedOn(statements, terms, new DateOnly(2056, 1, 1)));
        Assert.Null(MortgageMath.OwedOn(statements, terms, new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Afdragsfrihed_pays_only_interest_and_a_loan_without_terms_stays_put()
    {
        var statements = new List<MortgageBalance> { new() { Date = new DateOnly(2026, 1, 1), Balance = 1000000m } };
        var interestOnly = new MortgageMath.Terms(4m, null, new DateOnly(2056, 1, 1), new DateOnly(2027, 1, 1));
        Assert.Equal(1000000m, MortgageMath.OwedOn(statements, interestOnly, new DateOnly(2027, 1, 1)));
        Assert.True(MortgageMath.OwedOn(statements, interestOnly, new DateOnly(2027, 2, 1)) < 1000000m);

        var noTerms = new MortgageMath.Terms(null, null, null, null);
        Assert.Equal(1000000m, MortgageMath.OwedOn(statements, noTerms, new DateOnly(2040, 1, 1)));
    }

    [Fact]
    public async Task A_home_keeps_its_values_and_loans_and_works_out_what_is_owed_today()
    {
        var client = NewOwner();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = await client.PostAsJsonAsync("/api/homes", new { name = "  Huset  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var home = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/homes", new { name = " " })).StatusCode);

        await client.PutAsJsonAsync($"/api/homes/{home}/values", new { date = today.AddMonths(-6), value = 4000000m });
        await client.PutAsJsonAsync($"/api/homes/{home}/values", new { date = today.AddMonths(-1), value = 4200000m });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/homes/{home}/values", new { date = today.AddDays(1), value = 1m })).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/api/homes/{home}/values")).GetArrayLength());

        var loan = await client.PostAsJsonAsync($"/api/homes/{home}/loans",
            new { name = "Realkreditlån", interestPct = 4m, contributionPct = 0.5m, endDate = today.AddYears(30) });
        var loanId = (await loan.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/homes/{home}/loans",
            new { name = "Lån", interestPct = 4m, endDate = today.AddYears(10), interestOnlyUntil = today.AddYears(11) })).StatusCode);
        await client.PutAsJsonAsync($"/api/homes/{home}/loans/{loanId}/balances", new { date = today.AddMonths(-3), balance = 3000000m });

        var listed = (await client.GetFromJsonAsync<JsonElement>("/api/homes"))[0];
        Assert.Equal("Huset", listed.GetProperty("name").GetString());
        Assert.Equal(4200000m, listed.GetProperty("value").GetDecimal());
        var l = listed.GetProperty("loans")[0];
        Assert.Equal(3000000m, l.GetProperty("statement").GetDecimal());
        // Three monthly payments since the statement have paid some of it down.
        var owed = l.GetProperty("owed").GetDecimal();
        Assert.InRange(owed, 2980000m, 2995000m);
        Assert.Equal(owed, listed.GetProperty("owed").GetDecimal());
        Assert.Equal(4200000m - owed, listed.GetProperty("equity").GetDecimal());
        Assert.True(l.GetProperty("payment").GetProperty("repayment").GetDecimal() > 0);

        // Another user can't see or change it.
        var other = NewOwner();
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/homes")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/homes/{home}/values", new { date = today, value = 1m })).StatusCode);

        // Deleting the home removes its values and loans.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/homes/{home}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/homes")).EnumerateArray());
    }
}
