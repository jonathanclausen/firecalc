namespace FireCalc.Api.Data;

public enum AccountType
{
    Investment,
    Savings,
    Cash,
    /// <summary>A home: each balance is the home's value with <see cref="AccountBalance.Loan"/> owed on it.</summary>
    Property,
}

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string GoogleSubject { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public string Currency { get; set; } = "DKK";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public bool Archived { get; set; }
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
    /// <summary>For a <see cref="AccountType.Property"/> account, what the home is worth.</summary>
    public decimal Balance { get; set; }
    /// <summary>What is owed on a home (restgæld); null for other accounts.</summary>
    public decimal? Loan { get; set; }
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
}

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
