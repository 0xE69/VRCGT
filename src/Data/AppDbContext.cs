using System.IO;
using Microsoft.EntityFrameworkCore;
using VRCGroupTools.Data.Models;

namespace VRCGroupTools.Data;

public class AppDbContext : DbContext
{
    private readonly string _dbPath;

    public AppDbContext()
    {
        // Store in AppData like VRCX does
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "VRCGroupTools");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "vrcgrouptools.db");
    }

    public DbSet<AuditLogEntity> AuditLogs { get; set; } = null!;
    public DbSet<GroupMemberEntity> GroupMembers { get; set; } = null!;
    public DbSet<UserEntity> Users { get; set; } = null!;
    public DbSet<CachedSessionEntity> CachedSessions { get; set; } = null!;
    public DbSet<AppSettingEntity> AppSettings { get; set; } = null!;
    public DbSet<RoleSnapshotEntity> RoleSnapshots { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AuditLog configuration
        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AuditLogId).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.EventType);
        });

        // GroupMember configuration
        modelBuilder.Entity<GroupMemberEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.DisplayName);
        });

        // User configuration
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.DisplayName);
        });

        // CachedSession configuration
        modelBuilder.Entity<CachedSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // AppSetting configuration
        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // RoleSnapshot configuration (Kill Switch)
        modelBuilder.Entity<RoleSnapshotEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SnapshotId);
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => new { e.GroupId, e.UserId, e.RoleId });
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
