namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A point-in-time snapshot of role assignments for a group, created
/// automatically before any Kill Switch execution so they can be restored.
/// The full member+role data is stored as a JSON blob to avoid extra tables.
/// </summary>
public sealed class KillSwitchSnapshot
{
    public long     Id       { get; set; }
    public string   GroupId  { get; set; } = string.Empty;

    /// <summary>When the snapshot was taken (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of members captured.</summary>
    public int MemberCount { get; set; }

    /// <summary>Total (userId, roleId) pairs captured.</summary>
    public int RoleAssignmentCount { get; set; }

    /// <summary>
    /// JSON array of objects: [{ userId, displayName, roleIds: string[] }, …]
    /// Stored as TEXT; parsed by the service layer.
    /// </summary>
    public string SnapshotJson { get; set; } = "[]";

    /// <summary>How many role assignments were re-applied via restore.</summary>
    public int RestoredCount { get; set; }

    /// <summary>When the most recent restore was completed (UTC). Null = never restored.</summary>
    public DateTime? RestoredAt { get; set; }

    /// <summary>Optional operator label (e.g. a reason string).</summary>
    public string? Label { get; set; }
}
