using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class AccountEndpoints
{
    public record AccountDto(Guid Id, string Name, AccountType Type, bool Archived);
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
                .Select(a => new AccountDto(a.Id, a.Name, a.Type, a.Archived))
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
            if (await db.SnapshotEntries.AnyAsync(x => x.AccountId == id, ct))
                return Results.Problem("The account has balances in snapshots. Archive it instead.", statusCode: StatusCodes.Status409Conflict);
            if (await db.Transactions.AnyAsync(t => t.AccountId == id, ct))
                return Results.Problem("The account has transactions. Archive it instead.", statusCode: StatusCodes.Status409Conflict);

            db.Accounts.Remove(account);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static AccountDto ToDto(Account a) => new(a.Id, a.Name, a.Type, a.Archived);

    private static Validation Validate(string? name, AccountType? type) => new Validation()
        .Check(!string.IsNullOrWhiteSpace(name), "name", "Name is required.")
        .Check(name is null || name.Trim().Length <= 100, "name", "Name must be at most 100 characters.")
        .Check(type is not null, "type", "Type is required.");
}
