namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A VRChat group that the user is managing with VRCGT.
/// Cached VRChat metadata is refreshed on demand via the /refresh endpoint.
/// </summary>
public sealed class ManagedGroup
{
    /// <summary>VRChat group ID, e.g. "grp_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".</summary>
    public string GroupId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ShortCode { get; set; }

    public string? Description { get; set; }

    public string? IconUrl { get; set; }

    public string? BannerUrl { get; set; }

    /// <summary>Whether this group is currently the active/primary group in the UI.</summary>
    public bool IsActive { get; set; }

    /// <summary>Sort order in the group switcher. Lower numbers appear first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Cached member count from the last VRChat API refresh.</summary>
    public int MemberCount { get; set; }

    /// <summary>Cached online count from the last VRChat API refresh.</summary>
    public int OnlineCount { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastRefreshedAt { get; set; }
}
