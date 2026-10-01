using Microsoft.EntityFrameworkCore;

namespace FireCalc.Api.Data;

public class FireCalcDbContext(DbContextOptions<FireCalcDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<SnapshotEntry> SnapshotEntries => Set<SnapshotEntry>();
    public DbSet<Goal> Goals => Set<Goal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.GoogleSubject).IsUnique();
            e.Property(u => u.GoogleSubject).HasMaxLength(255);
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
    }
}
