using System.ComponentModel;
using System.Text.Json;
using FireCalc.Api.Data;
using FireCalc.Api.Endpoints;
using FireCalc.Api.Portfolio;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FireCalc.Api.Mcp;

/// <summary>
/// What an MCP client such as Claude can ask about the signed-in person's finances. Read-only: nothing here
/// changes data. Every tool starts from the user the access token belongs to (<see cref="McpTokenHandler"/>)
/// and only reads rows owned by that user, the same way the app's own endpoints do.
/// </summary>
[McpServerToolType]
public sealed class FireCalcTools(
    IHttpContextAccessor http, FireCalcDbContext db, PortfolioHistory history, PortfolioValuation valuation)
{
    public const string Instructions =
        "FireCalc holds one person's own finances: accounts and balances, investment portfolios, homes and loans, " +
        "a FIRE goal and saved future scenarios. All amounts are in the user's currency (see 'currency'). " +
        "Start with get_overview. The data is read-only here; changes are made in the FireCalc app.";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [McpServerTool(Name = "get_overview", Title = "Overview: net worth today", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Net worth today, split by account type and by account (homes count with their equity), the change over the last month, " +
        "progress towards the FIRE goal, and how much was put aside per month over the last year.")]
    public async Task<object> GetOverview(
        [Description("Count homes and their loans. Default true.")] bool includeHome = true,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var d = await DashboardEndpoints.BuildAsync(user, includeHome, db, history, valuation, ct);
        return new
        {
            d.Currency,
            AsOf = d.Latest?.Date,
            NetWorth = d.Latest?.Total,
            ByType = d.Latest?.ByType,
            ChangeLastMonth = d.Change,
            ChangeSince = d.ChangeSince,
            Accounts = d.Accounts.Select(a => new
            {
                a.Id, a.Name, a.Type, a.Value,
                ValuedFrom = a.Tracked ? "transactions, live prices" : a.Type == AccountType.Property ? "home value less loans" : "latest balance entered",
                a.BalanceDate, a.HomeValue, a.HomeLoan,
            }),
            Goal = d.Goal,
            SavingPerMonth = d.Pace is null ? null : new { d.Pace.Since, d.Pace.InvestedPerMonth, d.Pace.SavedPerMonth },
        };
    }

    [McpServerTool(Name = "get_net_worth_history", Title = "Net worth over time", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Net worth over time with its split by account type: a point at each month end and each balance entered.")]
    public async Task<object> GetNetWorthHistory(
        [Description("First date, yyyy-mm-dd. Optional.")] DateOnly? from = null,
        [Description("Last date, yyyy-mm-dd. Optional.")] DateOnly? to = null,
        [Description("Count homes and their loans. Default true.")] bool includeHome = true,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var d = await DashboardEndpoints.BuildAsync(user, includeHome, db, history, valuation, ct);
        return new
        {
            d.Currency,
            Points = d.Series.Where(p => (from is null || p.Date >= from) && (to is null || p.Date <= to)),
        };
    }

    [McpServerTool(Name = "get_account_balances", Title = "Balances of an account", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Every balance entered for a savings, cash or loan account, oldest first. For a loan the balance is what is owed. " +
        "Investment accounts are valued from transactions instead: use get_portfolio or list_transactions.")]
    public async Task<object> GetAccountBalances(
        [Description("The account's id, from get_overview.")] Guid accountId,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id, ct)
            ?? throw new McpException("No account with that id.");
        var balances = await db.Balances.AsNoTracking().Where(b => b.AccountId == account.Id).OrderBy(b => b.Date)
            .Select(b => new { b.Date, b.Balance }).ToListAsync(ct);
        return new { user.Currency, account.Id, account.Name, account.Type, account.Archived, Balances = balances };
    }

    [McpServerTool(Name = "get_portfolio", Title = "Portfolio holdings", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Investment accounts with their holdings valued at the latest prices: quantity, price, value, cost basis, " +
        "unrealised and realised gain, dividends, weight and today's change. Values are in the user's currency.")]
    public async Task<object> GetPortfolio(CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var accounts = await valuation.ValueAsync(user.Id, user.Currency, Today, refresh: true, ct);
        return new
        {
            user.Currency,
            AsOf = Today,
            Value = accounts.Sum(a => a.Value),
            DayChange = accounts.Sum(a => a.DayChange),
            Accounts = accounts,
        };
    }

    [McpServerTool(Name = "get_portfolio_performance", Title = "Portfolio return for a period", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("How all investment accounts did together over a period: time-weighted return (deposits don't count as gains), " +
        "value and net deposits over time, and the gain of each account and each security in the period.")]
    public async Task<object> GetPortfolioPerformance(
        [Description("Start of the period, yyyy-mm-dd. Default: since the first transaction.")] DateOnly? from = null,
        [Description("End of the period, yyyy-mm-dd. Default: today.")] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var h = await history.BuildAsync(user.Id, user.Currency, from, to, ct);
        // About 60 points are plenty to describe the curve and keep the answer small.
        var step = Math.Max(1, (int)Math.Ceiling(h.Points.Count / 60.0));
        var points = h.Points.Where((_, i) => i % step == 0).ToList();
        if (h.Points.Count > 0 && points[^1] != h.Points[^1]) points.Add(h.Points[^1]);
        return new
        {
            h.Currency,
            h.FirstDate,
            h.From,
            h.To,
            ReturnPct = h.Points.LastOrDefault()?.ReturnPct,
            Points = points,
            h.Accounts,
            Securities = h.Securities.Select(s => new
            {
                s.AccountId, s.Name, s.Symbol, s.Quantity, s.Value, s.Gain, s.Invested,
                GainPct = s.Invested > 0 ? Math.Round(s.Gain / s.Invested * 100, 2) : (decimal?)null,
            }),
        };
    }

    [McpServerTool(Name = "list_transactions", Title = "Portfolio transactions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Transactions in the investment accounts (buys, sells, dividends, deposits, withdrawals, fees…), newest first. " +
        "Amounts are in the account's currency; a negative amount is money out.")]
    public async Task<object> ListTransactions(
        [Description("Only this account (id from get_overview). Optional.")] Guid? accountId = null,
        [Description("From date, yyyy-mm-dd. Optional.")] DateOnly? from = null,
        [Description("To date, yyyy-mm-dd. Optional.")] DateOnly? to = null,
        [Description("At most this many, 1 to 500. Default 100.")] int limit = 100,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var mine = db.Accounts.Where(a => a.UserId == user.Id && (accountId == null || a.Id == accountId));
        var query = db.Transactions.AsNoTracking().Include(t => t.Instrument)
            .Where(t => mine.Any(a => a.Id == t.AccountId))
            .Where(t => (from == null || t.Date >= from) && (to == null || t.Date <= to));
        var total = await query.CountAsync(ct);
        var list = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        var names = await mine.ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        return new
        {
            Total = total,
            Transactions = list.Select(PortfolioEndpoints.ToDto).Select(t => new
            {
                t.Id, Account = names.GetValueOrDefault(t.AccountId), t.AccountId, t.Date, t.Type,
                Instrument = t.InstrumentName, t.Symbol, t.Isin, t.Quantity, t.Price, t.Amount, t.Note,
            }),
        };
    }

    [McpServerTool(Name = "get_homes", Title = "Homes and loans", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Homes with their latest value, what is owed on each loan today, the equity, and each loan's terms and this month's payment.")]
    public async Task<object> GetHomes(CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var homes = await db.Homes.AsNoTracking().Where(h => h.UserId == user.Id && !h.Archived).OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var data = await HomeData.LoadAsync(db, homes.Select(h => h.Id).ToList(), ct);
        return new { user.Currency, Homes = homes.Select(h => HomeEndpoints.ToDto(h, data, Today)) };
    }

    [McpServerTool(Name = "get_fire_plan", Title = "FIRE goal and scenarios", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The FIRE goal and progress towards it, the person's age, and each saved scenario with its assumptions and " +
        "projection (as on the app's Fremtid page): net worth in 5 and 10 years, at the FIRE age and at 90, the first year's " +
        "withdrawal, the age the goal is reached, and whether the money runs out. Given both in future (nominal) money and in today's money.")]
    public async Task<object> GetFirePlan(
        [Description("Count homes and their loans. Default true.")] bool includeHome = true,
        CancellationToken ct = default)
    {
        var user = await UserAsync(ct);
        var d = await DashboardEndpoints.BuildAsync(user, includeHome, db, history, valuation, ct);
        var scenarios = await db.Scenarios.AsNoTracking().Where(s => s.UserId == user.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var target = d.Goal?.Goal.TargetAmount;
        var today = Today;
        var start = ScenarioProjection.StartFrom(d);

        object Summary(List<ScenarioProjection.Point> points, decimal fireAge)
        {
            double? At(double age) => Round(ScenarioProjection.AtAge(points, age)?.NetWorth);
            var first = points[0];
            return new
            {
                In5Years = At(first.Age + 5),
                In10Years = At(first.Age + 10),
                AtFireAge = At((double)fireAge),
                FirstYearWithdrawal = Round(points.FirstOrDefault(p => p.Age > (double)fireAge)?.Withdrawal),
                At90 = At(90),
                GoalReachedAtAge = target is null ? null : Round(points.FirstOrDefault(p => p.NetWorth >= (double)target)?.Age, 1),
            };
        }

        return new
        {
            d.Currency,
            BirthDate = user.BirthDate,
            Age = user.BirthDate is { } b ? Round(ScenarioProjection.AgeOn(b, today), 1) : null,
            Goal = d.Goal,
            NetWorthToday = d.Latest?.Total,
            SavingPerMonth = d.Pace is null ? null : new { d.Pace.InvestedPerMonth, d.Pace.SavedPerMonth },
            Note = user.BirthDate is null
                ? "No birth date is set in FireCalc, so scenarios can't be projected. It is set on the Fremtid page."
                : null,
            Scenarios = scenarios.Select(s =>
            {
                var dto = ScenarioEndpoints.ToDto(s);
                if (user.BirthDate is not { } birth) return (object)new { Assumptions = dto };
                var projection = ScenarioProjection.Project(start, s, dto.Events, birth, today);
                return new
                {
                    Assumptions = dto,
                    Projection = new
                    {
                        Nominal = Summary(projection.Points, s.FireAge),
                        TodaysMoney = Summary(ScenarioProjection.InTodaysMoney(projection.Points, s.InflationPct), s.FireAge),
                        MoneyRunsOutAtAge = Round(projection.DepletedAge, 1),
                        HomeLoansPaidAtAge = Round(projection.LoansPaidAge, 1),
                    },
                };
            }),
        };
    }

    private static double? Round(double? value, int digits = 0) => value is { } v ? Math.Round(v, digits) : null;

    private async Task<User> UserAsync(CancellationToken ct)
    {
        var claim = http.HttpContext?.User.FindFirst(McpTokenHandler.UserIdClaim)?.Value;
        if (!Guid.TryParse(claim, out var id)) throw new McpException("Not signed in.");
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new McpException("The FireCalc user no longer exists.");
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        // Keeps æ, ø and å readable in the answers instead of \u escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };
}
