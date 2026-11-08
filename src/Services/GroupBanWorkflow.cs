using VRCGroupTools.Models;
namespace VRCGroupTools.Services;

public sealed record GroupBanTarget(string GroupId, string UserId, string DisplayName, string ActingUserId);

public static class GroupBanWorkflow
{
    public static async Task<string> RunAsync(GroupBanTarget target, Func<GroupBanTarget?> current,
        Func<GroupBanTarget, Task<bool>> canBan,
        Func<GroupBanTarget, Task<ModerationActionRequest?>> confirm,
        Func<GroupBanTarget, ModerationActionRequest, Task<bool>> execute)
    {
        if (string.IsNullOrWhiteSpace(target.GroupId) || string.IsNullOrWhiteSpace(target.ActingUserId) || ModerationUserSearch.UserId(target.UserId) != target.UserId)
            return "Ban not sent: unresolved group/user/account identity.";
        if (current() != target || !await canBan(target)) return "Ban not sent: group/target changed or ban permission unavailable.";
        var request = await confirm(target);
        if (request == null) return "Ban cancelled.";
        if (request.ActionType != "ban" || request.TargetUserId != target.UserId) return "Ban not sent: confirmation target mismatch.";
        if (current() != target || !await canBan(target) || current() != target) return "Ban not sent: group/target/account or permission changed.";
        return await execute(target, request) ? $"Banned {target.DisplayName} ({target.UserId}) from {target.GroupId}." : "Ban was not confirmed successful. Check VRChat before retrying.";
    }

    public static async Task<bool> CanBanAsync(IVRChatApiService api, GroupBanTarget target)
    {
        if (!api.IsLoggedIn || api.CurrentUserId != target.ActingUserId) return false;
        var group = await api.GetGroupAsync(target.GroupId);
        if (group == null) return false;
        if (group.OwnerId == target.ActingUserId) return true;
        var member = await api.GetGroupMemberAsync(target.GroupId, target.ActingUserId);
        if (member == null) return false;
        var roles = await api.GetGroupRolesAsync(target.GroupId);
        return roles.Any(r => member.RoleIds.Contains(r.RoleId) && (r.Permissions.Contains("group-bans-manage") || r.Permissions.Contains("*")));
    }
}
