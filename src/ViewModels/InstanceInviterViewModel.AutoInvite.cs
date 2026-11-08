using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class InstanceInviterViewModel
{
    private readonly GroupInviteLedger _inviteLedger;
    private AutoGroupInviteSession? _autoSession;
    private int _autoGeneration;
    private bool _disposed;
    private GroupMembershipSnapshot? _membership;
    private Dictionary<string, GroupInviteState> _history = new();
    private readonly Dictionary<string, UserDetails?> _userDetails = new();
    private int _detailsBudget;
    private AutomaticPresenceCorroborator? _presence;
    private Func<DateTimeOffset> _presenceUtcNow = () => DateTimeOffset.UtcNow;
    public ObservableCollection<string> AutoInviteLog { get; } = new();
    public Func<AutoGroupInviteContext, string, bool> ConfirmAutoInvite { get; set; } = (context, filters) => MessageBox.Show(
        $"Enable automatic GROUP invitations?\n\nGroup: {context.GroupId}\nActing account: {context.AccountId}\nCurrent instance: {context.WorldId}:{context.InstanceId}\n\nFilters: {filters}\n\nOnly resolved IDs, not yourself, cached non-members from a timestamped complete membership cache, and users without previous sent/in-flight/unknown/failed invitations are eligible. Local logs are untrusted discovery hints, never proof of presence. Each new candidate needs a read-only API identity/location match (online in the exact instance), fresh for at most 20 seconds including pacing. Hidden/offline/non-friend/unknown locations are skipped without automatic retry; coverage is NOT all occupants. Review those users manually. Presence reads are paced at least 13 seconds apart; unchanged polls make no API requests. Checks run locally every 5 seconds; sends are paced at least 13 seconds apart. Stop on context/filter changes or leaving this view. No restart/resume. Failed invitations require deliberate manual retry. This does NOT send friends instance invitations or publish posts.",
        "Confirm Auto Invite — current instance to selected group", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    partial void OnAutoInviteEnabledChanged(bool value)
    {
        if (value) _ = StartAutoInviteAsync();
        else { ++_autoGeneration; _autoSession?.Stop(); }
    }
    private AutoGroupInviteContext? ReadAutoContext()
    {
        var group = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
        var account = _apiService.CurrentUserId;
        var instance = _instanceInviterService.GetCurrentInstance();
        if (!_apiService.IsLoggedIn || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(group) || instance == null || string.IsNullOrEmpty(instance.SessionKey)) return null;
        // No world-owner exception unless details have been explicitly verified; fail closed in another group's instance.
        if (!instance.IsGroupInviteAllowed(group)) return null;
        return new(group, account, _apiService.InvitationSession, instance.SessionKey, instance.WorldId, instance.InstanceId);
    }
    public async Task StartAutoInviteAsync()
    {
        var generation = ++_autoGeneration;
        try
        {
            var context = ReadAutoContext();
            if (_disposed || IsLoading || context == null || SkipJustJoined && MinInstanceTimeSeconds < 0 || _autoSession?.PendingSend.IsCompleted == false)
            { StopAutoInvite("Auto Invite unavailable: sign in, select a group and join a permitted current instance; wait for any pending request."); return; }
            await LoadLocalEligibilityAsync(context.GroupId);
            if (_membership is not { IsComplete: true } || _membership.CapturedUtc == default)
            { StopAutoInvite("Auto Invite off: a timestamped COMPLETE membership cache is required. Refresh the group's member cache first."); return; }
            var filters = $"18+={Only18Plus}; skip Visitor={SkipVisitors}, New={SkipNewUsers}, User={SkipUsers}, Known={SkipKnown}; minimum time={(SkipJustJoined ? MinInstanceTimeSeconds + "s (unknown times skipped)" : "off")}; search={SearchQuery}";
            if (generation != _autoGeneration || !AutoInviteEnabled || ReadAutoContext() != context) return;
            if (!ConfirmAutoInvite(context, filters) || generation != _autoGeneration || ReadAutoContext() != context)
            { StopAutoInvite("Auto Invite not started: confirmation declined or context changed."); return; }
            _presence = new(context, () => _presenceUtcNow());
            _autoSession = new(context, ReadAutoContext, DiscoverAutoUsersAsync, IsAutoEligibleAsync, StillAutoEligible,
                async (id, guard, token) =>
                {
                    GroupInviteAttempt result;
                    try
                    {
                        var sent = await _apiService.SendAutomaticGroupInviteAsync(context.GroupId, id, guard, token);
                        result = new(sent, sent ? GroupInviteState.Sent : GroupInviteState.Failed, sent ? "Sent (recorded in shared local history)" : "Rejected; manual retry only");
                    }
                    catch (GroupInviteSuppressedException ex) { result = new(false, ex.State, $"{ex.State}: shared local history/dispatch guard blocked request"); }
                    _history[id] = result.State;
                    var row = InstanceUsers.FirstOrDefault(u => u.User.UserId == id);
                    if (row != null) row.GroupInviteResult = result.Message;
                    return result;
                }, message =>
                {
                    AddAutoLog(message);
                    if (message.StartsWith("Auto Invite stopped", StringComparison.Ordinal)) AutoInviteEnabled = false;
                });
            AddAutoLog("Auto Invite on — local checks every 5 seconds; API presence corroboration required. Hidden/non-friend/unknown presence is skipped; not all occupants are supported. Shared history prevents repeats.");
            await _autoSession.RunAsync();
        }
        catch { StopAutoInvite("Auto Invite stopped: local cache/request unavailable. No automatic retry."); }
        finally { if (generation == _autoGeneration && _autoSession?.IsRunning != true) AutoInviteEnabled = false; }
    }
    private void AddAutoLog(string message)
    {
        StatusMessage = message;
        AutoInviteLog.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        while (AutoInviteLog.Count > 50) AutoInviteLog.RemoveAt(AutoInviteLog.Count - 1);
    }
    public void StopAutoInvite(string reason = "Auto Invite off.")
    {
        ++_autoGeneration;
        _autoSession?.Stop(reason);
        if (AutoInviteEnabled) AutoInviteEnabled = false;
        if (_autoSession != null) AddAutoLog(reason);
    }
    public void Dispose() { _disposed = true; StopAutoInvite("Auto Invite stopped: view/application closed."); }
    private async Task LoadLocalEligibilityAsync(string group)
    {
        _membership = await _inviteLedger.ReadMembershipAsync(group);
        _history = (await _inviteLedger.ReadAsync(group)).ToDictionary(r => r.UserId, r => r.State);
    }
    private async Task<List<InstanceUser>> DiscoverAutoUsersAsync()
    {
        var context = ReadAutoContext();
        if (context == null) return new();
        await LoadLocalEligibilityAsync(context.GroupId);
        var users = await _instanceInviterService.GetCurrentInstanceUsersAsync(_apiService);
        _detailsBudget = 1; // at most one new detail lookup per detection cycle, never a blanket N+1 fetch
        InstanceUsers.Clear();
        foreach (var user in users)
        {
            ApplyKnownDetails(user);
            var state = _history.GetValueOrDefault(user.UserId);
            InstanceUsers.Add(new(user, this) { GroupInviteResult = $"{GroupInviteEligibility.Membership(user.UserId, _membership)} · {state} · {_presence?.Describe(context, user.UserId) ?? "presence pending — logs alone cannot authorize"}" });
        }
        ApplyInstanceUserFilters();
        return users;
    }
    private void ApplyKnownDetails(InstanceUser user)
    {
        if (!_userDetails.TryGetValue(user.UserId, out var detail) || detail == null) return;
        user.IsAgeVerified = detail.IsAgeVerified; user.Tags = detail.Tags; user.ProfilePicUrl = detail.ProfilePicUrl ?? "";
    }
    private bool NeedsFilterDetails => Only18Plus || SkipVisitors || SkipNewUsers || SkipUsers || SkipKnown;
    private async Task ResolveFilterDetailsAsync(InstanceUser user)
    {
        if (_userDetails.ContainsKey(user.UserId)) { ApplyKnownDetails(user); return; }
        _userDetails[user.UserId] = null; // reserve/cache failures too; overlapping refresh cannot duplicate a lookup
        var details = await _apiService.GetUserAsync(user.UserId);
        _userDetails[user.UserId] = details?.UserId == user.UserId ? details : null;
        ApplyKnownDetails(user);
    }
    internal bool CanSelectInstanceUser(InstanceUser user) => BasicEligibility(user, false) && PassDetailFilters(user);
    private bool BasicEligibility(InstanceUser user, bool automatic, bool claimed = false)
    {
        if (!ApprovalPublisher.Id(user.UserId, "usr") || user.UserId == _apiService.CurrentUserId) return false;
        if (_membership?.CapturedUtc == default || GroupInviteEligibility.Membership(user.UserId, _membership) != GroupMembershipState.CachedNonMember) return false;
        var state = _history.GetValueOrDefault(user.UserId);
        if (!claimed && (automatic ? state is not (GroupInviteState.None or GroupInviteState.NotSent) : !GroupInviteEligibility.CanSelect(user.UserId, GroupMembershipState.CachedNonMember, state))) return false;
        if (SkipJustJoined && (MinInstanceTimeSeconds < 0 || user.JoinTime == DateTime.MinValue || (DateTime.Now - user.JoinTime).TotalSeconds < MinInstanceTimeSeconds)) return false;
        if (!string.IsNullOrWhiteSpace(SearchQuery) && !user.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) && !user.UserId.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
    private bool PassDetailFilters(InstanceUser user)
    {
        if ((Only18Plus || SkipVisitors || SkipNewUsers || SkipUsers || SkipKnown) && (!_userDetails.TryGetValue(user.UserId, out var detail) || detail == null)) return false;
        if (Only18Plus && !user.IsAgeVerified) return false;
        var trust = new InstanceUserViewModel(user, this).TrustLevel;
        return !(SkipVisitors && trust == "Visitor" || SkipNewUsers && trust == "New User" || SkipUsers && trust == "User" || SkipKnown && trust == "Known User");
    }
    private async Task<bool> IsAutoEligibleAsync(InstanceUser user)
    {
        if (!BasicEligibility(user, true)) return false; // ledger/membership before any enrichment HTTP
        if ((Only18Plus || SkipVisitors || SkipNewUsers || SkipUsers || SkipKnown) && !_userDetails.ContainsKey(user.UserId))
        {
            if (_detailsBudget <= 0) return false;
            --_detailsBudget;
            await ResolveFilterDetailsAsync(user);
            ApplyInstanceUserFilters();
        }
        if (!PassDetailFilters(user)) return false;
        var context = ReadAutoContext();
        if (!AutoInviteEnabled || context == null) return false;
        var generation = _autoGeneration;
        var presence = _presence ??= new(context, () => _presenceUtcNow());
        bool Valid() => AutoInviteEnabled && generation == _autoGeneration && ReadAutoContext() == context;
        var corroborated = await presence.ResolveAsync(context, user.UserId, _apiService, Valid);
        var row = InstanceUsers.FirstOrDefault(u => u.User.UserId == user.UserId);
        if (row != null) row.GroupInviteResult = presence.Describe(context, user.UserId);
        return Valid() && corroborated;
    }
    private bool StillAutoEligible(InstanceUser user)
    {
        var context = ReadAutoContext();
        if (!AutoInviteEnabled || context == null || _presence?.Confirms(context, user.UserId) != true || !BasicEligibility(user, true, claimed: true) || !PassDetailFilters(user)) return false;
        // Revalidate the bounded API evidence and LOCAL departure/context after pacing, before central HTTP.
        // A forged log row remains only a hint and cannot produce API evidence.
        if (_instanceInviterService is InstanceInviterService local)
            return local.GetLocalUsers().Any(u => u.UserId == user.UserId && u.JoinTime == user.JoinTime);
        return InstanceUsers.Any(u => u.User.UserId == user.UserId);
    }
}
