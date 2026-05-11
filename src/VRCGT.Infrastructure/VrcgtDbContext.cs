using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Entity Framework Core DbContext for VRCGT v2.
///
/// Database location: {IAppDataPathProvider.DatabasePath}
///   e.g.  %LocalAppData%\VRCGT\v2\data\vrcgt-v2.db
///
/// This database is separate from the v1 WPF database intentionally.
/// The two databases coexist during the migration period. v1 data can
/// be imported via a migration wizard in a later phase.
///
/// Design notes:
///   - All string primary keys use TEXT affinity (SQLite default).
///   - DateTime columns are stored as TEXT in ISO-8601 UTC.
///   - No secrets are persisted here (see docs/v2/storage.md).
///   - Migrations are code-first. Run: dotnet ef migrations add &lt;Name&gt;
///     from the VRCGT.Infrastructure project root.
/// </summary>
public sealed class VrcgtDbContext : DbContext
{
    public VrcgtDbContext(DbContextOptions<VrcgtDbContext> options) : base(options) { }

    public DbSet<AppSetting>     AppSettings     => Set<AppSetting>();
    public DbSet<LocalUser>      LocalUsers      => Set<LocalUser>();
    public DbSet<ManagedGroup>   ManagedGroups   => Set<ManagedGroup>();
    public DbSet<GroupMember>    GroupMembers    => Set<GroupMember>();
    public DbSet<AuditLogEntry>  AuditLog        => Set<AuditLogEntry>();
    public DbSet<BackgroundJob>  BackgroundJobs  => Set<BackgroundJob>();
    public DbSet<BackgroundJobItem> BackgroundJobItems => Set<BackgroundJobItem>();
    public DbSet<BadgeScan>          BadgeScans           => Set<BadgeScan>();
    public DbSet<BadgeScanMember>    BadgeScanMembers     => Set<BadgeScanMember>();
    public DbSet<KillSwitchSnapshot> KillSwitchSnapshots  => Set<KillSwitchSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ---- AppSetting ----
        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.Value).HasMaxLength(4096);
        });

        // ---- LocalUser ----
        modelBuilder.Entity<LocalUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.VrchatUserId).HasMaxLength(128);
            e.Property(x => x.VrchatDisplayName).HasMaxLength(256);
        });

        // ---- ManagedGroup ----
        modelBuilder.Entity<ManagedGroup>(e =>
        {
            e.HasKey(x => x.GroupId);
            e.Property(x => x.GroupId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.ShortCode).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.Property(x => x.IconUrl).HasMaxLength(512);
            e.Property(x => x.BannerUrl).HasMaxLength(512);
        });

        // ---- GroupMember ----
        modelBuilder.Entity<GroupMember>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.UserId });
            e.HasIndex(x => x.GroupId);                           // list by group
            e.HasIndex(x => new { x.GroupId, x.DisplayName });   // search
            e.Property(x => x.GroupId).HasMaxLength(128);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.JoinedAt).HasMaxLength(64);
            e.Property(x => x.RoleIds).HasMaxLength(1024);
            e.Property(x => x.RoleNames).HasMaxLength(1024);
            e.Property(x => x.AvatarUrl).HasMaxLength(512);
        });

        // ---- AuditLogEntry ----
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            // Main query index: list by group ordered newest-first
            e.HasIndex(x => new { x.GroupId, x.Timestamp });
            // Type filter index
            e.HasIndex(x => new { x.GroupId, x.Action });
            // Legacy single-column indexes (keep for any existing queries)
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.GroupId);
            // Natural dedup key — prevents double-inserting the same VRChat event
            e.HasIndex(x => new { x.GroupId, x.Timestamp, x.Action, x.ActorId })
             .IsUnique()
             .HasFilter(null); // SQLite ignores partial-filter syntax; NULL columns break uniqueness anyway

            e.Property(x => x.Action).HasMaxLength(256);
            e.Property(x => x.GroupId).HasMaxLength(128);
            e.Property(x => x.ActorId).HasMaxLength(128);
            e.Property(x => x.TargetId).HasMaxLength(128);
            e.Property(x => x.ActorDisplayName).HasMaxLength(256);
            e.Property(x => x.TargetDisplayName).HasMaxLength(256);
            e.Property(x => x.Source).HasMaxLength(16);
        });

        // ---- BackgroundJob ----
        modelBuilder.Entity<BackgroundJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.JobType).HasMaxLength(128);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.CurrentItemLabel).HasMaxLength(512);
            e.HasMany(x => x.Items)
             .WithOne(x => x.Job)
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- BackgroundJobItem ----
        modelBuilder.Entity<BackgroundJobItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.JobId);
        });

        // ---- BadgeScan ----
        modelBuilder.Entity<BadgeScan>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.GroupId).HasMaxLength(128);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.ErrorMessage).HasMaxLength(1024);
            e.HasMany(x => x.Members)
             .WithOne(x => x.Scan)
             .HasForeignKey(x => x.ScanId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- BadgeScanMember ----
        modelBuilder.Entity<BadgeScanMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ScanId);
            e.HasIndex(x => new { x.ScanId, x.UserId }).IsUnique();
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.DisplayName).HasMaxLength(256);
        });

        // ---- KillSwitchSnapshot ----
        modelBuilder.Entity<KillSwitchSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.GroupId).HasMaxLength(128);
            e.Property(x => x.Label).HasMaxLength(512);
            // SnapshotJson can be large; no column size cap for SQLite TEXT.
        });
    }
}
