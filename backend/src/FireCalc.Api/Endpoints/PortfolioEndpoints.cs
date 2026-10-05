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
    public record QuoteDto(string Symbol, string Currency, decimal Price, DateOnly Date);
    public record UpdateInstrumentRequest(string? Symbol, string? Name);

    /// <summary>Amount is the total paid or received; UnitPrice is per share in the instrument's own currency.</summary>
    public record SetHoldingRequest(InstrumentRef? Instrument, decimal? Quantity, decimal? Amount, decimal? UnitPrice, DateOnly? Date, decimal? AveragePrice);
    public record SetHoldingResult(decimal Quantity, decimal Change, decimal Amount);

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

        // The combined value of all investment accounts per day, with the return excluding deposits.
        api.MapGet("/portfolio/history", async (DateOnly? from, DateOnly? to, ClaimsPrincipal principal, FireCalcDbContext db, PortfolioHistory history, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            return await history.BuildAsync(user.Id, user.Currency, from, to, ct);
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
            var instrument = await ResolveInstrumentAsync(req.Instrument, db, ct);
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
            var instrument = await ResolveInstrumentAsync(req.Instrument, db, ct);
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
        api.MapPost("/accounts/{accountId:guid}/import/{broker}", async (Guid accountId, string broker, bool? commit, HttpRequest request, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            broker = broker.ToLowerInvariant();
            if (broker is not ("nordnet" or "saxo")) return Results.NotFound();
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();
            if (account.Type != AccountType.Investment) return NotInvestment();

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length == 0) return Results.Problem("Choose a file to import.", statusCode: StatusCodes.Status400BadRequest);
            if (buffer.Length > MaxImportBytes) return Results.Problem("The file is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);

            // Saxo's export is an .xlsx; Nordnet's is a CSV.
            var parsed = broker == "saxo" ? SaxoXlsx.Parse(buffer.ToArray()) : NordnetCsv.Parse(buffer.ToArray(), user.Currency);
            if (parsed.Error is not null) return Results.Problem(parsed.Error, statusCode: StatusCodes.Status400BadRequest);

            var ids = parsed.Rows.Select(r => r.ExternalId).ToList();
            var existing = (await db.Transactions
                    .Where(t => t.AccountId == accountId && t.Source == broker && ids.Contains(t.ExternalId!))
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
                    instrument.Symbol ??= row.Symbol;
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
                    Source = broker,
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

        // The phone-friendly way to keep a portfolio current: say how many shares you now own. The change is
        // booked as a buy or sale, paid for by money moved in or out of the account, so cash stays put and
        // net deposits still separate your savings from market growth.
        api.MapPut("/accounts/{accountId:guid}/holdings", async (Guid accountId, SetHoldingRequest req, ClaimsPrincipal principal, FireCalcDbContext db, PortfolioValuation valuation, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id, ct);
            if (account is null) return Results.NotFound();
            if (account.Type != AccountType.Investment) return NotInvestment();

            var v = new Validation()
                .Check(req.Instrument is not null, "instrument", "Choose a share or fund.")
                .Check(req.Quantity is >= 0, "quantity", "Enter how many you own.")
                .Check(req.Amount is null or >= 0, "amount", "The amount cannot be negative.")
                .Check(req.UnitPrice is null or > 0, "unitPrice", "The price must be above 0.")
                .Check(req.AveragePrice is null or > 0, "averagePrice", "The price must be above 0.")
                .Check(req.Date is null || req.Date <= DateOnly.FromDateTime(DateTime.UtcNow), "date", "The purchase date cannot be in the future.");
            if (!v.IsValid) return v.Problem();
            var resolved = await ResolveInstrumentAsync(req.Instrument, db, ct);
            if (resolved is IResult error) return error;
            if (resolved is not Instrument instrument) return new Validation().Check(false, "instrument", "Choose a share or fund.").Problem();

            // A purchase date converts a foreign price at that day's exchange rate, so the gain in kroner
            // includes the currency's move since then. It may be earlier than the stored history.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var date = req.Date ?? today;
            if (date < today) instrument.PricesCheckedAt = null;
            await db.SaveChangesAsync(ct);

            var own = await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId).ToListAsync(ct);
            var current = PortfolioCalculator.Calculate(own, DateOnly.MaxValue).Holdings
                .FirstOrDefault(h => h.InstrumentId == instrument.Id)?.Quantity ?? 0;
            var change = req.Quantity!.Value - current;
            if (change == 0 && req.AveragePrice is null) return Results.Ok(new SetHoldingResult(current, 0, 0));
            // For the first shares the average price is what they were bought at.
            var unitPrice = req.UnitPrice ?? (current == 0 ? req.AveragePrice : null);

            // Without what was paid for the first shares there is no return to show, so ask for it.
            if (current == 0 && req.Amount is null && unitPrice is null)
                return new Validation().Check(false, "unitPrice", "Enter the average price you paid per share.").Problem();

            var amount = change == 0 ? 0 : req.Amount;
            if (amount is null)
            {
                var price = await valuation.UnitPriceAsync(instrument.Id, user.Currency, date, unitPrice, ct);
                // Without today's price a trade can still go through at the GAK given with it.
                if (price is null && req.AveragePrice is not null)
                    price = await valuation.UnitPriceAsync(instrument.Id, user.Currency, date, req.AveragePrice, ct);
                if (price is null)
                    return new Validation().Check(false, "unitPrice", "No price found for this share. Enter the price per share.").Problem();
                amount = Math.Round(Math.Abs(change) * price.Value, 2);
            }

            var buying = change > 0;
            var note = $"{current:0.####} → {req.Quantity:0.####}";
            if (change != 0) db.Transactions.AddRange(
                new PortfolioTransaction
                {
                    AccountId = accountId, Date = date, Note = note,
                    Type = buying ? TransactionType.Deposit : TransactionType.Withdrawal,
                    Amount = buying ? amount.Value : -amount.Value,
                    CreatedAt = DateTimeOffset.UtcNow.AddTicks(buying ? -1 : 1),
                },
                new PortfolioTransaction
                {
                    AccountId = accountId, Date = date, Note = note, InstrumentId = instrument.Id,
                    Type = buying ? TransactionType.Buy : TransactionType.Sell,
                    Quantity = Math.Abs(change),
                    Price = unitPrice,
                    Amount = buying ? -amount.Value : amount.Value,
                });
            await db.SaveChangesAsync(ct);

            if (req.AveragePrice is { } average && req.Quantity > 0)
            {
                var problem = await CorrectAveragePriceAsync(db, valuation, accountId, instrument, user.Currency, average, date, ct);
                if (problem is not null) return problem;
            }
            return Results.Ok(new SetHoldingResult(req.Quantity.Value, change, amount.Value));
        });

        api.MapGet("/instruments/search", async (string? q, PriceService prices, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q) ? [] : await prices.SearchAsync(q.Trim(), ct));

        api.MapGet("/instruments/quote", async (string? symbol, PriceService prices, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol)) return Results.NotFound();
            var quote = await prices.QuoteAsync(symbol.Trim(), ct);
            return quote is { } q ? Results.Ok(new QuoteDto(symbol.Trim().ToUpperInvariant(), q.Currency, q.Close.Close, q.Close.Date)) : Results.NotFound();
        });

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
            .Check(type is not TransactionType.CostCorrection, "type", "Correct the average price from the holding instead.")
            .Check(req.Note is null || req.Note.Trim().Length <= 500, "note", "Note must be at most 500 characters.");
    }

    /// <summary>Returns the instrument to use (or null for cash-only rows), or an error result.</summary>
    /// <summary>
    /// Corrects what the shares held cost (GAK, in the share's currency) by booking a cost correction. It moves
    /// no money and works the same for imported, transferred and typed-in shares. Purchases keep their own
    /// exchange rates: the new cost uses the blended rate of what was paid so far, or the rate on
    /// <paramref name="date"/> when nothing was paid (e.g. shares transferred in at 0 kr).
    /// </summary>
    private static async Task<IResult?> CorrectAveragePriceAsync(
        FireCalcDbContext db, PortfolioValuation valuation, Guid accountId, Instrument instrument, string currency,
        decimal average, DateOnly date, CancellationToken ct)
    {
        var all = await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync(ct);
        var holding = PortfolioCalculator.Calculate(all, DateOnly.MaxValue).Holdings.FirstOrDefault(h => h.InstrumentId == instrument.Id);
        if (holding is null || holding.Quantity <= 0) return null;

        var nativeCost = NativeCost(all.Where(t => t.InstrumentId == instrument.Id));
        decimal? rate = holding.CostBasis > 0 && nativeCost > 0 ? holding.CostBasis / nativeCost : null;
        rate ??= await valuation.UnitPriceAsync(instrument.Id, currency, date, 1m, ct);
        if (rate is null or 0)
            return new Validation().Check(false, "averagePrice", "No exchange rate found for this share. Try again later.").Problem();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Transactions.Add(new PortfolioTransaction
        {
            AccountId = accountId,
            InstrumentId = instrument.Id,
            Date = all.Where(t => t.InstrumentId == instrument.Id).Select(t => t.Date).DefaultIfEmpty(today).Max() is var last && last > today ? last : today,
            Type = TransactionType.CostCorrection,
            Quantity = holding.Quantity,
            Price = average,
            Amount = 0,
            CostChange = Math.Round(holding.Quantity * average * rate.Value - holding.CostBasis, 2),
            Note = $"GAK {average:0.####}{(instrument.Currency is { } c ? " " + c : "")}",
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// What the shares still held cost in their own currency, replayed with the average cost method like
    /// <see cref="PortfolioCalculator"/>. Zero when a purchase has no price per share.
    /// </summary>
    private static decimal NativeCost(IEnumerable<PortfolioTransaction> transactions)
    {
        var all = transactions.ToList();
        decimal quantity = 0, cost = 0;
        foreach (var t in all.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt))
        {
            switch (t.Type)
            {
                case TransactionType.Buy or TransactionType.SecurityIn:
                    if (t.Type == TransactionType.Buy && t.Price is null) return 0;
                    quantity += t.Quantity;
                    cost += t.Quantity * (t.Price ?? 0);
                    break;
                case TransactionType.Sell or TransactionType.SecurityOut:
                    cost -= quantity > 0 ? cost * Math.Min(1, t.Quantity / quantity) : 0;
                    quantity -= t.Quantity;
                    break;
                case TransactionType.CostCorrection when t.Price is { } avg:
                    // Like the stored change in kroner: measured against what was entered before it.
                    cost += t.Quantity * avg - NativeCost(all.Where(o => o.CreatedAt < t.CreatedAt));
                    break;
            }
        }
        return cost;
    }

    private static async Task<object?> ResolveInstrumentAsync(InstrumentRef? r, FireCalcDbContext db, CancellationToken ct)
    {
        if (r is null) return null;
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
