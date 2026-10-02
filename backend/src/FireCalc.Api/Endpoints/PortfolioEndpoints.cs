using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using FireCalc.Api.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

public static class PortfolioEndpoints
{
    public record PortfolioDto(string Currency, DateOnly AsOf, decimal Value, decimal DayChange, List<AccountPortfolio> Accounts);
    public record AccountValueDto(Guid AccountId, decimal Value);

    public record TransactionDto(
        Guid Id, Guid AccountId, DateOnly Date, TransactionType Type,
        Guid? InstrumentId, string? InstrumentName, string? Isin, string? Symbol,
        decimal Quantity, decimal? Price, decimal Amount, string? Note, string Source);

    public record InstrumentRef(Guid? Id, string? Isin, string? Symbol, string? Name);
    public record SaveTransactionRequest(DateOnly? Date, TransactionType? Type, InstrumentRef? Instrument, decimal? Quantity, decimal? Price, decimal? Amount, string? Note);

    public record InstrumentDto(Guid Id, string Name, string? Isin, string? Symbol, string? Currency);
    public record UpdateInstrumentRequest(string? Symbol, string? Name);

    public record ImportPreviewRow(int Line, DateOnly Date, TransactionType Type, string RawType, string? Name, decimal Quantity, decimal Amount);
    public record ImportResultDto(
        bool Committed, int Rows, int New, int Duplicates,
        List<ImportIssue> Skipped, List<string> OtherTypes, List<ImportPreviewRow> Preview);

    private const int MaxImportBytes = 10 * 1024 * 1024;

