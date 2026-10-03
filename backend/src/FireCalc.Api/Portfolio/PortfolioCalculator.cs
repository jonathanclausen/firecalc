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
    private const decimal Dust = 0.000001m;

    public static AccountHoldings Calculate(IEnumerable<PortfolioTransaction> transactions, DateOnly asOf)
    {
        var positions = new Dictionary<Guid, Position>();
        Position Pos(Guid id) => positions.TryGetValue(id, out var p) ? p : positions[id] = new Position();

        decimal cash = 0, netDeposits = 0;

        // Within a day, shares going out are handled before shares coming in, so that a split or
        // ISIN change booked as "out old, in new" carries the cost over to the new line.
        var days = transactions
            .Where(t => t.Date <= asOf)
            .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt)
            .GroupBy(t => t.Date);

        foreach (var day in days)
        {
            decimal carriedCost = 0;
            var ordered = day.OrderBy(t => t.Type == TransactionType.SecurityIn ? 1 : 0).ToList();
            var sharesIn = ordered.Where(t => t.Type == TransactionType.SecurityIn && t.InstrumentId is not null).Sum(t => t.Quantity);

            foreach (var t in ordered)
            {
                cash += t.Amount;
                if (t.Type == TransactionType.Deposit || t.Type == TransactionType.Withdrawal) netDeposits += t.Amount;
                if (t.InstrumentId is not { } id) continue;
                var p = Pos(id);

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

        var holdings = positions
            .Select(kv => new Holding(kv.Key, kv.Value.Quantity, Math.Round(kv.Value.Cost, 2), Math.Round(kv.Value.Realized, 2), kv.Value.Dividends))
            .ToList();
        return new AccountHoldings(
            holdings,
            cash,
            netDeposits,
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
