using FireCalc.Api.Data;
using FireCalc.Api.Endpoints;
using FireCalc.Api.Homes;

namespace FireCalc.Api.Mcp;

/// <summary>
/// The scenario projection from the Fremtid page (frontend core/finance/projection.ts), so the MCP tools can
/// answer with the same numbers the app shows. Month by month from today: balances grow, savings go into
/// investments until the FIRE age, then the withdrawal rate is taken out (during a break: the yearly
/// spending, raised with inflation). Home loans are paid down by their terms; other loans stay. Keep the
/// two in step.
/// </summary>
public static class ScenarioProjection
{
    public record Start(double Investments, double Savings, double HomeValue, List<(double Owed, MortgageMath.Terms Terms)> HomeLoans, double OtherLoans);

    public record Point(DateOnly Date, double Age, double Investments, double Savings, double HomeEquity, double NetWorth, double Liquid, double Withdrawal);

    public record Result(List<Point> Points, double? DepletedAge, double? LoansPaidAge);

    /// <summary>Splits today's accounts the way the projection grows them, as the Fremtid page does.</summary>
    public static Start StartFrom(DashboardEndpoints.DashboardDto dashboard)
    {
        double investments = 0, savings = 0, home = 0, other = 0;
        var loans = new List<(double, MortgageMath.Terms)>();
        foreach (var a in dashboard.Accounts)
        {
            switch (a.Type)
            {
                case AccountType.Investment: investments += (double)a.Value; break;
                case AccountType.Savings or AccountType.Cash: savings += (double)a.Value; break;
                case AccountType.Property:
                    home += (double)(a.HomeValue ?? a.Value);
                    foreach (var l in a.Loans ?? [])
                        loans.Add(((double)l.Owed, new MortgageMath.Terms(l.InterestPct, null, l.EndDate, l.InterestOnlyUntil)));
                    break;
                case AccountType.Loan: other += -(double)a.Value; break;
            }
        }
        return new Start(investments, savings, home, loans, other);
    }

    public static Result Project(Start start, Scenario s, IReadOnlyList<ScenarioEvent> events, DateOnly birthDate, DateOnly today, double endAge = 90)
    {
        var startAge = AgeOn(birthDate, today);
        var months = Math.Max(0, (int)Math.Ceiling((endAge - startAge) * 12));
        double Monthly(decimal pct) => Math.Pow(1 + (double)pct / 100, 1.0 / 12) - 1;
        var ri = Monthly(s.InvestmentReturnPct);
        var rs = Monthly(s.SavingsReturnPct);
        var rh = Monthly(s.HomeGrowthPct);
        var inflation = 1 + (double)s.InflationPct / 100;
        var fireAge = (double)s.FireAge;
        var savingsChanges = events.Where(e => e.Kind == ScenarioEventKind.Savings).OrderBy(e => e.Age).ToList();

        var investments = start.Investments;
        var savings = start.Savings;
        var home = start.HomeValue;
        var owed = start.HomeLoans.Select(l => l.Owed).ToArray();
        double HomeOwed() => owed.Sum();
        double? loansPaidAge = null, depletedAge = null;
        double yearlyWithdrawal = 0, withdrawing = 0;
        int? firstFireMonth = null;
        var points = new List<Point>();

        void Push(int m, double age) => points.Add(new Point(today.AddMonths(m), age, investments, savings,
            home - HomeOwed(), investments + savings + home - HomeOwed() - start.OtherLoans, investments + savings, withdrawing));

        Push(0, startAge);
        for (var m = 1; m <= months; m++)
        {
            var prevAge = startAge + (m - 1) / 12.0;
            var age = startAge + m / 12.0;
            investments *= 1 + ri;
            savings *= 1 + rs;
            home *= 1 + rh;
            var monthStart = today.AddMonths(m - 1);
            for (var i = 0; i < owed.Length; i++)
                owed[i] = Math.Max(0, owed[i] - (double)MortgageMath.Payment((decimal)owed[i], start.HomeLoans[i].Terms, monthStart).Repayment);
            if (loansPaidAge is null && owed.Length > 0 && HomeOwed() < 0.005) loansPaidAge = age;

            var onBreak = events.Any(e => e.Kind == ScenarioEventKind.Break && age > (double)e.Age && age <= (double)(e.Age + (e.Years ?? 0)));
            double flow;
            if (age > fireAge)
            {
                firstFireMonth ??= m;
                if ((m - firstFireMonth.Value) % 12 == 0)
                    yearlyWithdrawal = Math.Max(investments + savings, 0) * (double)s.WithdrawalPct / 100;
                withdrawing = yearlyWithdrawal;
                flow = -yearlyWithdrawal / 12;
            }
            else if (onBreak)
            {
                withdrawing = (double)s.YearlySpending * Math.Pow(inflation, m / 12.0);
                flow = -withdrawing / 12;
            }
            else
            {
                withdrawing = 0;
                var change = savingsChanges.LastOrDefault(e => (double)e.Age < age);
                flow = change is not null ? (double)(change.Amount ?? 0) : (double)s.MonthlySavings;
            }
            foreach (var e in events)
                if (e.Kind == ScenarioEventKind.LumpSum && (double)e.Age > prevAge && (double)e.Age <= age) flow += (double)(e.Amount ?? 0);

            if (flow >= 0) investments += flow;
            else
            {
                var need = -flow;
                var fromSavings = Math.Min(Math.Max(savings, 0), need);
                savings -= fromSavings;
                need -= fromSavings;
                var fromInvestments = Math.Min(Math.Max(investments, 0), need);
                investments -= fromInvestments;
                need -= fromInvestments;
                if (need > 0.005 && depletedAge is null) depletedAge = age;
            }
            Push(m, age);
        }
        return new Result(points, depletedAge, loansPaidAge);
    }

    /// <summary>The same points in today's money: each divided by inflation since today.</summary>
    public static List<Point> InTodaysMoney(List<Point> points, decimal inflationPct)
    {
        var inflation = 1 + (double)inflationPct / 100;
        return points.Select((p, m) =>
        {
            var f = Math.Pow(inflation, m / 12.0);
            return p with
            {
                Investments = p.Investments / f, Savings = p.Savings / f, HomeEquity = p.HomeEquity / f,
                NetWorth = p.NetWorth / f, Liquid = p.Liquid / f, Withdrawal = p.Withdrawal / f,
            };
        }).ToList();
    }

    /// <summary>The point closest to an age, or null outside the projection.</summary>
    public static Point? AtAge(List<Point> points, double age) =>
        points.Count == 0 || age < points[0].Age - 1e-9 || age > points[^1].Age + 1.0 / 24
            ? null
            : points.MinBy(p => Math.Abs(p.Age - age));

    /// <summary>Age in years, with a fraction, on a date.</summary>
    public static double AgeOn(DateOnly birth, DateOnly date)
    {
        var years = date.Year - birth.Year;
        if (date < Birthday(birth, birth.Year + years)) years--;
        var last = Birthday(birth, birth.Year + years);
        var next = Birthday(birth, birth.Year + years + 1);
        return years + (double)(date.DayNumber - last.DayNumber) / (next.DayNumber - last.DayNumber);
    }

    // Like JavaScript's Date.UTC, 29 February rolls over to 1 March in other years.
    private static DateOnly Birthday(DateOnly birth, int year) =>
        birth is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year) ? new DateOnly(year, 3, 1) : new DateOnly(year, birth.Month, birth.Day);
}
