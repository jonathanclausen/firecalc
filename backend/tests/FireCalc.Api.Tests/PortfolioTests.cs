using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FireCalc.Api.Data;
using FireCalc.Api.Portfolio;

namespace FireCalc.Api.Tests;

public class PortfolioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient NewOwner() => factory.CreateClientFor(subject: Guid.NewGuid().ToString());

    // Header and row shapes copied from a real Nordnet export (Danish, UTF-16, tab-separated).
    private const string Header =
        "Id\tBogføringsdag\tHandelsdag\tValørdag\tDepot\tTransaktionstype\tVærdipapirer\tVærdipapirtype\tISIN\tAntal\tKurs\tRente\tSamlede afgifter\tValuta\tBeløb\tValuta\tIndkøbsværdi\tValuta\tResultat\tValuta\tTotalt antal\tSaldo\tVekslingskurs\tTransaktionstekst\tMakuleringsdato\tNotanummer\tVerifikationsnummer\tKurtage\tValuta";

    private static string Row(string id, string date, string type, string name, string isin, string qty, string price, string amount, string cancelled = "") =>
        $"{id}\t{date}\t{date}\t{date}\t9999\t{type}\t{name}\tAktier\t{isin}\t{qty}\t{price}\t0\t0\tDKK\t{amount}\tDKK\t0\tDKK\t0\tDKK\t0\t0\t1\t\t{cancelled}\t1\t1\t0\tDKK";

    private static byte[] Utf16(params string[] lines) =>
        [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")];

    private static readonly byte[] Export = Utf16(
        Header,
        Row("105", "2026-03-02", "UDBYTTE", "NOVO B", "DK0062498333", "10", "5", "50"),
        Row("104", "2026-02-20", "SOLGT", "NOVO B", "DK0062498333", "5", "450", "2.250"),
        Row("103", "2026-02-10", "KØBT", "TSLA", "US88160R1014", "2", "200", "-2.800,00"),
        Row("102", "2026-02-02", "KØBT", "NOVO B", "DK0062498333", "15", "400", "-6.000"),
        Row("101", "2026-02-02", "KØBT", "NOVO B", "DK0062498333", "1", "400", "-400", cancelled: "2026-02-03"),
        Row("100", "2026-02-01", "INDBETALING", "", "", "NA", "NA", "10.000"),
        "");

    private static async Task<string> CreateAccount(HttpClient client, string name = "Nordnet", string type = "investment")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new { name, type });
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> Import(HttpClient client, string accountId, byte[] file, bool commit)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PostAsync($"/api/accounts/{accountId}/import/nordnet?commit={commit.ToString().ToLowerInvariant()}", content);
    }

    private void SeedPrices()
    {
        var m = factory.MarketData;
        m.Search["DK0062498333"] = [new("NOVO-B.ST", "Novo (Stockholm)", "STO", "EQUITY"), new("NOVO-B.CO", "Novo Nordisk B", "CPH", "EQUITY")];
        m.Search["US88160R1014"] = [new("TSLA", "Tesla", "NMS", "EQUITY")];
        m.Prices["NOVO-B.CO"] = new("DKK", [new(new(2026, 3, 9), 480m), new(new(2026, 3, 10), 500m)]);
        m.Prices["TSLA"] = new("USD", [new(new(2026, 3, 9), 210m), new(new(2026, 3, 10), 200m)]);
        m.Fx["USD"] = [new(new(2026, 3, 9), 7m), new(new(2026, 3, 10), 7m)];
    }

    [Fact]
    public void Parser_reads_a_danish_utf16_export()
    {
        var result = NordnetCsv.Parse(Export, "DKK");

        Assert.Null(result.Error);
        Assert.Equal(5, result.Rows.Count);
        Assert.Contains(result.Skipped, s => s.Reason.Contains("Cancelled"));

        var buy = result.Rows.Single(r => r.ExternalId == "103");
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(new DateOnly(2026, 2, 10), buy.Date);
        Assert.Equal("US88160R1014", buy.Isin);
        Assert.Equal(2m, buy.Quantity);
        Assert.Equal(-2800m, buy.Amount);

        Assert.Equal(TransactionType.Sell, result.Rows.Single(r => r.ExternalId == "104").Type);
        Assert.Equal(TransactionType.Dividend, result.Rows.Single(r => r.ExternalId == "105").Type);
        var deposit = result.Rows.Single(r => r.ExternalId == "100");
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        Assert.Equal(10000m, deposit.Amount);
        Assert.Null(deposit.Isin);
    }

    [Fact]
    public void Parser_reads_the_older_latin1_semicolon_export()
    {
        var text = "Id;Bogføringsdag;Handelsdag;Valørdag;Transaktionstype;Værdipapirer;Instrumenttype;ISIN;Antal;Kurs;Rente;Afgifter;Beløb;Valuta\n"
            + "123456789;2019-01-02;2019-01-02;2019-01-04;KØBT;DANSKE;Aktie;DK0010274414;10;100,00;0,00;29,00;-1.029,00;DKK\n";
        var result = NordnetCsv.Parse(Encoding.Latin1.GetBytes(text), "DKK");

        var row = Assert.Single(result.Rows);
        Assert.Equal(TransactionType.Buy, row.Type);
        Assert.Equal(-1029m, row.Amount);
    }

    [Theory]
    [InlineData("UDBYTTESKAT", TransactionType.Tax)]
    [InlineData("HÆVNING", TransactionType.Withdrawal)]
    [InlineData("INDSÆTTELSE", TransactionType.Deposit)]
    [InlineData("DEBITRENTE", TransactionType.Interest)]
    [InlineData("KÖPT", TransactionType.Buy)]
    [InlineData("SÅLT", TransactionType.Sell)]
    public void Transaction_types_are_classified(string raw, TransactionType expected) =>
        Assert.Equal(expected, NordnetCsv.Classify(raw, -1, 0, false));

    [Fact]
    public void Calculator_uses_average_cost_and_carries_cost_through_a_split()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        PortfolioTransaction Tx(int day, TransactionType type, Guid? id, decimal qty, decimal amount) =>
            new() { Date = new(2026, 1, day), Type = type, InstrumentId = id, Quantity = qty, Amount = amount, CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(day) };

        var result = PortfolioCalculator.Calculate(
        [
            Tx(1, TransactionType.Deposit, null, 0, 5000),
            Tx(2, TransactionType.Buy, a, 10, -1000),
            Tx(3, TransactionType.Buy, a, 10, -2000),
            Tx(4, TransactionType.Sell, a, 5, 1000),
            // A split booked as the old line out and the new line in.
            Tx(5, TransactionType.SecurityIn, b, 30, 0),
            Tx(5, TransactionType.SecurityOut, a, 15, 0),
            Tx(6, TransactionType.Dividend, b, 0, 90),
            Tx(6, TransactionType.Tax, b, 0, -24),
        ], new DateOnly(2026, 12, 31));

        Assert.Equal(5000 - 1000 - 2000 + 1000 + 90 - 24, result.Cash);
        Assert.Equal(5000, result.NetDeposits);
        Assert.Equal(250, result.RealizedGain); // 1000 received for 5 shares that cost 150 each.
        var newLine = result.Holdings.Single(h => h.InstrumentId == b);
        Assert.Equal(30, newLine.Quantity);
        Assert.Equal(2250, newLine.CostBasis);
        Assert.Equal(66, newLine.Dividends);
        Assert.Equal(0, result.Holdings.Single(h => h.InstrumentId == a).Quantity);
    }

    [Fact]
    public async Task Import_previews_then_saves_and_skips_rows_already_imported()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);

        var preview = await (await Import(client, account, Export, commit: false)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(preview.GetProperty("committed").GetBoolean());
        Assert.Equal(5, preview.GetProperty("new").GetInt32());
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>($"/api/accounts/{account}/transactions") ?? []);

        var saved = await (await Import(client, account, Export, commit: true)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(saved.GetProperty("committed").GetBoolean());
        Assert.Equal(5, (await client.GetFromJsonAsync<JsonElement[]>($"/api/accounts/{account}/transactions"))!.Length);

        var again = await (await Import(client, account, Export, commit: true)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("new").GetInt32());
        Assert.Equal(5, again.GetProperty("duplicates").GetInt32());
    }

    [Fact]
    public async Task Import_rejects_files_that_are_not_nordnet_exports()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);
        var res = await Import(client, account, Encoding.UTF8.GetBytes("foo,bar\n1,2\n"), commit: false);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Transactions_only_go_into_investment_accounts()
    {
        var client = NewOwner();
        var savings = await CreateAccount(client, "Opsparing", "savings");
        var res = await client.PostAsJsonAsync($"/api/accounts/{savings}/transactions", new { date = "2026-01-01", type = "deposit", amount = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Portfolio_values_positions_with_prices_and_exchange_rates()
    {
        SeedPrices();
        var client = NewOwner();
        var account = await CreateAccount(client);
        await Import(client, account, Export, commit: true);

        var portfolio = await client.GetFromJsonAsync<JsonElement>("/api/portfolio?date=2026-03-10");
        var acc = Assert.Single(portfolio.GetProperty("accounts").EnumerateArray());
        var positions = acc.GetProperty("positions").EnumerateArray().ToList();

        // 10 Novo at 500 kr, and 2 Tesla at 200 USD × 7.
        var novo = positions.Single(p => p.GetProperty("isin").GetString() == "DK0062498333");
        Assert.Equal("NOVO-B.CO", novo.GetProperty("symbol").GetString());
        Assert.Equal(5000m, novo.GetProperty("value").GetDecimal());
        Assert.Equal(4000m, novo.GetProperty("costBasis").GetDecimal());
        Assert.Equal(200m, novo.GetProperty("dayChange").GetDecimal());
        var tesla = positions.Single(p => p.GetProperty("isin").GetString() == "US88160R1014");
        Assert.Equal(2800m, tesla.GetProperty("value").GetDecimal());
        Assert.Equal("USD", tesla.GetProperty("currency").GetString());

        // Cash: 10.000 in, 6.000 + 2.800 out, 2.250 + 50 back.
        Assert.Equal(3500m, acc.GetProperty("cash").GetDecimal());
        Assert.Equal(11300m, acc.GetProperty("value").GetDecimal());
        Assert.Equal(1300m, acc.GetProperty("growth").GetDecimal());
        Assert.Equal(250m, acc.GetProperty("realizedGain").GetDecimal());
        Assert.Equal(11300m, portfolio.GetProperty("value").GetDecimal());

        var values = await client.GetFromJsonAsync<JsonElement>("/api/portfolio/values?date=2026-03-10");
        Assert.Equal(11300m, values[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Positions_without_a_price_fall_back_to_cost()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);
        var res = await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new
        {
            date = "2026-01-05",
            type = "buy",
            instrument = new { symbol = "NOPRICE.CO", name = "Unknown fund" },
            quantity = 4,
            amount = -1000,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var portfolio = await client.GetFromJsonAsync<JsonElement>("/api/portfolio?date=2026-03-10");
        var position = portfolio.GetProperty("accounts")[0].GetProperty("positions")[0];
        Assert.True(position.GetProperty("priceMissing").GetBoolean());
        Assert.Equal(1000m, position.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Manual_transactions_can_be_edited_and_deleted_and_validate_signs()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);

        var bad = await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new { date = "2026-01-05", type = "buy", instrument = new { symbol = "AAA" }, quantity = 1, amount = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var created = await (await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new { date = "2026-01-05", type = "deposit", amount = 100 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var edit = await client.PutAsJsonAsync($"/api/transactions/{id}", new { date = "2026-01-06", type = "deposit", amount = 200, note = "løn" });
        Assert.Equal(200m, (await edit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());

        var other = NewOwner();
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/transactions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/accounts/{account}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Correcting_a_symbol_fetches_prices_for_the_new_one()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);
        var tx = await (await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new
        {
            date = "2026-03-01", type = "buy", instrument = new { isin = "DK0000000001", name = "Lokal fond" }, quantity = 10, amount = -1000,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var instrumentId = tx.GetProperty("instrumentId").GetString();

        factory.MarketData.Prices["LOKAL.CO"] = new("DKK", [new(new(2026, 3, 10), 120m)]);
        var put = await client.PutAsJsonAsync($"/api/instruments/{instrumentId}", new { symbol = "lokal.co" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var portfolio = await client.GetFromJsonAsync<JsonElement>("/api/portfolio?date=2026-03-10");
        Assert.Equal(1200m, portfolio.GetProperty("accounts")[0].GetProperty("positions")[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Setting_a_share_count_books_the_change_without_touching_cash()
    {
        var m = factory.MarketData;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        m.Prices["VWS.CO"] = new("DKK", [new(today.AddDays(-1), 100m), new(today, 110m)]);
        m.Prices["ERIC-B.ST"] = new("SEK", [new(today, 80m)]);
        m.Fx["SEK"] = [new(today.AddDays(-1), 0.6m)];
        var client = NewOwner();
        var account = await CreateAccount(client);

        var quote = await client.GetFromJsonAsync<JsonElement>("/api/instruments/quote?symbol=VWS.CO");
        Assert.Equal(110m, quote.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/instruments/quote?symbol=NOPE")).StatusCode);

        // The first shares need what was paid, so the return means something.
        var missing = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "VWS.CO", name = "Vestas" }, quantity = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var first = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "VWS.CO", name = "Vestas" }, quantity = 10, unitPrice = 90 });
        Assert.Equal(900m, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());

        // Buying more without a price uses today's close.
        var more = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "VWS.CO" }, quantity = 12 });
        Assert.Equal(220m, (await more.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());

        // Selling 6 for a total you type yourself.
        var sold = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "VWS.CO" }, quantity = 6, amount = 600 });
        Assert.Equal(-6m, (await sold.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("change").GetDecimal());

        // A Swedish share priced in kronor is converted at the stored rate.
        var ericsson = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "ERIC-B.ST" }, quantity = 2, unitPrice = 150 });
        Assert.Equal(180m, (await ericsson.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());

        var acc = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0];
        var vestas = acc.GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("symbol").GetString() == "VWS.CO");
        Assert.Equal(6m, vestas.GetProperty("quantity").GetDecimal());
        Assert.Equal(560m, vestas.GetProperty("costBasis").GetDecimal()); // (900 + 220) / 12 × 6
        Assert.Equal(0m, acc.GetProperty("cash").GetDecimal());
    }

    [Fact]
    public async Task Setting_a_share_count_without_any_price_asks_for_one()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "UNKNOWN.CO" }, quantity = 5, amount = 250 });

        var res = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "UNKNOWN.CO" }, quantity = 8 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("unitPrice", await res.Content.ReadAsStringAsync());

        var ok = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "UNKNOWN.CO" }, quantity = 8, unitPrice = 52 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // A new GAK with the new count is enough when there is no price to trade at.
        var withGak = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "UNKNOWN.CO" }, quantity = 10, averagePrice = 51 });
        Assert.Equal(HttpStatusCode.OK, withGak.StatusCode);
        var position = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0].GetProperty("positions")[0];
        Assert.Equal(10m, position.GetProperty("quantity").GetDecimal());
        Assert.Equal(510m, position.GetProperty("costBasis").GetDecimal());
    }
    [Fact]
    public async Task A_purchase_date_converts_at_that_days_exchange_rate()
    {
        var m = factory.MarketData;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bought = today.AddDays(-200);
        m.Prices["EQNR.OL"] = new("NOK", [new(today, 250m)]);
        m.Prices["DNB.OL"] = new("NOK", [new(today, 200m)]);
        m.Fx["NOK"] = [new(bought.AddDays(-1), 0.6m), new(today.AddDays(-1), 0.7m)];
        var client = NewOwner();
        var account = await CreateAccount(client);

        // Today's rate first, so the rates are already checked when the older date comes in.
        var dnb = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "DNB.OL" }, quantity = 1, unitPrice = 200 });
        Assert.Equal(140m, (await dnb.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());

        var res = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "EQNR.OL" }, quantity = 10, unitPrice = 150, date = bought });
        Assert.Equal(900m, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal()); // 10 × 150 × 0.60

        var acc = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0];
        var equinor = acc.GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("symbol").GetString() == "EQNR.OL");
        Assert.Equal(1750m, equinor.GetProperty("value").GetDecimal()); // 10 × 250 × 0.70
        Assert.Equal(850m, equinor.GetProperty("gain").GetDecimal()); // share and currency rise together

        var future = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "EQNR.OL" }, quantity = 12, unitPrice = 150, date = today.AddDays(2) });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }
    [Fact]
    public async Task Correcting_the_average_price_rescales_what_was_paid()
    {
        var m = factory.MarketData;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        m.Prices["NESN.SW"] = new("CHF", [new(today, 30m)]);
        m.Fx["CHF"] = [new(today.AddDays(-1), 8m)];
        var client = NewOwner();
        var account = await CreateAccount(client);

        // A typo: 90 instead of 24.07, then 3 more bought at today's price.
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "NESN.SW" }, quantity = 537, unitPrice = 90 });
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "NESN.SW" }, quantity = 540 });

        var res = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "NESN.SW" }, quantity = 540, averagePrice = 24.07m });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var acc = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0];
        var position = acc.GetProperty("positions")[0];
        Assert.Equal(540m, position.GetProperty("quantity").GetDecimal());
        Assert.InRange(position.GetProperty("costBasis").GetDecimal(), 540 * 24.07m * 8 - 0.05m, 540 * 24.07m * 8 + 0.05m);
        Assert.Equal(0m, acc.GetProperty("cash").GetDecimal());
    }
    [Fact]
    public async Task The_average_price_of_imported_shares_can_be_corrected()
    {
        var client = NewOwner();
        var account = await CreateAccount(client);
        var file = Utf16(
            Header,
            Row("201", "2026-03-01", "KØBT", "NOVO B", "DK0062498333", "15", "400", "-6.000"),
            Row("200", "2023-03-30", "INDLÆG  OVERF.", "cBrain", "DK0060030286", "40", "0", "0"),
            "");
        await Import(client, account, file, commit: true);
        var cbrain = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0]
            .GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("isin").GetString() == "DK0060030286");
        Assert.Equal(0m, cbrain.GetProperty("costBasis").GetDecimal());

        var instrument = cbrain.GetProperty("instrumentId").GetString();
        var res = await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { id = instrument }, quantity = 40, averagePrice = 52.5m });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Importing the same file again keeps the correction, and no money moved.
        await Import(client, account, file, commit: true);
        var acc = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0];
        var corrected = acc.GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("isin").GetString() == "DK0060030286");
        Assert.Equal(2100m, corrected.GetProperty("costBasis").GetDecimal());
        Assert.Equal(-6000m, acc.GetProperty("cash").GetDecimal());

        // Correcting the bought shares keeps their cost in line with what Nordnet charged per share.
        var novo = acc.GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("isin").GetString() == "DK0062498333");
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { id = novo.GetProperty("instrumentId").GetString() }, quantity = 15, averagePrice = 380 });
        var after = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0]
            .GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("isin").GetString() == "DK0062498333");
        Assert.Equal(5700m, after.GetProperty("costBasis").GetDecimal());
    }
}
