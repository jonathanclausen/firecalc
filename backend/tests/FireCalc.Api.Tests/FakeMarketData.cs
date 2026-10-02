using FireCalc.Api.Portfolio;

namespace FireCalc.Api.Tests;

public sealed class FakeMarketData : IMarketData
{
    /// <summary>ISIN or free-text query → symbols the search returns.</summary>
    public Dictionary<string, List<SymbolMatch>> Search { get; } = [];
    public Dictionary<string, PriceHistory> Prices { get; } = [];
    public Dictionary<string, List<DailyClose>> Fx { get; } = [];
    public int PriceRequests { get; private set; }

    public Task<List<SymbolMatch>> SearchAsync(string query, CancellationToken ct) =>
        Task.FromResult(Search.GetValueOrDefault(query) ?? []);

    public Task<PriceHistory?> GetDailyClosesAsync(string symbol, DateOnly from, CancellationToken ct)
    {
        PriceRequests++;
        return Task.FromResult(Prices.TryGetValue(symbol, out var p)
            ? p with { Closes = p.Closes.Where(c => c.Date >= from).ToList() }
            : null);
    }

    public Task<List<DailyClose>> GetFxRatesAsync(string currency, string quote, DateOnly from, CancellationToken ct) =>
        Task.FromResult((Fx.GetValueOrDefault(currency) ?? []).Where(r => r.Date >= from).ToList());
}
