namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// Cached snapshot of a VRChat group member.
/// Populated (and refreshed) from the VRChat API via POST /api/groups/{id}/members/refresh.
/// Primary key is composite (GroupId + UserId) — a user can be in multiple managed groups.
/// </summary>
public sealed class GroupMember
{
    public string GroupId     { get; set; } = string.Empty;
    public string UserId      { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO-8601 UTC string from the VRChat API. Null if not returned.</summary>
    public string? JoinedAt   { get; set; }

    /// <summary>
    /// Pipe-delimited role IDs, e.g. "grol_aaa|grol_bbb".
    /// Stored as a single TEXT column for simplicity; parsed to a list by the service layer.
    /// </summary>
    public string? RoleIds    { get; set; }

    /// <summary>
    /// Pipe-delimited resolved role display names, matching the order of RoleIds.
    /// Populated during refresh when roles are fetched alongside members.
    /// </summary>
    public string? RoleNames  { get; set; }

    public string? AvatarUrl  { get; set; }

    public bool IsRepresenting { get; set; }

    /// <summary>When this member record was last written from the VRChat API.</summary>
    public DateTime CachedAt  { get; set; } = DateTime.UtcNow;
}
