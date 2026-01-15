using System.ComponentModel.DataAnnotations;

namespace VRCGroupTools.Data.Models;

/// <summary>
/// Audit log entry stored in the database
/// </summary>
public class AuditLogEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string AuditLogId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? ActorId { get; set; }
    
    [MaxLength(200)]
    public string? ActorName { get; set; }
    
    [MaxLength(100)]
    public string? TargetId { get; set; }
    
    [MaxLength(200)]
    public string? TargetName { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Raw JSON data for additional details
    /// </summary>
    public string? RawData { get; set; }
    
    /// <summary>
    /// When this record was inserted into the local database
    /// </summary>
    public DateTime InsertedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this log was successfully sent to Discord (null if not sent)
    /// </summary>
    public DateTime? DiscordSentAt { get; set; }
}

/// <summary>
/// Group member information cached locally
/// </summary>
public class GroupMemberEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }
    
    [MaxLength(100)]
    public string? RoleId { get; set; }
    
    [MaxLength(200)]
    public string? RoleName { get; set; }
    
    public DateTime JoinedAt { get; set; }
    
    public bool HasBadge { get; set; }
    
    /// <summary>
    /// When this record was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// User information cached locally
/// </summary>
public class UserEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }
    
    [MaxLength(100)]
    public string? CurrentAvatarThumbnailImageUrl { get; set; }
    
    [MaxLength(100)]
    public string? Status { get; set; }
    
    [MaxLength(500)]
    public string? StatusDescription { get; set; }
    
    [MaxLength(500)]
    public string? Bio { get; set; }
    
    public bool IsPlus { get; set; }
    
    /// <summary>
    /// Raw JSON data for additional details
    /// </summary>
    public string? RawData { get; set; }
    
    /// <summary>
    /// When this record was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cached session/credential data (encrypted)
/// </summary>
public class CachedSessionEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Encrypted value using DPAPI
    /// </summary>
    public byte[] EncryptedValue { get; set; } = Array.Empty<byte>();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Application settings stored in database
/// </summary>
public class AppSettingEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;
    
    public string Value { get; set; } = string.Empty;
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Snapshot of user roles before emergency role removal (Kill Switch)
/// Used to restore roles after an emergency
/// </summary>
public class RoleSnapshotEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string SnapshotId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string RoleId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string RoleName { get; set; } = string.Empty;
    
    /// <summary>
    /// When the snapshot was taken (before roles were removed)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether this role has been restored
    /// </summary>
    public bool IsRestored { get; set; }
    
    /// <summary>
    /// When the role was restored (null if not restored)
    /// </summary>
    public DateTime? RestoredAt { get; set; }
}

/// <summary>
/// Security action tracking - tracks individual actions by users (kicks, bans, etc.)
/// </summary>
public class SecurityActionEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ActorUserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string ActorDisplayName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty; // "kick", "ban", "role_remove", "invite_reject", "post_delete"
    
    [MaxLength(100)]
    public string? TargetUserId { get; set; }
    
    [MaxLength(200)]
    public string? TargetDisplayName { get; set; }
    
    /// <summary>
    /// When the action occurred
    /// </summary>
    public DateTime ActionTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Additional JSON data about the action
    /// </summary>
    public string? AdditionalData { get; set; }
}

