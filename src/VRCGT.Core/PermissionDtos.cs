namespace VRCGT.Core;

/// <summary>
/// Granular permissions for local VRCGT accounts.
/// </summary>
public enum VrcgtPermission
{
    ViewDashboard,
    ViewMembers,
    InviteUsers,
    ApproveJoinRequests,
    KickMembers,
    BanMembers,
    ManagePosts,
    ManageEvents,
    UseKillSwitch,
    ManageSecurity,
    ManageWebhooks,
    ManageLocalUsers,
    ExportData,
}

/// <summary>
/// Maps each <see cref="LocalUserRole"/> string name to its default set of permissions.
/// </summary>
public static class PermissionMap
{
    private static readonly IReadOnlySet<VrcgtPermission> s_viewer =
        new HashSet<VrcgtPermission>
        {
            VrcgtPermission.ViewDashboard,
            VrcgtPermission.ViewMembers,
        };

    private static readonly IReadOnlySet<VrcgtPermission> s_eventManager =
        new HashSet<VrcgtPermission>
        {
            VrcgtPermission.ViewDashboard,
            VrcgtPermission.ViewMembers,
            VrcgtPermission.InviteUsers,
            VrcgtPermission.ManageEvents,
        };

    private static readonly IReadOnlySet<VrcgtPermission> s_moderator =
        new HashSet<VrcgtPermission>
        {
            VrcgtPermission.ViewDashboard,
            VrcgtPermission.ViewMembers,
            VrcgtPermission.InviteUsers,
            VrcgtPermission.ApproveJoinRequests,
            VrcgtPermission.KickMembers,
            VrcgtPermission.BanMembers,
            VrcgtPermission.ManagePosts,
            VrcgtPermission.ManageEvents,
            VrcgtPermission.ManageWebhooks,
            VrcgtPermission.ExportData,
        };

    private static readonly IReadOnlySet<VrcgtPermission> s_admin =
        new HashSet<VrcgtPermission>
        {
            VrcgtPermission.ViewDashboard,
            VrcgtPermission.ViewMembers,
            VrcgtPermission.InviteUsers,
            VrcgtPermission.ApproveJoinRequests,
            VrcgtPermission.KickMembers,
            VrcgtPermission.BanMembers,
            VrcgtPermission.ManagePosts,
            VrcgtPermission.ManageEvents,
            VrcgtPermission.UseKillSwitch,
            VrcgtPermission.ManageSecurity,
            VrcgtPermission.ManageWebhooks,
            VrcgtPermission.ExportData,
        };

    private static readonly IReadOnlySet<VrcgtPermission> s_owner =
        new HashSet<VrcgtPermission>(Enum.GetValues<VrcgtPermission>());

    /// <summary>Returns the permission set for the given role name string.</summary>
    public static IReadOnlySet<VrcgtPermission> For(string role) => role switch
    {
        "Owner"        => s_owner,
        "Admin"        => s_admin,
        "Moderator"    => s_moderator,
        "EventManager" => s_eventManager,
        _              => s_viewer,
    };

    /// <summary>Returns permission names as strings suitable for claims or API responses.</summary>
    public static IReadOnlyList<string> NamesFor(string role) =>
        For(role).Select(p => p.ToString()).ToList();
}
