using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class AccountEndpoints
{
    /// <summary>
    /// <see cref="Tracked"/> accounts have transactions and are valued from them; the others carry the
    /// latest balance entered by hand. For a home, <see cref="Balance"/> is its value and <see cref="Loan"/> what is owed.
    /// </summary>
    public record AccountDto(Guid Id, string Name, AccountType Type, bool Archived, bool Tracked = false, decimal? Balance = null, DateOnly? BalanceDate = null, decimal? Loan = null);
    public record BalanceDto(DateOnly Date, decimal Balance, decimal? Loan = null);
    public record SaveBalanceRequest(DateOnly? Date, decimal? Balance, decimal? Loan = null);
    public record CreateAccountRequest(string? Name, AccountType? Type);
    public record UpdateAccountRequest(string? Name, AccountType? Type, bool Archived);

    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts");

        accounts.MapGet("/", async (bool? includeArchived, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            return await db.Accounts
                .Where(a => a.UserId == user.Id && (includeArchived == true || !a.Archived))
                .OrderBy(a => a.CreatedAt)
                .Select(a => new
                {
                    Account = a,
                    Tracked = db.Transactions.Any(t => t.AccountId == a.Id),
                    Latest = db.Balances.Where(x => x.AccountId == a.Id).OrderByDescending(x => x.Date).FirstOrDefault(),
                })
                .Select(x => new AccountDto(
                    x.Account.Id, x.Account.Name, x.Account.Type, x.Account.Archived, x.Tracked,
                    x.Latest == null ? null : x.Latest.Balance, x.Latest == null ? null : x.Latest.Date,
                    x.Latest == null ? null : x.Latest.Loan))
                .ToListAsync(ct);
        });

        accounts.MapPost("/", async (CreateAccountRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = Validate(req.Name, req.Type);
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = new Account { UserId = user.Id, Name = req.Name!.Trim(), Type = req.Type!.Value };
            db.Accounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/accounts/{account.Id}", ToDto(account));
        });

        accounts.MapPut("/{id:guid}", async (Guid id, UpdateAccountRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = Validate(req.Name, req.Type);
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();

            account.Name = req.Name!.Trim();
            account.Type = req.Type!.Value;
            account.Archived = req.Archived;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(account));
        });

        accounts.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();

            // Deleting would rewrite history; accounts with balances are archived instead.
            if (await db.Balances.AnyAsync(x => x.AccountId == id, ct) || await db.SnapshotEntries.AnyAsync(x => x.AccountId == id, ct))
                return Results.Problem("The account has balances. Archive it instead.", statusCode: StatusCodes.Status409Conflict);
            if (await db.Transactions.AnyAsync(t => t.AccountId == id, ct))
                return Results.Problem("The account has transactions. Archive it instead.", statusCode: StatusCodes.Status409Conflict);

            db.Accounts.Remove(account);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var balances = accounts.MapGroup("/{id:guid}/balances");

        balances.MapGet("/", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            if (!await db.Accounts.AnyAsync(a => a.Id == id && a.UserId == user.Id, ct)) return Results.NotFound();
            var list = await db.Balances.AsNoTracking()
                .Where(x => x.AccountId == id)
                .OrderByDescending(x => x.Date)
                .Select(x => new BalanceDto(x.Date, x.Balance, x.Loan))
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        // One balance per date: saving the same date again replaces it.
        balances.MapPut("/", async (Guid id, SaveBalanceRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var v = new Validation()
                .Check(req.Date is not null, "date", "Date is required.")
                .Check(req.Date is null || req.Date <= today, "date", "The date can't be in the future.")
                .Check(req.Balance is not null, "balance", "Balance is required.")
                .Check(req.Balance is null || req.Balance >= 0, "balance", "Balance cannot be negative.")
                .Check(req.Loan is null || req.Loan >= 0, "loan", "Loan cannot be negative.");
            if (!v.IsValid) return v.Problem();

            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();
            if (await db.Transactions.AnyAsync(t => t.AccountId == id, ct))
                return Results.Problem("This account is valued from its transactions.", statusCode: StatusCodes.Status409Conflict);

            var balance = await db.Balances.SingleOrDefaultAsync(x => x.AccountId == id && x.Date == req.Date, ct);
            if (balance is null) db.Balances.Add(balance = new AccountBalance { AccountId = id, Date = req.Date!.Value });
            balance.Balance = Math.Round(req.Balance!.Value, 2);
            // Only a home has a loan against it; a home without one owes nothing.
            balance.Loan = account.Type == AccountType.Property ? Math.Round(req.Loan ?? 0, 2) : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new BalanceDto(balance.Date, balance.Balance, balance.Loan));
        });

        balances.MapDelete("/{date}", async (Guid id, DateOnly date, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            if (!await db.Accounts.AnyAsync(a => a.Id == id && a.UserId == user.Id, ct)) return Results.NotFound();
            var deleted = await db.Balances.Where(x => x.AccountId == id && x.Date == date).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static AccountDto ToDto(Account a) => new(a.Id, a.Name, a.Type, a.Archived);

    private static Validation Validate(string? name, AccountType? type) => new Validation()
        .Check(!string.IsNullOrWhiteSpace(name), "name", "Name is required.")
        .Check(name is null || name.Trim().Length <= 100, "name", "Name must be at most 100 characters.")
        .Check(type is not null, "type", "Type is required.");
}
