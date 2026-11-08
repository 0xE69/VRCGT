using System.Windows;
namespace VRCGroupTools.Services;

public sealed record GroupInviteAttempt(bool Sent, GroupInviteState State, string Message);
public static class GroupInvitePresentation
{
    public static bool Confirm(string groupId, IEnumerable<string> recipients) => MessageBox.Show(
        $"Send group invitations to {groupId}?\n\n{string.Join("\n", recipients)}\n\nPreviously sent, in-flight and unknown invites are blocked locally. This does not send instance invitations.",
        "Confirm group invitations", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static async Task<GroupInviteAttempt> SendTrackedAsync(this IVRChatApiService api, string groupId, string userId, Func<bool>? canSend = null)
    {
        try
        {
            var sent = canSend == null ? await api.SendGroupInviteAsync(groupId, userId) : await api.SendGroupInviteGuardedAsync(groupId, userId, canSend);
            return new(sent, sent ? GroupInviteState.Sent : GroupInviteState.Failed, sent ? "Sent" : "Rejected by API; not sent. Correct the cause before explicitly retrying.");
        }
        catch (GroupInviteSuppressedException ex)
        {
            return new(false, ex.State, ex.State switch
            {
                GroupInviteState.Sent => "Already sent (local history); duplicate suppressed without HTTP.",
                GroupInviteState.InFlight => "In-flight or interrupted; duplicate suppressed. Check VRChat; no automatic retry.",
                GroupInviteState.NotSent => "Not sent: selected group/account changed before dispatch.",
                _ => "Unknown outcome; may have been sent. Repeat suppressed; check VRChat. No automatic retry."
            });
        }
    }
}
