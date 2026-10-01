using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class SnapshotEndpoints
{
    public record SnapshotEntryDto(Guid AccountId, string AccountName, AccountType AccountType, decimal Balance);
    public record SnapshotDto(Guid Id, DateOnly Date, string? Note, decimal Total, List<SnapshotEntryDto> Entries);
    public record SnapshotEntryRequest(Guid AccountId, decimal Balance);
    public record SaveSnapshotRequest(DateOnly? Date, string? Note, List<SnapshotEntryRequest>? Entries);

    public static void MapSnapshotEndpoints(this RouteGroupBuilder api)
    {
        var snapshots = api.MapGroup("/snapshots");

        snapshots.MapGet("/", async (ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var list = await Query(db, user.Id).OrderByDescending(s => s.Date).ToListAsync(ct);
            return list.Select(ToDto).ToList();
        });

        snapshots.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var snapshot = await Query(db, user.Id).SingleOrDefaultAsync(s => s.Id == id, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(ToDto(snapshot));
        });

        snapshots.MapPost("/", async (SaveSnapshotRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var error = await ValidateAsync(req, user.Id, null, db, ct);
            if (error is not null) return error;

            var snapshot = new Snapshot { UserId = user.Id };
            Apply(snapshot, req);
            db.Snapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);

            var saved = await Query(db, user.Id).SingleAsync(s => s.Id == snapshot.Id, ct);
            return Results.Created($"/api/snapshots/{snapshot.Id}", ToDto(saved));
        });

        snapshots.MapPut("/{id:guid}", async (Guid id, SaveSnapshotRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var snapshot = await db.Snapshots.Include(s => s.Entries).SingleOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, ct);
            if (snapshot is null) return Results.NotFound();

            var error = await ValidateAsync(req, user.Id, id, db, ct);
            if (error is not null) return error;

            Apply(snapshot, req);
            await db.SaveChangesAsync(ct);

            db.ChangeTracker.Clear();
            var saved = await Query(db, user.Id).SingleAsync(s => s.Id == id, ct);
            return Results.Ok(ToDto(saved));
        });

        snapshots.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var deleted = await db.Snapshots.Where(s => s.Id == id && s.UserId == user.Id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    internal static IQueryable<Snapshot> Query(FireCalcDbContext db, Guid userId) =>
        db.Snapshots.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Include(s => s.Entries).ThenInclude(x => x.Account);

    private static SnapshotDto ToDto(Snapshot s) => new(
        s.Id,
        s.Date,
        s.Note,
        s.Entries.Sum(x => x.Balance),
        s.Entries
            .OrderBy(x => x.Account.CreatedAt)
            .Select(x => new SnapshotEntryDto(x.AccountId, x.Account.Name, x.Account.Type, x.Balance))
            .ToList());

    private static void Apply(Snapshot snapshot, SaveSnapshotRequest req)
    {
        snapshot.Date = req.Date!.Value;
        snapshot.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        var wanted = req.Entries!.ToDictionary(e => e.AccountId, e => e.Balance);
        snapshot.Entries.RemoveAll(x => !wanted.ContainsKey(x.AccountId));
        foreach (var (accountId, balance) in wanted)
        {
            var entry = snapshot.Entries.Find(x => x.AccountId == accountId);
            if (entry is null)
                snapshot.Entries.Add(new SnapshotEntry { AccountId = accountId, Balance = balance });
            else
                entry.Balance = balance;
        }
    }

    private static async Task<IResult?> ValidateAsync(SaveSnapshotRequest req, Guid userId, Guid? existingId, FireCalcDbContext db, CancellationToken ct)
    {
        var entries = req.Entries ?? [];
        var accountIds = entries.Select(e => e.AccountId).Distinct().ToList();
        var ownedCount = await db.Accounts.CountAsync(a => a.UserId == userId && accountIds.Contains(a.Id), ct);

        var v = new Validation()
            .Check(req.Date is not null, "date", "Date is required.")
            .Check(req.Note is null || req.Note.Trim().Length <= 500, "note", "Note must be at most 500 characters.")
            .Check(entries.Count > 0, "entries", "Enter at least one balance.")
            .Check(accountIds.Count == entries.Count, "entries", "Each account can only appear once.")
            .Check(ownedCount == accountIds.Count, "entries", "Unknown account.")
            .Check(entries.All(e => e.Balance >= 0), "entries", "Balances cannot be negative.");
        if (!v.IsValid) return v.Problem();

        var dateTaken = await db.Snapshots.AnyAsync(s => s.UserId == userId && s.Date == req.Date && s.Id != existingId, ct);
        return dateTaken
            ? Results.Problem("There is already a snapshot for this date. Edit that one instead.", statusCode: StatusCodes.Status409Conflict)
            : null;
    }
}
