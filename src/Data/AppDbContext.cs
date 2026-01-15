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
    public DbSet<SecurityActionEntity> SecurityActions { get; set; } = null!;
    public DbSet<SecurityIncidentEntity> SecurityIncidents { get; set; } = null!;
    public DbSet<MemberBackupEntity> MemberBackups { get; set; } = null!;
    public DbSet<ModerationActionEntity> ModerationActions { get; set; } = null!;

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
            entity.HasIndex(e => e.DiscordSentAt);
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
        
        // SecurityAction configuration
        modelBuilder.Entity<SecurityActionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.ActionType);
            entity.HasIndex(e => e.ActionTime);
            entity.HasIndex(e => new { e.ActorUserId, e.ActionType, e.ActionTime });
        });
        
        // SecurityIncident configuration
        modelBuilder.Entity<SecurityIncidentEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IncidentId).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.IncidentType);
            entity.HasIndex(e => e.DetectedAt);
            entity.HasIndex(e => e.IsResolved);
        });
        
        // MemberBackup configuration
        modelBuilder.Entity<MemberBackupEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.BackupId);
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => new { e.BackupId, e.UserId });
            entity.HasIndex(e => e.BackupCreatedAt);
            entity.HasIndex(e => e.WasReInvited);
        });
        
        // ModerationAction configuration
        modelBuilder.Entity<ModerationActionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ActionId).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.TargetUserId);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.ActionType);
            entity.HasIndex(e => e.Reason);
            entity.HasIndex(e => e.ActionTime);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => new { e.GroupId, e.TargetUserId, e.ActionType });
            entity.HasIndex(e => new { e.GroupId, e.TargetUserId, e.Reason, e.ActionTime });
        });
    }
}
