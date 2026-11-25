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
    private readonly HashSet<string> _approvedHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupMembershipState> _exactMembership = new();
    private DateTimeOffset _nextMembershipRead;
    private string? _eligibilityGroup;
    private bool _futureHistoryAuthorised;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _includeFutureHistory = true;
    partial void OnIncludeFutureHistoryChanged(bool value) => StopAutoInvite("Auto Invite off: history scope changed. Reconfirm scope.");
    public ObservableCollection<string> AutoInviteLog { get; } = new();
    public Func<AutoGroupInviteContext, string, bool> ConfirmAutoInvite { get; set; } = (context, filters) =>
        VRCGroupTools.Views.HistoryInviteConfirmation.Ask("Enable automatic group invitations?",
            $"Group: {context.GroupId}\nActing account: {context.AccountId}\n\n{filters}",
            "Enable Auto Invite");

    partial void OnAutoInviteEnabledChanged(bool value)
    {
        if (value) _ = StartAutoInviteAsync();
        else { ++_autoGeneration; _futureHistoryAuthorised=false; _approvedHistory.Clear(); _autoSession?.Stop(); }
    }
    private AutoGroupInviteContext? ReadAutoContext()
    {
        var group = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
        var account = _apiService.CurrentUserId;
        if (!_apiService.IsLoggedIn || !ApprovalPublisher.Id(account ?? "", "usr") || !ApprovalPublisher.Id(group ?? "", "grp")) return null;
        // Group invitations target membership, not a destination. History consent survives room/client changes.
        return new(group!, account!, _apiService.InvitationSession, "reviewed-history", "", "");
    }
    public async Task StartAutoInviteAsync()
    {
        if (_autoSession?.IsRunning == true) return;
        var generation = ++_autoGeneration;
        try
        {
            var context = ReadAutoContext();
            if (_disposed || IsLoading || context == null || SkipJustJoined && MinInstanceTimeSeconds < 0 || _autoSession?.PendingSend.IsCompleted == false)
            { StopAutoInvite("Auto Invite unavailable: authenticate, select a group and review retained history; wait for any pending request."); return; }
            var selection = FilteredInstanceUsers.Where(row => row.IsSelected).Select(r => r.User.UserId).ToHashSet(StringComparer.Ordinal);
            var hasSelection = selection.Count > 0;
            await DiscoverAutoUsersAsync();
            var reviewRows = FilteredInstanceUsers.Where(row => !hasSelection || selection.Contains(row.User.UserId));
            var reviewed = reviewRows.Select(r => r.User.UserId).Where(id => ApprovalPublisher.Id(id, "usr")).Distinct(StringComparer.Ordinal).ToArray();
            var includeFuture = !hasSelection && IncludeFutureHistory;
            var filters = $"{_instanceInviterService.EncounterHistoryDiagnostic}\nHistory scope: {reviewed.Length:N0} {(hasSelection ? "selected" : "visible")} exact IDs from current and past instances.\nInclude future exact-ID local encounters: {includeFuture}.\n18+={Only18Plus}; skip Visitor={SkipVisitors}, New={SkipNewUsers}, User={SkipUsers}, Known={SkipKnown}; minimum time={(SkipJustJoined ? MinInstanceTimeSeconds + "s" : "off")}; search={SearchQuery}\n\nThe queue checks every 5 seconds and sends at least 13 seconds apart while this view is open. Unknown memberships stay pending.\n\nExact recipients:\n{string.Join("\n", reviewed)}";
            if (generation != _autoGeneration || !AutoInviteEnabled || ReadAutoContext() != context) return;
            if (!ConfirmAutoInvite(context, filters) || generation != _autoGeneration || ReadAutoContext() != context)
            { StopAutoInvite("Auto Invite not started: confirmation declined or context changed."); return; }
            _approvedHistory.Clear(); _approvedHistory.UnionWith(reviewed);
            _futureHistoryAuthorised = includeFuture;
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
                    if (row != null)
                    {
                        row.GroupInviteResult = result.Message;
                        row.NotifyEligibilityChanged();
                    }
                    return result;
                }, message =>
                {
                    AddAutoLog(message);
                    if (message.StartsWith("Auto Invite stopped", StringComparison.Ordinal)) AutoInviteEnabled = false;
                });
            AddAutoLog($"Auto GROUP invitations on — {reviewed.Length} reviewed history IDs; checks every 5 seconds, sends >=13 seconds. Unknown membership pending. No location/client requirement.");
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
        var wasEnabled = AutoInviteEnabled;
        ++_autoGeneration;
        _approvedHistory.Clear();
        _futureHistoryAuthorised=false;
        _autoSession?.Stop(reason);
        if (AutoInviteEnabled) AutoInviteEnabled = false;
        if (wasEnabled && _autoSession?.IsRunning != true && StatusMessage != reason) AddAutoLog(reason);
    }
    public void Dispose() { _disposed = true; StopAutoInvite("Auto Invite stopped: view/application closed."); }
    private async Task LoadLocalEligibilityAsync(string group)
    {
        var context = ReadAutoContext();
        if (context?.GroupId != group) return;
        var membership = await _inviteLedger.ReadMembershipAsync(group);
        var history = await _inviteLedger.ReadAsync(group);
        if (ReadAutoContext() != context) return;
        if (_eligibilityGroup != group) _exactMembership.Clear();
        _eligibilityGroup = group;
        _membership = membership;
        _history = history.ToDictionary(r => r.UserId, r => r.State);
    }
    private async Task<List<InstanceUser>> DiscoverAutoUsersAsync()
    {
        var context = ReadAutoContext();
        var generation = _autoGeneration;
        EnsureIdentityScope();
        var fresh = await _instanceInviterService.SyncLocalEncounterHistoryAsync(_inviteLedger);
        if (context != null && generation == _autoGeneration && ReadAutoContext() == context && _futureHistoryAuthorised && AutoInviteEnabled)
            _approvedHistory.UnionWith(fresh.Select(u => u.UserId).Where(id => ApprovalPublisher.Id(id, "usr")));

        if (context != null && ReadAutoContext() == context) await LoadLocalEligibilityAsync(context.GroupId);
        var retained = await _inviteLedger.ReadEncountersAsync();
        var users = retained.Select(r => new InstanceUser { UserId = r.UserId, DisplayName = r.DisplayName,
            JoinTime = r.LastSeenAt, IsAgeVerified = r.IsAgeVerified, Tags = r.Tags, ProfilePicUrl = r.ProfilePicUrl }).ToList();
        _detailsBudget = 1;
        ShowEncounters(users);
        return users;
    }

    private void ShowEncounters(List<InstanceUser> users)
    {
        static string RowKey(InstanceUser user) => ApprovalPublisher.Id(user.UserId, "usr") ? user.UserId : "name:" + user.DisplayName;
        var previous = InstanceUsers.ToDictionary(row => RowKey(row.User), StringComparer.Ordinal);
        var rows = new List<InstanceUserViewModel>(users.Count);
        foreach (var user in users)
        {
            ApplyKnownDetails(user);
            var membership = MembershipOf(user.UserId);
            var description = !ApprovalPublisher.Id(user.UserId, "usr") ? "Name only · VRChat user ID unavailable"
                : membership is GroupMembershipState.Unknown or GroupMembershipState.ApiError ? "Membership will be checked before inviting"
                : membership.ToString();
            var state = _history.GetValueOrDefault(user.UserId);
            if (state != GroupInviteState.None) description += $" · {state}";
            if (previous.TryGetValue(RowKey(user), out var row))
            {
                row.UpdateEncounter(user);
                if (row.GroupInviteResult != description) row.NotifyEligibilityChanged();
            }
            else row = new InstanceUserViewModel(user, this);
            row.GroupInviteResult = description;
            rows.Add(row);
        }
        if (!InstanceUsers.SequenceEqual(rows)) InstanceUsers = new ObservableCollection<InstanceUserViewModel>(rows);
        ApplyInstanceUserFilters();
    }
    private GroupMembershipState MembershipOf(string id)
    {
        if (_exactMembership.GetValueOrDefault(id)==GroupMembershipState.Blocked) return GroupMembershipState.Blocked;
        var cached = GroupInviteEligibility.Membership(id,_membership);
        return cached != GroupMembershipState.Unknown ? cached : _exactMembership.GetValueOrDefault(id,GroupMembershipState.Unknown);
    }
    private void ApplyKnownDetails(InstanceUser user)
    {
        if (!_userDetails.TryGetValue(user.UserId, out var detail) || detail == null) return;
        user.IsAgeVerified = detail.IsAgeVerified; user.Tags = detail.Tags; user.ProfilePicUrl = detail.ProfilePicUrl ?? "";
    }
    private bool NeedsFilterDetails => Only18Plus || SkipVisitors || SkipNewUsers || SkipUsers || SkipKnown;
    private AutoGroupInviteContext? _identityScope;
    private void EnsureIdentityScope()
    {
        var scope=ReadAutoContext();
        if (_identityScope==scope) return;
        _identityScope=scope; _userDetails.Clear(); _exactMembership.Clear(); _nextMembershipRead=default;
        _membership = null;
        _history.Clear();
    }
    private bool HasExactIdentity(string id) => _identityScope!=null && _identityScope==ReadAutoContext() && _userDetails.TryGetValue(id,out var detail) && detail?.UserId==id;
    private async Task ResolveFilterDetailsAsync(InstanceUser user)
    {
        EnsureIdentityScope();
        var scope=_identityScope;
        if (scope==null) return;
        if (_userDetails.ContainsKey(user.UserId)) { ApplyKnownDetails(user); return; }
        _userDetails[user.UserId] = null; // reserve/cache failures too; overlapping refresh cannot duplicate a lookup
        var details = await _apiService.GetExactUserIdentityAsync(user.UserId);
        if (scope!=ReadAutoContext() || scope!=_identityScope) return;
        _userDetails[user.UserId] = details?.UserId == user.UserId ? details : null;
        ApplyKnownDetails(user);
    }
    internal bool CanSelectInstanceUser(InstanceUser user) => _identityScope != null && BasicEligibility(user, false, allowPendingMembership: true) && PassDetailFilters(user);
    private bool BasicEligibility(InstanceUser user, bool automatic, bool claimed = false, bool allowPendingMembership = false)
    {
        if (!ApprovalPublisher.Id(user.UserId, "usr") || user.UserId == _apiService.CurrentUserId) return false;
        var membership = MembershipOf(user.UserId);
        if (membership != GroupMembershipState.CachedNonMember &&
            !(allowPendingMembership && membership is GroupMembershipState.Unknown or GroupMembershipState.ApiError)) return false;
        var state = _history.GetValueOrDefault(user.UserId);
        if (!claimed && (automatic ? state is not (GroupInviteState.None or GroupInviteState.NotSent) : !GroupInviteEligibility.CanSelect(user.UserId, GroupMembershipState.CachedNonMember, state))) return false;
        if (SkipJustJoined && (MinInstanceTimeSeconds < 0 || user.JoinTime == DateTime.MinValue || (DateTime.Now - user.JoinTime).TotalSeconds < MinInstanceTimeSeconds)) return false;
        if (!string.IsNullOrWhiteSpace(SearchQuery) && !user.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) && !user.UserId.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
    private bool PassDetailFilters(InstanceUser user)
    {
        if (!NeedsFilterDetails) return true;
        if (!_userDetails.TryGetValue(user.UserId, out var detail) || detail == null) return false;
        if (Only18Plus && !user.IsAgeVerified) return false;
        var trust = new InstanceUserViewModel(user, this).TrustLevel;
        return !(SkipVisitors && trust == "Visitor" || SkipNewUsers && trust == "New User" || SkipUsers && trust == "User" || SkipKnown && trust == "Known User");
    }
    private async Task<bool> IsAutoEligibleAsync(InstanceUser user)
    {
        EnsureIdentityScope();
        if (!AutoInviteEnabled || !_approvedHistory.Contains(user.UserId) || !BasicEligibility(user, true, allowPendingMembership: true)) return false;
        if (MembershipOf(user.UserId)==GroupMembershipState.Unknown && !_exactMembership.ContainsKey(user.UserId) && DateTimeOffset.UtcNow>=_nextMembershipRead)
        {
            _exactMembership[user.UserId]=GroupMembershipState.Unknown;
            _nextMembershipRead=DateTimeOffset.UtcNow.AddSeconds(13);
            var context=ReadAutoContext();
            if (context == null) return false;
            var membership = await _apiService.CheckGroupMembershipAsync(context.GroupId, user.UserId);
            if (ReadAutoContext() != context) return false;
            _exactMembership[user.UserId] = membership;
            _nextMembershipRead=DateTimeOffset.UtcNow.AddSeconds(13);
        }
        if (!BasicEligibility(user,true)) return false;
        if (!_userDetails.ContainsKey(user.UserId))
        {
            if (_detailsBudget<=0) return false;
            --_detailsBudget; await ResolveFilterDetailsAsync(user); ApplyInstanceUserFilters();
        }
        return AutoInviteEnabled && _approvedHistory.Contains(user.UserId) && HasExactIdentity(user.UserId) && PassDetailFilters(user);
    }
    private bool StillAutoEligible(InstanceUser user) => AutoInviteEnabled && !_disposed && HasExactIdentity(user.UserId) && _approvedHistory.Contains(user.UserId) && BasicEligibility(user,true,claimed:true) && PassDetailFilters(user);
}
