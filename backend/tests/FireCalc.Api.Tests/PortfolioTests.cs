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

        // A purchase entered afterwards with an earlier date still adds to the corrected cost.
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { id = novo.GetProperty("instrumentId").GetString() }, quantity = 20, unitPrice = 420, date = new DateOnly(2026, 3, 2) });
        var later = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0]
            .GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("isin").GetString() == "DK0062498333");
        Assert.Equal(20m, later.GetProperty("quantity").GetDecimal());
        Assert.Equal(7800m, later.GetProperty("costBasis").GetDecimal()); // 5,700 + 5 × 420
    }
    // The shape of Saxo's export: one sheet, shared strings, dates as Excel serial numbers.
    private static byte[] SaxoXlsx(params object[][] rows)
    {
        string[] header = ["Kunde-id", "Handelsdato", "Valørdato", "Type", "Instrument", "Instrumentets ISIN", "Instrumentvaluta", "Børsbeskrivelse", "Instrumentsymbol", "Begivenhed", "Beløb", "Ordre-ID", "Omregningssats"];
        var strings = new List<string>();
        string Cell(object v, int col, int row)
        {
            var r = $"{(char)('A' + col)}{row}";
            if (v is DateOnly d) return $"<x:c r=\"{r}\"><x:v>{d.ToDateTime(TimeOnly.MinValue).ToOADate()}</x:v></x:c>";
            if (v is decimal or int) return $"<x:c r=\"{r}\"><x:v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</x:v></x:c>";
            strings.Add((string)v);
            return $"<x:c r=\"{r}\" t=\"s\"><x:v>{strings.Count - 1}</x:v></x:c>";
        }
        var sheetRows = new[] { header.Cast<object>().ToArray() }.Concat(rows)
            .Select((cells, i) => $"<x:row r=\"{i + 1}\">{string.Concat(cells.Select((c, col) => Cell(c, col, i + 1)))}</x:row>");
        const string ns = "xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        var sheet = $"<x:worksheet {ns}><x:sheetData>{string.Concat(sheetRows)}</x:sheetData></x:worksheet>";
        var shared = $"<x:sst {ns}>{string.Concat(strings.Select(t => $"<x:si><x:t>{System.Security.SecurityElement.Escape(t)}</x:t></x:si>"))}</x:sst>";
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open());
                w.Write(xml);
            }
            Add("xl/workbook.xml", $"<x:workbook {ns}><x:sheets><x:sheet name=\"Transaktioner\" sheetId=\"1\" r:id=\"rId2\" /></x:sheets></x:workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId2\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
            Add("xl/worksheets/sheet1.xml", sheet);
            Add("xl/sharedStrings.xml", shared);
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task Saxo_export_imports_trades_dividends_and_cash()
    {
        var client = NewOwner();
        var account = await CreateAccount(client, "Saxo");
        var file = SaxoXlsx(
            ["1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), "Corporate action", "iShares S&P 500 Info Technology Sector UCITS ETF", "IE00B3WJKG14", "EUR", "Xetra", "QDVE:xetr", "Kontantudbytte", 10m, "", 7.46m],
            ["1", new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 24), "Handel", "iShares S&P 500 Info Technology Sector UCITS ETF", "IE00B3WJKG14", "EUR", "Xetra", "QDVE:xetr", "Købt 66 @ 46.63 EUR", -23061.96m, "5445589278", 7.49m],
            ["1", new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 9), "Handel", "Novo Nordisk B A/S", "DK0062498333", "DKK", "Copenhagen", "NOVOb:xcse", "Solgt -5 @ 2,203.00 DKK", 11005m, "5369220793", 1m],
            ["1", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 7), "Handel", "Novo Nordisk B A/S", "DK0062498333", "DKK", "Copenhagen", "NOVOb:xcse", "Købt 9 @ 2,000.00 DKK", -18000m, "5369220700", 1m],
            ["1", new DateOnly(2024, 12, 16), new DateOnly(2024, 12, 17), "Corporate action", "Palo Alto Networks Inc.", "US6974351057", "USD", "NASDAQ", "PANW:xnas", "Aktiesplit", 0m, "0", 1m],
            ["1", new DateOnly(2025, 3, 2), new DateOnly(2025, 3, 2), "Kontantbeløb", "", "", "DKK", "Unknown", "", "Renter", 24.41m, "", 1m],
            ["1", new DateOnly(2025, 3, 2), new DateOnly(2025, 3, 2), "Kontantbeløb", "", "", "DKK", "Unknown", "", "Renter", 24.41m, "", 1m],
            ["1", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1), "Kontantoverførsel", "", "", "DKK", "Unknown", "", "Indbetaling", 40000m, "", 1m]);

        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var res = await client.PostAsync($"/api/accounts/{account}/import/saxo?commit=true", content);
        var result = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, result.GetProperty("new").GetInt32()); // the two equal interest rows both count
        Assert.Contains("split", result.GetProperty("skipped")[0].GetProperty("reason").GetString());

        var again = await client.PostAsync($"/api/accounts/{account}/import/saxo?commit=true", new ByteArrayContent(file));
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("new").GetInt32());

        var acc = (await client.GetFromJsonAsync<JsonElement>("/api/portfolio")).GetProperty("accounts")[0];
        Assert.Equal(40000 - 18000 + 11005 - 23061.96m + 10 + 48.82m, acc.GetProperty("cash").GetDecimal());
        var positions = acc.GetProperty("positions").EnumerateArray().ToList();
        var qdve = positions.Single(p => p.GetProperty("isin").GetString() == "IE00B3WJKG14");
        Assert.Equal(66m, qdve.GetProperty("quantity").GetDecimal());
        Assert.Equal("QDVE.DE", qdve.GetProperty("symbol").GetString());
        var novo = positions.Single(p => p.GetProperty("isin").GetString() == "DK0062498333");
        Assert.Equal(4m, novo.GetProperty("quantity").GetDecimal());
        Assert.Equal("NOVO-B.CO", novo.GetProperty("symbol").GetString());
    }

    [Theory]
    [InlineData("QDVE:xetr", "QDVE.DE")]
    [InlineData("NOVOb:xcse", "NOVO-B.CO")]
    [InlineData("NDA_FI:xhel", "NDA-FI.HE")]
    [InlineData("NVDA:xnas", "NVDA")]
    [InlineData("ABC:xunknown", null)]
    public void Saxo_symbols_map_to_price_symbols(string saxo, string? expected) =>
        Assert.Equal(expected, FireCalc.Api.Portfolio.SaxoXlsx.YahooSymbol(saxo));
    [Fact]
    public async Task History_shows_value_and_a_return_that_ignores_deposits()
    {
        var m = factory.MarketData;
        var d0 = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        m.Prices["TWR.CO"] = new("DKK", [new(d0, 100m), new(d0.AddDays(1), 110m), new(d0.AddDays(3), 121m)]);
        var client = NewOwner();
        var account = await CreateAccount(client);

        // 10 shares at 100, then the price rises 10 %, 10 more are bought with new money, and it rises 10 % again.
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "TWR.CO" }, quantity = 10, unitPrice = 100, date = d0 });
        await client.PutAsJsonAsync($"/api/accounts/{account}/holdings", new { instrument = new { symbol = "TWR.CO" }, quantity = 20, unitPrice = 110, date = d0.AddDays(2) });

        var all = await client.GetFromJsonAsync<JsonElement>($"/api/portfolio/history?to={d0.AddDays(3):yyyy-MM-dd}");
        var points = all.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(d0.ToString("yyyy-MM-dd"), points[0].GetProperty("date").GetString());
        var last = points[^1];
        Assert.Equal(2420m, last.GetProperty("value").GetDecimal());
        Assert.Equal(2100m, last.GetProperty("netDeposits").GetDecimal());
        Assert.Equal(21m, last.GetProperty("returnPct").GetDecimal()); // 1.1 × 1.1, the new money doesn't count

        // A later start measures from that day.
        var part = await client.GetFromJsonAsync<JsonElement>($"/api/portfolio/history?from={d0.AddDays(1):yyyy-MM-dd}&to={d0.AddDays(3):yyyy-MM-dd}");
        var partPoints = part.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(0m, partPoints[0].GetProperty("returnPct").GetDecimal());
        Assert.Equal(10m, partPoints[^1].GetProperty("returnPct").GetDecimal());
    }

    [Fact]
    public async Task History_counts_shares_moved_in_as_money_put_in_not_as_a_gain()
    {
        var m = factory.MarketData;
        var d0 = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        m.Prices["MOVE.CO"] = new("DKK", [new(d0, 100m), new(d0.AddDays(2), 120m)]);
        var client = NewOwner();
        var account = await CreateAccount(client);

        await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new { date = d0, type = "deposit", amount = 1000 });
        // 10 shares arrive from another broker the next day, worth 1,000 at that day's price.
        var res = await client.PostAsJsonAsync($"/api/accounts/{account}/transactions", new
        {
            date = d0.AddDays(1),
            type = "securityIn",
            instrument = new { symbol = "MOVE.CO" },
            quantity = 10,
            amount = 0,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var history = await client.GetFromJsonAsync<JsonElement>($"/api/portfolio/history?to={d0.AddDays(2):yyyy-MM-dd}");
        var points = history.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(0m, points[1].GetProperty("returnPct").GetDecimal());
        Assert.Equal(2000m, points[1].GetProperty("netDeposits").GetDecimal());
        // Then the shares rise 20 %: 200 gained on 2,000.
        Assert.Equal(2200m, points[2].GetProperty("value").GetDecimal());
        Assert.Equal(10m, points[2].GetProperty("returnPct").GetDecimal());
    }
}
