using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Data;

public class FireCalcDbContext(DbContextOptions<FireCalcDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountBalance> Balances => Set<AccountBalance>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<SnapshotEntry> SnapshotEntries => Set<SnapshotEntry>();
    public DbSet<Home> Homes => Set<Home>();
    public DbSet<HomeValuation> HomeValuations => Set<HomeValuation>();
    public DbSet<Mortgage> Mortgages => Set<Mortgage>();
    public DbSet<MortgageBalance> MortgageBalances => Set<MortgageBalance>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<InstrumentPrice> InstrumentPrices => Set<InstrumentPrice>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<PortfolioTransaction> Transactions => Set<PortfolioTransaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.AuthSubject).IsUnique();
            e.Property(u => u.AuthSubject).HasMaxLength(255);
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.Name).HasMaxLength(200);
            e.Property(u => u.Currency).HasMaxLength(3);
        });

        b.Entity<Account>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.UserId);
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<AccountBalance>(e =>
        {
            e.ToTable("AccountBalances");
            e.HasKey(x => new { x.AccountId, x.Date });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Balance).HasPrecision(18, 2);
        });

        b.Entity<Home>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(h => h.UserId);
            e.Property(h => h.Name).HasMaxLength(100);
        });

        b.Entity<HomeValuation>(e =>
        {
            e.HasKey(x => new { x.HomeId, x.Date });
            e.HasOne<Home>().WithMany().HasForeignKey(x => x.HomeId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Value).HasPrecision(18, 2);
        });

        b.Entity<Mortgage>(e =>
        {
            e.HasOne<Home>().WithMany().HasForeignKey(m => m.HomeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.HomeId);
            e.Property(m => m.Name).HasMaxLength(100);
            e.Property(m => m.InterestPct).HasPrecision(6, 3);
            e.Property(m => m.ContributionPct).HasPrecision(6, 3);
        });

        b.Entity<MortgageBalance>(e =>
        {
            e.HasKey(x => new { x.MortgageId, x.Date });
            e.HasOne<Mortgage>().WithMany().HasForeignKey(x => x.MortgageId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Balance).HasPrecision(18, 2);
        });

        b.Entity<Snapshot>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.UserId, s.Date }).IsUnique();
            e.Property(s => s.Note).HasMaxLength(500);
            e.HasMany(s => s.Entries).WithOne().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SnapshotEntry>(e =>
        {
            e.HasKey(x => new { x.SnapshotId, x.AccountId });
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Balance).HasPrecision(18, 2);
        });

        b.Entity<Goal>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(g => g.UserId).IsUnique();
            e.Property(g => g.Name).HasMaxLength(100);
            e.Property(g => g.TargetAmount).HasPrecision(18, 2);
            e.Property(g => g.ExpectedAnnualReturnPct).HasPrecision(5, 2);
        });

        b.Entity<Scenario>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.MonthlySavings).HasPrecision(18, 2);
            e.Property(s => s.YearlySpending).HasPrecision(18, 2);
            e.Property(s => s.InvestmentReturnPct).HasPrecision(5, 2);
            e.Property(s => s.SavingsReturnPct).HasPrecision(5, 2);
            e.Property(s => s.HomeGrowthPct).HasPrecision(5, 2);
            e.Property(s => s.InflationPct).HasPrecision(5, 2);
            e.Property(s => s.FireAge).HasPrecision(5, 2);
            e.Property(s => s.WithdrawalPct).HasPrecision(5, 2).HasDefaultValue(4m);
            e.Property(s => s.Events).HasColumnType("jsonb");
        });

        b.Entity<Instrument>(e =>
        {
            e.HasIndex(i => i.Isin).IsUnique();
            e.Property(i => i.Isin).HasMaxLength(12);
            e.Property(i => i.Symbol).HasMaxLength(32);
            e.Property(i => i.Name).HasMaxLength(200);
            e.Property(i => i.Currency).HasMaxLength(3);
        });

        b.Entity<InstrumentPrice>(e =>
        {
            e.HasKey(p => new { p.InstrumentId, p.Date });
            e.HasOne<Instrument>().WithMany().HasForeignKey(p => p.InstrumentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Close).HasPrecision(18, 6);
        });

        b.Entity<FxRate>(e =>
        {
            e.HasKey(r => new { r.Currency, r.QuoteCurrency, r.Date });
            e.Property(r => r.Currency).HasMaxLength(3);
            e.Property(r => r.QuoteCurrency).HasMaxLength(3);
            e.Property(r => r.Rate).HasPrecision(18, 8);
        });

        b.Entity<PortfolioTransaction>(e =>
        {
            e.ToTable("PortfolioTransactions");
            e.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Instrument).WithMany().HasForeignKey(t => t.InstrumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => new { t.AccountId, t.Date });
            e.HasIndex(t => new { t.AccountId, t.Source, t.ExternalId }).IsUnique();
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Quantity).HasPrecision(18, 6);
            e.Property(t => t.Price).HasPrecision(18, 6);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.CostChange).HasPrecision(18, 2);
            e.Property(t => t.Note).HasMaxLength(500);
            e.Property(t => t.Source).HasMaxLength(20);
            e.Property(t => t.ExternalId).HasMaxLength(64);
        });
    }
}
