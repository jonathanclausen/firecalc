using FireCalc.Api.Data;

namespace FireCalc.Api.Portfolio;

public record Holding(
    Guid InstrumentId,
    decimal Quantity,
    /// <summary>What the shares still held cost, in the user's currency (average cost method).</summary>
    decimal CostBasis,
    decimal RealizedGain,
    decimal Dividends);

public record AccountHoldings(
    List<Holding> Holdings,
    decimal Cash,
    decimal NetDeposits,
    decimal RealizedGain,
    decimal Dividends);

/// <summary>
/// Replays an account's transactions into positions. Cost uses the average cost method
/// (gennemsnitsmetoden, as Danish tax does): a sale takes out the average cost of the shares sold.
/// </summary>
public static class PortfolioCalculator
{
    public static AccountHoldings Calculate(IEnumerable<PortfolioTransaction> transactions, DateOnly asOf)
    {
        var replay = new PortfolioReplay();
        foreach (var day in transactions.Where(t => t.Date <= asOf).GroupBy(t => t.Date).OrderBy(g => g.Key))
            replay.ApplyDay(day);
        return replay.Result();
    }
}

/// <summary>
/// Plays transactions forward one day at a time, so a history can be valued day by day without
/// replaying everything for each date. Cost uses the average cost method (gennemsnitsmetoden, as Danish
/// tax does): a sale takes out the average cost of the shares sold.
/// </summary>
public sealed class PortfolioReplay
{
    private const decimal Dust = 0.000001m;
    private readonly Dictionary<Guid, Position> positions = [];

    public decimal Cash { get; private set; }
    public decimal NetDeposits { get; private set; }

    /// <summary>Shares held per instrument, with what they cost, in the user's currency.</summary>
    public IEnumerable<(Guid InstrumentId, decimal Quantity, decimal Cost)> Holdings =>
        positions.Where(kv => kv.Value.Quantity > Dust).Select(kv => (kv.Key, kv.Value.Quantity, kv.Value.Cost));

    /// <summary>Applies one day's transactions. Days must come in date order.</summary>
    public void ApplyDay(IEnumerable<PortfolioTransaction> day)
    {
        // Within a day, shares going out are handled before shares coming in, so that a split or
        // ISIN change booked as "out old, in new" carries the cost over to the new line.
        decimal carriedCost = 0;
        var ordered = day.OrderBy(t => t.CreatedAt).OrderBy(t => t.Type == TransactionType.SecurityIn ? 1 : 0).ToList();
        var sharesIn = ordered.Where(t => t.Type == TransactionType.SecurityIn && t.InstrumentId is not null).Sum(t => t.Quantity);

        foreach (var t in ordered)
        {
            Cash += t.Amount;
            if (t.Type == TransactionType.Deposit || t.Type == TransactionType.Withdrawal) NetDeposits += t.Amount;
            if (t.InstrumentId is not { } id) continue;
            if (!positions.TryGetValue(id, out var p)) positions[id] = p = new Position();

            switch (t.Type)
            {
                case TransactionType.Buy:
                    p.Quantity += t.Quantity;
                    p.Cost += -t.Amount;
                    break;
                case TransactionType.Sell:
                {
                    var cost = p.TakeOut(t.Quantity);
                    p.Realized += t.Amount - cost;
                    break;
                }
                case TransactionType.SecurityOut:
                    carriedCost += p.TakeOut(t.Quantity) + t.Amount;
                    break;
                case TransactionType.SecurityIn:
                    p.Quantity += t.Quantity;
                    p.Cost += -t.Amount + (sharesIn > 0 ? carriedCost * t.Quantity / sharesIn : 0);
                    break;
                case TransactionType.CostCorrection when t.CostChange is { } change && p.Quantity > Dust:
                    p.Cost = Math.Max(0, p.Cost + change);
                    break;
                case TransactionType.Dividend:
                case TransactionType.Tax:
                    // Withholding tax booked against a share reduces that share's dividend.
                    p.Dividends += t.Amount;
                    break;
            }
        }
    }

    public AccountHoldings Result()
    {
        var holdings = positions
            .Select(kv => new Holding(kv.Key, kv.Value.Quantity, Math.Round(kv.Value.Cost, 2), Math.Round(kv.Value.Realized, 2), kv.Value.Dividends))
            .ToList();
        return new AccountHoldings(
            holdings,
            Cash,
            NetDeposits,
            holdings.Sum(h => h.RealizedGain),
            holdings.Sum(h => h.Dividends));
    }

    private sealed class Position
    {
        public decimal Quantity;
        public decimal Cost;
        public decimal Realized;
        public decimal Dividends;

        /// <summary>Removes shares and returns the average cost they carried.</summary>
        public decimal TakeOut(decimal quantity)
        {
            var cost = Quantity > Dust ? Cost * Math.Min(1, quantity / Quantity) : 0;
            Quantity -= quantity;
            Cost -= cost;
            if (Math.Abs(Quantity) < Dust) Quantity = Cost = 0;
            return cost;
        }
    }
}
