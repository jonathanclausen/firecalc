using System.Security.Claims;
using FireCalc.Api.Auth;
using FireCalc.Api.Data;
using FireCalc.Api.Homes;
using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Endpoints;

/// <summary>Homes (Bolig): what each is worth over time and the loans on it.</summary>
public static class HomeEndpoints
{
    /// <summary>A home today: its latest value, what is owed on its loans now, and the difference.</summary>
    public record HomeDto(Guid Id, string Name, bool Archived, decimal? Value, DateOnly? ValueDate, decimal Owed, decimal Equity, List<MortgageDto> Loans);

    /// <summary>
    /// A loan with what is owed today (worked out from <see cref="StatementDate"/>) and this month's payment.
    /// </summary>
    public record MortgageDto(
        Guid Id, string Name, decimal? InterestPct, decimal? ContributionPct, DateOnly? EndDate, DateOnly? InterestOnlyUntil, bool Archived,
        decimal? Owed, decimal? Statement, DateOnly? StatementDate, MortgageMath.Month? Payment);

    public record SaveHomeRequest(string? Name, bool Archived = false);
    public record ValueDto(DateOnly Date, decimal Value);
    public record SaveValueRequest(DateOnly? Date, decimal? Value);
    public record SaveMortgageRequest(string? Name, decimal? InterestPct, decimal? ContributionPct, DateOnly? EndDate, DateOnly? InterestOnlyUntil, bool Archived = false);
    public record StatementDto(DateOnly Date, decimal Balance);
    public record SaveStatementRequest(DateOnly? Date, decimal? Balance);

