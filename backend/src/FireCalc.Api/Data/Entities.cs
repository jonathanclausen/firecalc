namespace FireCalc.Api.Data;

public enum AccountType
{
    Investment,
    Savings,
    Cash,
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

/// <summary>One check-in: the balance of each account on a given date.</summary>
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