/// <summary>
/// Security incidents - tracks when thresholds are exceeded and actions are taken
/// </summary>
public class SecurityIncidentEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string IncidentId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ActorUserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string ActorDisplayName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(50)]
    public string IncidentType { get; set; } = string.Empty; // "excessive_kicks", "excessive_bans", etc.
    
    /// <summary>
    /// Number of actions that triggered the incident
    /// </summary>
    public int ActionCount { get; set; }
    
    /// <summary>
    /// Timeframe in minutes that the actions occurred within
    /// </summary>
    public int TimeframeMinutes { get; set; }
    
    /// <summary>
    /// Threshold that was exceeded
    /// </summary>
    public int Threshold { get; set; }
    
    /// <summary>
    /// Whether roles were automatically removed
    /// </summary>
    public bool RolesRemoved { get; set; }
    
    /// <summary>
    /// Comma-separated list of role IDs that were removed
    /// </summary>
    [MaxLength(500)]
    public string? RemovedRoleIds { get; set; }
    
    /// <summary>
    /// Whether Discord was notified
    /// </summary>
    public bool DiscordNotified { get; set; }
    
    /// <summary>
    /// When the incident was detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When Discord notification was sent (null if not sent)
    /// </summary>
    public DateTime? DiscordNotifiedAt { get; set; }
    
    /// <summary>
    /// Whether the incident was manually resolved
    /// </summary>
    public bool IsResolved { get; set; }
    
    /// <summary>
    /// When the incident was resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }
    
    /// <summary>
    /// Additional details about the incident
    /// </summary>
    public string? Details { get; set; }
}

/// <summary>
/// Member backup snapshot - stores complete member list for disaster recovery
/// </summary>
public class MemberBackupEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string BackupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? ProfilePicUrl { get; set; }
    
    /// <summary>
    /// Comma-separated list of role IDs the member had
    /// </summary>
    [MaxLength(500)]
    public string? RoleIds { get; set; }
    
    /// <summary>
    /// Comma-separated list of role names for display
    /// </summary>
    [MaxLength(1000)]
    public string? RoleNames { get; set; }
    
    /// <summary>
    /// When the member joined the group
    /// </summary>
    public DateTime? JoinedAt { get; set; }
    
    /// <summary>
    /// When this backup was created
    /// </summary>
    public DateTime BackupCreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Optional description of why this backup was created
    /// </summary>
    [MaxLength(500)]
    public string? BackupDescription { get; set; }
    
    /// <summary>
    /// Whether this member was successfully re-invited
    /// </summary>
    public bool WasReInvited { get; set; }
    
    /// <summary>
    /// When the re-invite was sent (null if not sent)
    /// </summary>
    public DateTime? ReInvitedAt { get; set; }
    
    /// <summary>
    /// Whether the member is still in the group (checked during backup)
    /// </summary>
    public bool IsCurrentMember { get; set; } = true;
}

/// <summary>
/// Moderation action tracking - stores detailed information about kicks, bans, and warnings
/// </summary>
public class ModerationActionEntity
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string ActionId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GroupId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty; // "kick", "ban", "warning"
    
    [Required]
    [MaxLength(100)]
    public string TargetUserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string TargetDisplayName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ActorUserId { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string ActorDisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Reason for the action (from predefined list or custom)
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional description provided by moderator
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Duration in days (0 = permanent, -1 = no duration/warning only)
    /// </summary>
    public int DurationDays { get; set; }
    
    /// <summary>
    /// Whether the action allows appeals
    /// </summary>
    public bool AllowsAppeal { get; set; } = true;
    
    /// <summary>
    /// Whether this is an instance-specific action
    /// </summary>
    public bool IsInstanceAction { get; set; }
    
    /// <summary>
    /// Instance ID if this is an instance action
    /// </summary>
    [MaxLength(200)]
    public string? InstanceId { get; set; }
    
    /// <summary>
    /// When the action was taken
    /// </summary>
    public DateTime ActionTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the action expires (null if permanent or warning)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// Whether the action is still active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When the action was revoked/expired
    /// </summary>
    public DateTime? RevokedAt { get; set; }
    
    /// <summary>
    /// Who revoked the action
    /// </summary>
    [MaxLength(100)]
    public string? RevokedByUserId { get; set; }
    
    /// <summary>
    /// Reason for revoking
    /// </summary>
    [MaxLength(500)]
    public string? RevokeReason { get; set; }
    
    /// <summary>
    /// Additional JSON data
    /// </summary>
    public string? AdditionalData { get; set; }
}
