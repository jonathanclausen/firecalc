namespace FireCalc.Api.Data;

public enum AccountType
{
    Investment,
    Savings,
    Cash,
    /// <summary>
    /// Not an account any more: homes live in <see cref="Home"/>. Kept as the kind their equity has in the
    /// overview and its history.
    /// </summary>
    Property,
    /// <summary>Money owed, other than loans on a home. Its balance is the amount owed and counts against net worth.</summary>
    Loan,
}

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string AuthSubject { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string Currency { get; set; } = "DKK";
    /// <summary>Used to place scenario events and the FIRE age on a timeline.</summary>
    public DateOnly? BirthDate { get; set; }
    /// <summary>When the welcome guide was finished or skipped; until then it opens on sign-in.</summary>
    public DateTimeOffset? OnboardedAt { get; set; }
    /// <summary>When the "getting started" checklist on the overview was hidden.</summary>
    public DateTimeOffset? ChecklistHiddenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public bool Archived { get; set; }
    /// <summary>Example data from "Prøv med eksempeldata"; removed together in one go.</summary>
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The balance of an account on a date, entered by hand for accounts without transactions (savings, cash).
/// It counts until a newer one is entered.
/// </summary>
public class AccountBalance
{
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>A home, with what it is worth over time and the loans on it. It counts with its equity.</summary>
public class Home
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    /// <summary>A sold home stops counting.</summary>
    public bool Archived { get; set; }
    /// <summary>Example data from "Prøv med eksempeldata"; removed with its values and loans.</summary>
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>What a home was worth on a date, entered by hand. It counts until a newer one.</summary>
public class HomeValuation
{
    public Guid HomeId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// A loan on a home (realkreditlån, banklån). With its terms filled in, what is owed is worked out month by
/// month from the latest statement as an annuity; without them it stays at the statement.
/// </summary>
public class Mortgage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid HomeId { get; set; }
    public required string Name { get; set; }
    /// <summary>Yearly interest rate in percent.</summary>
    public decimal? InterestPct { get; set; }
    /// <summary>Yearly bidragssats in percent of what is owed. A cost; it doesn't pay the loan down.</summary>
    public decimal? ContributionPct { get; set; }
    /// <summary>When the loan is paid off (udløb).</summary>
    public DateOnly? EndDate { get; set; }
    /// <summary>Only interest is paid until this date (afdragsfrihed).</summary>
    public DateOnly? InterestOnlyUntil { get; set; }
    /// <summary>A loan paid off or replaced (omlagt) stops counting.</summary>
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>What was owed on a loan on a date (restgæld), from a statement.</summary>
public class MortgageBalance
{
    public Guid MortgageId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>
/// One check-in: the balance of each account on a given date. Replaced by <see cref="AccountBalance"/>; the
/// table is kept, unused, until the copied balances have been checked.
/// </summary>
public class Snapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public string? Note { get; set; }
    public List<SnapshotEntry> Entries { get; set; } = [];
}

public class SnapshotEntry
{
    public Guid SnapshotId { get; set; }
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public decimal Balance { get; set; }
}

/// <summary>A fixed FIRE number to work towards. One per user for now.</summary>
public class Goal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Name { get; set; } = "FIRE";
    public decimal TargetAmount { get; set; }
    public DateOnly? TargetDate { get; set; }
    public decimal? ExpectedAnnualReturnPct { get; set; }
    /// <summary>Set by the example data; saving the goal makes it the user's own.</summary>
    public bool IsDemo { get; set; }
}

/// <summary>
/// A saved "what if" for the future: assumptions plus life events placed by age. The projection itself is
/// calculated in the browser from today's net worth.
/// </summary>
public class Scenario
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public decimal MonthlySavings { get; set; }
    public decimal InvestmentReturnPct { get; set; }
    /// <summary>Return on savings and cash accounts.</summary>
    public decimal SavingsReturnPct { get; set; }
    public decimal HomeGrowthPct { get; set; }
    public decimal InflationPct { get; set; }
    /// <summary>Age when saving stops and <see cref="WithdrawalPct"/> is taken out instead.</summary>
    public decimal FireAge { get; set; }
    /// <summary>Yearly spending in today's money during a break.</summary>
    public decimal YearlySpending { get; set; }
    /// <summary>After FIRE, this percentage of investments and savings is taken out each year.</summary>
    public decimal WithdrawalPct { get; set; } = 4;
    /// <summary>The events as JSON (a list of <see cref="ScenarioEvent"/>).</summary>
    public string Events { get; set; } = "[]";
    /// <summary>Example data from "Prøv med eksempeldata".</summary>
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ScenarioEventKind
{
    /// <summary>Time off: no saving, and spending is taken out, from <see cref="ScenarioEvent.Age"/> for <see cref="ScenarioEvent.Years"/>.</summary>
    Break,
    /// <summary>Monthly savings change to <see cref="ScenarioEvent.Amount"/> from the age on.</summary>
    Savings,
    /// <summary>A one-off amount in or out (negative) at the age.</summary>
    LumpSum,
}

public record ScenarioEvent(ScenarioEventKind Kind, decimal Age, decimal? Years, decimal? Amount);

public enum TransactionType
{
    Buy,
    Sell,
    Dividend,
    Deposit,
    Withdrawal,
    Fee,
    Tax,
    Interest,
    /// <summary>Shares added without cash, e.g. a split or a transfer in from another broker.</summary>
    SecurityIn,
    /// <summary>Shares removed without cash, e.g. the old line in a split or a transfer out.</summary>
    SecurityOut,
    Other,
    /// <summary>Changes what the shares held cost (a corrected GAK), without moving any money.</summary>
    CostCorrection,
}

/// <summary>
/// A share, ETF or fund. Shared market data, not owned by a user: the same ISIN is one row.
/// </summary>
public class Instrument
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string? Isin { get; set; }
    /// <summary>Yahoo Finance symbol, e.g. NOVO-B.CO. Null until found or set by hand.</summary>
    public string? Symbol { get; set; }
    public required string Name { get; set; }
    /// <summary>Currency the price is quoted in, as reported by the price source.</summary>
    public string? Currency { get; set; }
    /// <summary>When prices were last fetched (successfully or not), so we don't ask too often.</summary>
    public DateTimeOffset? PricesCheckedAt { get; set; }
}

/// <summary>Daily closing price in the instrument's currency.</summary>
public class InstrumentPrice
{
    public Guid InstrumentId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
}

/// <summary>How many units of the user's currency one unit of <see cref="Currency"/> cost on a date.</summary>
public class FxRate
{
    public required string Currency { get; set; }
    public required string QuoteCurrency { get; set; }
    public DateOnly Date { get; set; }
    public decimal Rate { get; set; }
}

/// <summary>
/// One movement in an investment account. Amount is the signed cash effect in the user's currency
/// (negative for a buy including fees, positive for a sale or dividend), which makes the account's
/// cash the plain sum of amounts and a position's cost the money actually paid.
/// </summary>
public class PortfolioTransaction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public Guid? InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }
    public DateOnly Date { get; set; }
    public TransactionType Type { get; set; }
    /// <summary>Number of shares, always positive; the type says which way.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Price per share in the instrument's currency, as the broker reported it.</summary>
    public decimal? Price { get; set; }
    public decimal Amount { get; set; }
    /// <summary>
    /// For a cost correction: how much it adds to what the shares held cost, in the user's currency. A change
    /// rather than a total, so trades entered later with an earlier date still count.
    /// </summary>
    public decimal? CostChange { get; set; }
    public string? Note { get; set; }
    /// <summary>"manual" or the importer's name, e.g. "nordnet".</summary>
    public string Source { get; set; } = "manual";
    /// <summary>The broker's own id for the row, used to skip rows already imported.</summary>
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
