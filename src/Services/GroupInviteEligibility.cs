namespace VRCGroupTools.Services;

public enum GroupMembershipState { Unknown, CachedMember, CachedNonMember, ApiError, Blocked }
public sealed record GroupMembershipSnapshot(string GroupId, HashSet<string> MemberIds, DateTime CapturedUtc, bool IsComplete, string? ExcludedUserId = null);
public static class GroupInviteEligibility
{
    public static GroupMembershipState Membership(string userId, GroupMembershipSnapshot? snapshot) =>
        string.IsNullOrWhiteSpace(userId) || snapshot == null ? GroupMembershipState.Unknown :
        snapshot.MemberIds.Contains(userId) ? GroupMembershipState.CachedMember :
        snapshot.IsComplete && snapshot.CapturedUtc != default && userId != snapshot.ExcludedUserId ? GroupMembershipState.CachedNonMember : GroupMembershipState.Unknown;
    public static bool CanSelect(string userId, GroupMembershipState membership, GroupInviteState invite) =>
        !string.IsNullOrWhiteSpace(userId) && membership == GroupMembershipState.CachedNonMember && invite is GroupInviteState.None or GroupInviteState.Failed or GroupInviteState.NotSent;
}
