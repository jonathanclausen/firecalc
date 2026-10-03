using System.Globalization;
using System.Text.Json;

namespace FireCalc.Api.Portfolio;

public record SymbolMatch(string Symbol, string Name, string? Exchange, string? Type);
public record DailyClose(DateOnly Date, decimal Close);
public record PriceHistory(string Currency, List<DailyClose> Closes);

/// <summary>Where prices and exchange rates come from. Swappable so tests don't call the internet.</summary>
public interface IMarketData
{
    Task<List<SymbolMatch>> SearchAsync(string query, CancellationToken ct);
    Task<PriceHistory?> GetDailyClosesAsync(string symbol, DateOnly from, CancellationToken ct);
    /// <summary>Rates as "one <paramref name="currency"/> costs this many <paramref name="quote"/>".</summary>
    Task<List<DailyClose>> GetFxRatesAsync(string currency, string quote, DateOnly from, CancellationToken ct);
}

/// <summary>
/// Free sources: Yahoo Finance's chart and search endpoints (no key, unofficial) for prices,
/// and Frankfurter (ECB reference rates) for currencies.
/// </summary>
public sealed class YahooMarketData(HttpClient http, ILogger<YahooMarketData> log) : IMarketData
{
    public async Task<List<SymbolMatch>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=8&newsCount=0&listsCount=0";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("quotes", out var quotes)) return [];

        var list = new List<SymbolMatch>();
        foreach (var q in quotes.EnumerateArray())
        {
            var symbol = Str(q, "symbol");
            if (symbol is null) continue;
            list.Add(new SymbolMatch(symbol, Str(q, "longname") ?? Str(q, "shortname") ?? symbol, Str(q, "exchDisp") ?? Str(q, "exchange"), Str(q, "quoteType")));
        }
        return list;
    }

    public async Task<PriceHistory?> GetDailyClosesAsync(string symbol, DateOnly from, CancellationToken ct)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var end = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1d&events=div%2Csplit";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null) return null;

        var result = doc.RootElement.GetProperty("chart").GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0) return null;
        var r = result[0];
        var currency = Str(r.GetProperty("meta"), "currency");
        if (currency is null) return null;

        var closes = new List<DailyClose>();
        if (r.TryGetProperty("timestamp", out var stamps)
            && r.TryGetProperty("indicators", out var ind)
            && ind.GetProperty("quote")[0].TryGetProperty("close", out var close))
        {
            var offset = r.GetProperty("meta").TryGetProperty("gmtoffset", out var g) ? g.GetInt32() : 0;
            for (var i = 0; i < stamps.GetArrayLength() && i < close.GetArrayLength(); i++)
            {
                if (close[i].ValueKind != JsonValueKind.Number) continue;
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(stamps[i].GetInt64() + offset).UtcDateTime);
                closes.Add(new DailyClose(date, close[i].GetDecimal()));
            }
        }

        // London quotes many lines in pence ("GBp"); store them in pounds so FX rates apply.
        if (currency is "GBp" or "GBX")
        {
            currency = "GBP";
            closes = closes.Select(c => c with { Close = c.Close / 100 }).ToList();
        }
        return new PriceHistory(currency.ToUpperInvariant(), closes);
    }

    public async Task<List<DailyClose>> GetFxRatesAsync(string currency, string quote, DateOnly from, CancellationToken ct)
    {
        var url = $"https://api.frankfurter.dev/v1/{from:yyyy-MM-dd}..?base={currency}&symbols={quote}";
        using var doc = await GetJsonAsync(url, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("rates", out var rates)) return [];

        var list = new List<DailyClose>();
        foreach (var day in rates.EnumerateObject())
        {
            if (day.Value.TryGetProperty(quote, out var rate))
                list.Add(new DailyClose(DateOnly.ParseExact(day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture), rate.GetDecimal()));
        }
        return list;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Market data request {Url} returned {Status}", url, (int)res.StatusCode);
                return null;
            }
            return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Market data request {Url} failed", url);
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