    public static void MapHomeEndpoints(this RouteGroupBuilder api)
    {
        var homes = api.MapGroup("/homes");

        homes.MapGet("/", async (bool? includeArchived, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var list = await db.Homes.AsNoTracking()
                .Where(h => h.UserId == user.Id && (includeArchived == true || !h.Archived))
                .OrderBy(h => h.CreatedAt)
                .ToListAsync(ct);
            var data = await HomeData.LoadAsync(db, list.Select(h => h.Id).ToList(), ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return list.Select(h => ToDto(h, data, today)).ToList();
        });

        homes.MapPost("/", async (SaveHomeRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateName(req.Name);
            if (!v.IsValid) return v.Problem();
            var user = await db.GetOrCreateUserAsync(principal, ct);
            var home = new Home { UserId = user.Id, Name = req.Name!.Trim() };
            db.Homes.Add(home);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/homes/{home.Id}", ToDto(home, HomeData.Empty, DateOnly.FromDateTime(DateTime.UtcNow)));
        });

        homes.MapPut("/{id:guid}", async (Guid id, SaveHomeRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateName(req.Name);
            if (!v.IsValid) return v.Problem();
            var home = await FindHome(db, principal, id, ct);
            if (home is null) return Results.NotFound();
            home.Name = req.Name!.Trim();
            home.Archived = req.Archived;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // Removes the home with its values and loans.
        homes.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var home = await FindHome(db, principal, id, ct);
            if (home is null) return Results.NotFound();
            db.Homes.Remove(home);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var values = homes.MapGroup("/{id:guid}/values");

        values.MapGet("/", async (Guid id, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            if (await FindHome(db, principal, id, ct) is null) return Results.NotFound();
            return Results.Ok(await db.HomeValuations.AsNoTracking()
                .Where(x => x.HomeId == id)
                .OrderByDescending(x => x.Date)
                .Select(x => new ValueDto(x.Date, x.Value))
                .ToListAsync(ct));
        });

        // One value per date: saving the same date again replaces it.
        values.MapPut("/", async (Guid id, SaveValueRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateDated(req.Date, req.Value, "value");
            if (!v.IsValid) return v.Problem();
            if (await FindHome(db, principal, id, ct) is null) return Results.NotFound();
            var value = await db.HomeValuations.SingleOrDefaultAsync(x => x.HomeId == id && x.Date == req.Date, ct);
            if (value is null) db.HomeValuations.Add(value = new HomeValuation { HomeId = id, Date = req.Date!.Value });
            value.Value = Math.Round(req.Value!.Value, 2);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ValueDto(value.Date, value.Value));
        });

        values.MapDelete("/{date}", async (Guid id, DateOnly date, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            if (await FindHome(db, principal, id, ct) is null) return Results.NotFound();
            var deleted = await db.HomeValuations.Where(x => x.HomeId == id && x.Date == date).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        var loans = homes.MapGroup("/{id:guid}/loans");

        loans.MapPost("/", async (Guid id, SaveMortgageRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateMortgage(req);
            if (!v.IsValid) return v.Problem();
            if (await FindHome(db, principal, id, ct) is null) return Results.NotFound();
            var loan = new Mortgage { HomeId = id, Name = req.Name!.Trim() };
            Apply(loan, req);
            db.Mortgages.Add(loan);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/homes/{id}/loans/{loan.Id}", new { loan.Id });
        });

        loans.MapPut("/{loanId:guid}", async (Guid id, Guid loanId, SaveMortgageRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateMortgage(req);
            if (!v.IsValid) return v.Problem();
            var loan = await FindLoan(db, principal, id, loanId, ct);
            if (loan is null) return Results.NotFound();
            Apply(loan, req);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        loans.MapDelete("/{loanId:guid}", async (Guid id, Guid loanId, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var loan = await FindLoan(db, principal, id, loanId, ct);
            if (loan is null) return Results.NotFound();
            db.Mortgages.Remove(loan);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var statements = loans.MapGroup("/{loanId:guid}/balances");

        statements.MapGet("/", async (Guid id, Guid loanId, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            if (await FindLoan(db, principal, id, loanId, ct) is null) return Results.NotFound();
            return Results.Ok(await db.MortgageBalances.AsNoTracking()
                .Where(x => x.MortgageId == loanId)
                .OrderByDescending(x => x.Date)
                .Select(x => new StatementDto(x.Date, x.Balance))
                .ToListAsync(ct));
        });

        statements.MapPut("/", async (Guid id, Guid loanId, SaveStatementRequest req, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            var v = ValidateDated(req.Date, req.Balance, "balance");
            if (!v.IsValid) return v.Problem();
            if (await FindLoan(db, principal, id, loanId, ct) is null) return Results.NotFound();
            var balance = await db.MortgageBalances.SingleOrDefaultAsync(x => x.MortgageId == loanId && x.Date == req.Date, ct);
            if (balance is null) db.MortgageBalances.Add(balance = new MortgageBalance { MortgageId = loanId, Date = req.Date!.Value });
            balance.Balance = Math.Round(req.Balance!.Value, 2);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new StatementDto(balance.Date, balance.Balance));
        });

        statements.MapDelete("/{date}", async (Guid id, Guid loanId, DateOnly date, ClaimsPrincipal principal, FireCalcDbContext db, CancellationToken ct) =>
        {
            if (await FindLoan(db, principal, id, loanId, ct) is null) return Results.NotFound();
            var deleted = await db.MortgageBalances.Where(x => x.MortgageId == loanId && x.Date == date).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static HomeDto ToDto(Home home, HomeData data, DateOnly today)
    {
        var value = data.Values.GetValueOrDefault(home.Id)?[^1];
        var loans = data.Loans.GetValueOrDefault(home.Id, []).Select(m =>
        {
            var list = data.Statements.GetValueOrDefault(m.Id, []);
            var terms = MortgageMath.Terms.Of(m);
            var owed = MortgageMath.OwedOn(list, terms, today);
            return new MortgageDto(m.Id, m.Name, m.InterestPct, m.ContributionPct, m.EndDate, m.InterestOnlyUntil, m.Archived,
                owed, list.Count > 0 ? list[^1].Balance : null, list.Count > 0 ? list[^1].Date : null,
                owed is { } o ? Round(MortgageMath.Payment(o, terms, today)) : null);
        }).ToList();
        var owedTotal = loans.Where(l => !l.Archived).Sum(l => l.Owed ?? 0);
        return new HomeDto(home.Id, home.Name, home.Archived, value?.Value, value?.Date, owedTotal, (value?.Value ?? 0) - owedTotal, loans);
    }

    private static MortgageMath.Month Round(MortgageMath.Month m) =>
        new(Math.Round(m.Interest, 2), Math.Round(m.Contribution, 2), Math.Round(m.Repayment, 2));

    private static void Apply(Mortgage loan, SaveMortgageRequest req)
    {
        loan.Name = req.Name!.Trim();
        loan.InterestPct = req.InterestPct;
        loan.ContributionPct = req.ContributionPct;
        loan.EndDate = req.EndDate;
        loan.InterestOnlyUntil = req.InterestOnlyUntil;
        loan.Archived = req.Archived;
    }

    private static async Task<Home?> FindHome(FireCalcDbContext db, ClaimsPrincipal principal, Guid id, CancellationToken ct)
    {
        var user = await db.GetOrCreateUserAsync(principal, ct);
        return await db.Homes.SingleOrDefaultAsync(h => h.Id == id && h.UserId == user.Id, ct);
    }

    private static async Task<Mortgage?> FindLoan(FireCalcDbContext db, ClaimsPrincipal principal, Guid homeId, Guid loanId, CancellationToken ct)
    {
        if (await FindHome(db, principal, homeId, ct) is null) return null;
        return await db.Mortgages.SingleOrDefaultAsync(m => m.Id == loanId && m.HomeId == homeId, ct);
    }

    private static Validation ValidateName(string? name) => new Validation()
        .Check(!string.IsNullOrWhiteSpace(name), "name", "Name is required.")
        .Check(name is null || name.Trim().Length <= 100, "name", "Name must be at most 100 characters.");

    private static Validation ValidateDated(DateOnly? date, decimal? amount, string field) => new Validation()
        .Check(date is not null, "date", "Date is required.")
        .Check(date is null || date <= DateOnly.FromDateTime(DateTime.UtcNow), "date", "The date can't be in the future.")
        .Check(amount is not null, field, "An amount is required.")
        .Check(amount is null || amount >= 0, field, "The amount cannot be negative.");

    private static Validation ValidateMortgage(SaveMortgageRequest req) => ValidateName(req.Name)
        .Check(req.InterestPct is null or >= -5 and <= 30, "interestPct", "The rate must be between -5 and 30 %.")
        .Check(req.ContributionPct is null or >= 0 and <= 10, "contributionPct", "Bidrag must be between 0 and 10 %.")
        .Check(req.EndDate is null || req.EndDate > new DateOnly(2000, 1, 1), "endDate", "The end date is not valid.")
        .Check(req.InterestOnlyUntil is null || req.EndDate is null || req.InterestOnlyUntil <= req.EndDate, "interestOnlyUntil", "Afdragsfrihed must end by the end date.");
}

/// <summary>Values, loans and statements for a set of homes, oldest first; shared with the dashboard.</summary>
public sealed record HomeData(
    Dictionary<Guid, List<HomeValuation>> Values,
    Dictionary<Guid, List<Mortgage>> Loans,
    Dictionary<Guid, List<MortgageBalance>> Statements)
{
    public static readonly HomeData Empty = new([], [], []);

    public static async Task<HomeData> LoadAsync(FireCalcDbContext db, List<Guid> homeIds, CancellationToken ct)
    {
        var values = (await db.HomeValuations.AsNoTracking().Where(x => homeIds.Contains(x.HomeId)).ToListAsync(ct))
            .GroupBy(x => x.HomeId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());
        var loans = await db.Mortgages.AsNoTracking().Where(m => homeIds.Contains(m.HomeId)).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var loanIds = loans.Select(m => m.Id).ToList();
        var statements = (await db.MortgageBalances.AsNoTracking().Where(x => loanIds.Contains(x.MortgageId)).ToListAsync(ct))
            .GroupBy(x => x.MortgageId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());
        return new(values, loans.GroupBy(m => m.HomeId).ToDictionary(g => g.Key, g => g.ToList()), statements);
    }
}
