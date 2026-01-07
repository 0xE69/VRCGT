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
