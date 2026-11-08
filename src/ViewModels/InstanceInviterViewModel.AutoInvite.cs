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
    public Func<AutoGroupInviteContext, string, bool> ConfirmAutoInvite { get; set; } = (context, filters) => MessageBox.Show(
        $"Enable Auto GROUP invitations — current and previously encountered users?\n\nGroup: {context.GroupId}\nActing account: {context.AccountId}\n\n{filters}\n\nUntrusted/user-controlled Game History is discovery, NOT verified live presence. The visible snapshot plus future exact-ID LOCAL tracking records only if that scope is explicitly included below. Raw-log imports never silently broaden consent. Source records may be fabricated; this is NOT proof of encounter or presence. Bounded read-only discovery from the default LOCAL VRChat output logs only (16 latest files, last 5 MB/file, 1 MB/check); no contacts or arbitrary import scan. Unsupported client headers are not guaranteed readable. Valid exact IDs only; self, known members, blocked/invalid identities and unknown membership are skipped. Membership reads, if required, are bounded/paced; errors stay pending, not assumed non-members. Room changes/closure and closing VRChat do not stop this queue. Local checks every 5 seconds; sends >=13 seconds apart. Off/logout/account-auth/group/filter changes or closing this view/app stop it. Disabled on restart. No automatic retry of Failed/Unknown/InFlight/Sent. This does NOT invite friends to instances or publish posts.",
        "Confirm exact group, account and history scope", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

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
            await LoadLocalEligibilityAsync(context.GroupId);
            await DiscoverAutoUsersAsync();
            var reviewed = FilteredInstanceUsers.Select(r => r.User.UserId).Where(id => ApprovalPublisher.Id(id,"usr")).Distinct(StringComparer.Ordinal).ToArray();
            var filters = $"Local reader: {_instanceInviterService.EncounterHistoryDiagnostic}\nHistory scope: {reviewed.Length} visible exact IDs, retained limit {GroupInviteLedger.EncounterLimit}; current AND previously encountered. Include future exact-ID local tracking discovery={IncludeFutureHistory} (NOT raw-log imports/contacts).\n18+={Only18Plus}; skip Visitor={SkipVisitors}, New={SkipNewUsers}, User={SkipUsers}, Known={SkipKnown}; minimum time={(SkipJustJoined ? MinInstanceTimeSeconds + "s (unknown times skipped)" : "off")}; search={SearchQuery}\nExact recipients:\n{string.Join("\n", reviewed)}";
            if (generation != _autoGeneration || !AutoInviteEnabled || ReadAutoContext() != context) return;
            if (!ConfirmAutoInvite(context, filters) || generation != _autoGeneration || ReadAutoContext() != context)
            { StopAutoInvite("Auto Invite not started: confirmation declined or context changed."); return; }
            _approvedHistory.Clear(); _approvedHistory.UnionWith(reviewed);
            _futureHistoryAuthorised=IncludeFutureHistory;
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
        ++_autoGeneration;
        _approvedHistory.Clear();
        _futureHistoryAuthorised=false;
        _autoSession?.Stop(reason);
        if (AutoInviteEnabled) AutoInviteEnabled = false;
        if (_autoSession != null) AddAutoLog(reason);
    }
    public void Dispose() { _disposed = true; StopAutoInvite("Auto Invite stopped: view/application closed."); }
    private async Task LoadLocalEligibilityAsync(string group)
    {
        if (_eligibilityGroup != group) { _exactMembership.Clear(); _eligibilityGroup=group; }
        _membership = await _inviteLedger.ReadMembershipAsync(group);
        _history = (await _inviteLedger.ReadAsync(group)).ToDictionary(r => r.UserId, r => r.State);
    }
    private async Task<List<InstanceUser>> DiscoverAutoUsersAsync()
    {
        var context = ReadAutoContext();
        if (context == null) return new();
        EnsureIdentityScope();
        await LoadLocalEligibilityAsync(context.GroupId);
        var local = await _instanceInviterService.ReadLocalEncounterHistoryAsync();
        // Only deltas from the approved LOCAL reader can enter future scope. Retained/imported records alone cannot.
        var fresh = local.Where(u => ApprovalPublisher.Id(u.UserId,"usr")).Select(u => new SeenUser { UserId=u.UserId, DisplayName=u.DisplayName, LastSeenAt=u.JoinTime, Source="LOCAL VRChat log — untrusted discovery" }).ToList();
        if (fresh.Count > 0)
        {
            await _inviteLedger.SaveEncountersAsync(fresh);
            if (_futureHistoryAuthorised && AutoInviteEnabled) _approvedHistory.UnionWith(fresh.Select(r=>r.UserId));
        }
        var retained = await _inviteLedger.ReadEncountersAsync();
        var users = retained.Select(r => new InstanceUser { UserId=r.UserId, DisplayName=r.DisplayName, JoinTime=r.LastSeenAt, IsAgeVerified=r.IsAgeVerified, Tags=r.Tags, ProfilePicUrl=r.ProfilePicUrl }).ToList();
        _detailsBudget = 1;
        InstanceUsers.Clear();
        foreach (var user in users)
        {
            ApplyKnownDetails(user);
            InstanceUsers.Add(new(user,this) { GroupInviteResult=$"Untrusted retained Game History · {MembershipOf(user.UserId)} · {_history.GetValueOrDefault(user.UserId)} · {(_approvedHistory.Contains(user.UserId) ? "reviewed scope" : "not authorised / review required")}" });
        }
        ApplyInstanceUserFilters(); return users;
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
    internal bool CanSelectInstanceUser(InstanceUser user) => BasicEligibility(user, false) && PassDetailFilters(user);
    private bool BasicEligibility(InstanceUser user, bool automatic, bool claimed = false)
    {
        if (!ApprovalPublisher.Id(user.UserId, "usr") || user.UserId == _apiService.CurrentUserId) return false;
        if (MembershipOf(user.UserId) != GroupMembershipState.CachedNonMember) return false;
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
        EnsureIdentityScope();
        if (!AutoInviteEnabled || !_approvedHistory.Contains(user.UserId) || !ApprovalPublisher.Id(user.UserId,"usr") || user.UserId==_apiService.CurrentUserId || _history.GetValueOrDefault(user.UserId) is not (GroupInviteState.None or GroupInviteState.NotSent)) return false;
        if (MembershipOf(user.UserId)==GroupMembershipState.Unknown && !_exactMembership.ContainsKey(user.UserId) && DateTimeOffset.UtcNow>=_nextMembershipRead)
        {
            _exactMembership[user.UserId]=GroupMembershipState.Unknown;
            _nextMembershipRead=DateTimeOffset.UtcNow.AddSeconds(13);
            var context=ReadAutoContext();
            if (context!=null) _exactMembership[user.UserId]=await _apiService.CheckGroupMembershipAsync(context.GroupId,user.UserId);
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
