using FireCalc.Api.Data;

namespace FireCalc.Api.Homes;

/// <summary>
/// A loan's payments as a Danish annuity loan paid monthly: the same ydelse each month until the end date,
/// split into interest and afdrag; during afdragsfrihed only interest is paid. Bidrag is a cost on what is
/// owed and doesn't pay the loan down. The same steps run in the browser's projection.
/// </summary>
public static class MortgageMath
{
    public record Terms(decimal? InterestPct, decimal? ContributionPct, DateOnly? EndDate, DateOnly? InterestOnlyUntil)
    {
        public static Terms Of(Mortgage m) => new(m.InterestPct, m.ContributionPct, m.EndDate, m.InterestOnlyUntil);

        /// <summary>Without a rate and an end date nothing can be worked out, and the statement stands.</summary>
        public bool Amortizes => InterestPct is not null && EndDate is not null;
    }

    /// <summary>One month's payment on what is owed at its start.</summary>
    public record Month(decimal Interest, decimal Contribution, decimal Repayment)
    {
        public decimal Total => Interest + Contribution + Repayment;
    }

    /// <summary>The payment for the month starting on <paramref name="date"/>, with <paramref name="owed"/> at its start.</summary>
    public static Month Payment(decimal owed, Terms terms, DateOnly date)
    {
        if (owed <= 0) return new(0, 0, 0);
        var contribution = owed * (terms.ContributionPct ?? 0) / 100 / 12;
        if (!terms.Amortizes) return new(0, contribution, 0);

        var r = terms.InterestPct!.Value / 100 / 12;
        var interest = owed * terms.InterestPct!.Value / 1200;
        if (terms.InterestOnlyUntil is { } until && date < until) return new(interest, contribution, 0);

        var left = MonthsBetween(date, terms.EndDate!.Value);
        if (left <= 1) return new(interest, contribution, owed);
        var annuity = r == 0 ? owed / left : owed * r / (1 - (decimal)Math.Pow(1 + (double)r, -left));
        return new(interest, contribution, Math.Min(owed, annuity - interest));
    }

    /// <summary>
    /// What is owed on <paramref name="day"/>: the latest statement on or before it, paid down by each whole month
    /// since. Null before the first statement.
    /// </summary>
    public static decimal? OwedOn(IReadOnlyList<MortgageBalance> statements, Terms terms, DateOnly day)
    {
        var statement = statements.LastOrDefault(s => s.Date <= day);
        if (statement is null) return null;
        var owed = statement.Balance;
        if (!terms.Amortizes) return owed;
        for (var month = statement.Date; month.AddMonths(1) <= day && owed > 0; month = month.AddMonths(1))
            owed -= Payment(owed, terms, month).Repayment;
        return Math.Round(Math.Max(0, owed), 2);
    }

    /// <summary>Whole months from <paramref name="from"/> until <paramref name="to"/>, at least 0.</summary>
    public static int MonthsBetween(DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day) months--;
        return Math.Max(0, months);
    }
}