    public static void MapPortfolioEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/portfolio", async (DateOnly? date, ClaimsPrincipal principal, FireCalcDbContext db, PortfolioValuation valuation, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var asOf = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var accounts = await valuation.ValueAsync(user.Id, user.Currency, asOf, refresh: true, ct);
            return new PortfolioDto(user.Currency, asOf, accounts.Sum(a => a.Value), accounts.Sum(a => a.DayChange), accounts);
        });

        // Account values on a date, used to pre-fill a snapshot.
        api.MapGet("/portfolio/values", async (DateOnly? date, ClaimsPrincipal principal, FireCalcDbContext db, PortfolioValuation valuation, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var asOf = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var accounts = await valuation.ValueAsync(user.Id, user.Currency, asOf, refresh: true, ct);
            return accounts.Select(a => new AccountValueDto(a.AccountId, a.Value)).ToList();
        });

        var transactions = api.MapGroup("/accounts/{accountId:guid}/transactions");

        transactions.MapGet("/", async (Guid accountId, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.UserId == user.Id, ct)) return Results.NotFound();
            var list = await db.Transactions.AsNoTracking().Include(t => t.Instrument)
                .Where(t => t.AccountId == accountId)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(list.Select(ToDto).ToList());
        });

        transactions.MapPost("/", async (Guid accountId, SaveTransactionRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();
            if (account.Type != AccountType.Investment) return NotInvestment();

            var v = Validate(req);
            if (!v.IsValid) return v.Problem();
            var instrument = await ResolveInstrumentAsync(req, db, ct);
            if (instrument is IResult error) return error;

            var tx = new PortfolioTransaction { AccountId = accountId };
            Apply(tx, req, instrument as Instrument);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync(ct);
            await db.Entry(tx).Reference(t => t.Instrument).LoadAsync(ct);
            return Results.Created($"/api/transactions/{tx.Id}", ToDto(tx));
        });

        api.MapPut("/transactions/{id:guid}", async (Guid id, SaveTransactionRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var tx = await OwnedTransaction(db, user.Id).SingleOrDefaultAsync(t => t.Id == id, ct);
            if (tx is null) return Results.NotFound();

            var v = Validate(req);
            if (!v.IsValid) return v.Problem();
            var instrument = await ResolveInstrumentAsync(req, db, ct);
            if (instrument is IResult error) return error;

            Apply(tx, req, instrument as Instrument);
            await db.SaveChangesAsync(ct);
            await db.Entry(tx).Reference(t => t.Instrument).LoadAsync(ct);
            return Results.Ok(ToDto(tx));
        });

        api.MapDelete("/transactions/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var deleted = await OwnedTransaction(db, user.Id).Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        // The browser sends the file as the raw request body. Without ?commit=true nothing is saved,
        // and the response is a preview of what would be imported.
        api.MapPost("/accounts/{accountId:guid}/import/nordnet", async (Guid accountId, bool? commit, HttpRequest request, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();
            if (account.Type != AccountType.Investment) return NotInvestment();

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length == 0) return Results.Problem("Choose a file to import.", statusCode: StatusCodes.Status400BadRequest);
            if (buffer.Length > MaxImportBytes) return Results.Problem("The file is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);

            var parsed = NordnetCsv.Parse(buffer.ToArray(), user.Currency);
            if (parsed.Error is not null) return Results.Problem(parsed.Error, statusCode: StatusCodes.Status400BadRequest);

            var ids = parsed.Rows.Select(r => r.ExternalId).ToList();
            var existing = (await db.Transactions
                    .Where(t => t.AccountId == accountId && t.Source == "nordnet" && ids.Contains(t.ExternalId!))
                    .Select(t => t.ExternalId!)
                    .ToListAsync(ct))
                .ToHashSet();
            var fresh = parsed.Rows.Where(r => !existing.Contains(r.ExternalId)).DistinctBy(r => r.ExternalId).ToList();

            if (commit == true && fresh.Count > 0)
            {
                var isins = fresh.Select(r => r.Isin).OfType<string>().Distinct().ToList();
                var instruments = await db.Instruments.Where(i => isins.Contains(i.Isin!)).ToDictionaryAsync(i => i.Isin!, ct);
                foreach (var row in fresh.Where(r => r.Isin is not null))
                {
                    if (!instruments.TryGetValue(row.Isin!, out var instrument))
                    {
                        instrument = new Instrument { Isin = row.Isin, Name = Truncate(row.Name ?? row.Isin!, 200) };
                        instruments[row.Isin!] = instrument;
                        db.Instruments.Add(instrument);
                    }
                    // Older transactions may need price history that isn't stored yet.
                    instrument.PricesCheckedAt = null;
                }

                db.Transactions.AddRange(fresh.Select(r => new PortfolioTransaction
                {
                    AccountId = accountId,
                    Instrument = r.Isin is null ? null : instruments[r.Isin],
                    Date = r.Date,
                    Type = r.Type,
                    Quantity = r.Quantity,
                    Price = r.Price,
                    Amount = r.Amount,
                    Note = r.Type == TransactionType.Other ? Truncate(r.RawType, 500) : null,
                    Source = "nordnet",
                    ExternalId = Truncate(r.ExternalId, 64),
                    // Keeps the file's order for rows on the same day (Nordnet lists newest first).
                    CreatedAt = DateTimeOffset.UtcNow.AddTicks(-r.Line),
                }));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new ImportResultDto(
                commit == true,
                parsed.Rows.Count,
                fresh.Count,
                parsed.Rows.Count - fresh.Count,
                parsed.Skipped,
                fresh.Where(r => r.Type == TransactionType.Other).Select(r => r.RawType).Distinct().ToList(),
                fresh.OrderByDescending(r => r.Date).Take(20)
                    .Select(r => new ImportPreviewRow(r.Line, r.Date, r.Type, r.RawType, r.Name, r.Quantity, r.Amount))
                    .ToList()));
        });

        api.MapGet("/instruments/search", async (string? q, PriceService prices, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q) ? [] : await prices.SearchAsync(q.Trim(), ct));

        // Lets the user fix which price symbol an instrument uses when the automatic match is wrong.
        api.MapPut("/instruments/{id:guid}", async (Guid id, UpdateInstrumentRequest req, ClaimsPrincipal principal, FireCalcDbContext db, PriceService prices, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var used = await db.Transactions.AnyAsync(t => t.InstrumentId == id && db.Accounts.Any(a => a.Id == t.AccountId && a.UserId == user.Id), ct);
            var instrument = used ? await db.Instruments.FindAsync([id], ct) : null;
            if (instrument is null) return Results.NotFound();

            var symbol = string.IsNullOrWhiteSpace(req.Symbol) ? null : req.Symbol.Trim().ToUpperInvariant();
            var v = new Validation()
                .Check(symbol is null || symbol.Length <= 32, "symbol", "Symbol must be at most 32 characters.")
                .Check(req.Name is null || req.Name.Trim().Length is > 0 and <= 200, "name", "Name must be 1 to 200 characters.");
            if (!v.IsValid) return v.Problem();

            if (symbol != instrument.Symbol)
            {
                await prices.ResetPricesAsync(instrument, ct);
                instrument.Symbol = symbol;
            }
            if (req.Name is not null) instrument.Name = req.Name.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new InstrumentDto(instrument.Id, instrument.Name, instrument.Isin, instrument.Symbol, instrument.Currency));
        });
    }

    private static IQueryable<PortfolioTransaction> OwnedTransaction(FireCalcDbContext db, Guid userId) =>
        db.Transactions.Where(t => db.Accounts.Any(a => a.Id == t.AccountId && a.UserId == userId));

    private static IResult NotInvestment() =>
        Results.Problem("Transactions can only be added to investment accounts.", statusCode: StatusCodes.Status400BadRequest);

    private static bool NeedsInstrument(TransactionType t) =>
        t is TransactionType.Buy or TransactionType.Sell or TransactionType.SecurityIn or TransactionType.SecurityOut;

    private static Validation Validate(SaveTransactionRequest req)
    {
        var type = req.Type;
        var amount = req.Amount ?? 0;
        var hasInstrument = req.Instrument is { } i && (i.Id is not null || !string.IsNullOrWhiteSpace(i.Isin) || !string.IsNullOrWhiteSpace(i.Symbol));
        return new Validation()
            .Check(req.Date is not null, "date", "Date is required.")
            .Check(type is not null, "type", "Type is required.")
            .Check(req.Amount is not null, "amount", "Amount is required.")
            .Check(req.Quantity is null or >= 0, "quantity", "Quantity cannot be negative.")
            .Check(type is null || !NeedsInstrument(type.Value) || hasInstrument, "instrument", "Choose a share or fund.")
            .Check(type is null || !NeedsInstrument(type.Value) || req.Quantity > 0, "quantity", "Quantity is required.")
            .Check(type is not (TransactionType.Buy or TransactionType.Withdrawal or TransactionType.Fee) || amount <= 0, "amount", "This type takes money out, so the amount must be negative.")
            .Check(type is not (TransactionType.Sell or TransactionType.Deposit) || amount >= 0, "amount", "This type brings money in, so the amount must be positive.")
            .Check(req.Note is null || req.Note.Trim().Length <= 500, "note", "Note must be at most 500 characters.");
    }

    /// <summary>Returns the instrument to use (or null for cash-only rows), or an error result.</summary>
    private static async Task<object?> ResolveInstrumentAsync(SaveTransactionRequest req, FireCalcDbContext db, CancellationToken ct)
    {
        if (req.Instrument is not { } r) return null;
        if (r.Id is { } id)
            return await db.Instruments.FindAsync([id], ct) ?? (object)Results.ValidationProblem(new Dictionary<string, string[]> { ["instrument"] = ["Unknown instrument."] });

        var isin = string.IsNullOrWhiteSpace(r.Isin) ? null : r.Isin.Trim().ToUpperInvariant();
        var symbol = string.IsNullOrWhiteSpace(r.Symbol) ? null : r.Symbol.Trim().ToUpperInvariant();
        if (isin is null && symbol is null) return null;
        if (isin is { Length: not 12 } || symbol is { Length: > 32 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["instrument"] = ["Invalid ISIN or symbol."] });

        var instrument = isin is not null
            ? await db.Instruments.FirstOrDefaultAsync(i => i.Isin == isin, ct)
            : await db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol, ct);
        if (instrument is null)
        {
            instrument = new Instrument { Isin = isin, Symbol = symbol, Name = Truncate(r.Name?.Trim() is { Length: > 0 } n ? n : (symbol ?? isin)!, 200) };
            db.Instruments.Add(instrument);
        }
        instrument.Symbol ??= symbol;
        return instrument;
    }

    private static void Apply(PortfolioTransaction tx, SaveTransactionRequest req, Instrument? instrument)
    {
        tx.Date = req.Date!.Value;
        tx.Type = req.Type!.Value;
        tx.Instrument = instrument;
        tx.InstrumentId = instrument?.Id;
        tx.Quantity = req.Quantity ?? 0;
        tx.Price = req.Price;
        tx.Amount = req.Amount!.Value;
        tx.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        // The date may be earlier than the stored price history.
        if (instrument is not null) instrument.PricesCheckedAt = null;
    }

    private static TransactionDto ToDto(PortfolioTransaction t) => new(
        t.Id, t.AccountId, t.Date, t.Type, t.InstrumentId, t.Instrument?.Name, t.Instrument?.Isin, t.Instrument?.Symbol,
        t.Quantity, t.Price, t.Amount, t.Note, t.Source);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
