using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;
using VRCGroupTools.Helpers;
using VRCGroupTools.Views;
using VRCGroupTools.Models;
using Microsoft.Extensions.DependencyInjection;

namespace VRCGroupTools.ViewModels;

public partial class GameLogViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly GroupInviteLedger _ledger;
    private readonly LocalEncounterHistoryReader _logReader = new();
    private readonly string _logDirectory;
    private readonly ICacheService? _cache;
    private readonly VRCGroupTools.Data.IDatabaseService? _database;
    [ObservableProperty] private string _groupContext = "No group loaded";
    [ObservableProperty] private ObservableCollection<GroupInviteRecord> _inviteHistory = new();
    private string? _contextGroupId;
    public string? SelectedGroupId => _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;


    [ObservableProperty]
    private ObservableCollection<GameLogUserViewModel> _seenUsers = new();

    [ObservableProperty]
    private ObservableCollection<GameLogUserViewModel> _filteredUsers = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _only18Plus;

    [ObservableProperty]
    private bool _hideAlreadyInGroup;

    [ObservableProperty]
    private bool _hidePriorInvites;
    partial void OnHidePriorInvitesChanged(bool value) => ApplyFilters();

    [ObservableProperty]
    private int _totalUsersFound;

    [ObservableProperty] private IReadOnlyList<GameLogFile> _logFiles = Array.Empty<GameLogFile>();
    [ObservableProperty] private string _scanSummary = "Looking for VRChat output logs…";

    public GameLogViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService,
        GroupInviteLedger? ledger = null,
        ICacheService? cache = null,
        VRCGroupTools.Data.IDatabaseService? database = null,
        string? logDirectory = null)
    {
        _apiService = apiService;
        ConfirmGroupInvites = (group, users) => Task.FromResult(HistoryInviteConfirmation.Ask(
            "Confirm group invitations",
            $"Send {users.Count} group invitations?\nGroup: {group}\nActing account: {_apiService.CurrentUserId}\n\n{string.Join("\n", users)}",
            "Send invitations"));
        _settingsService = settingsService;
        _ledger = ledger ?? new GroupInviteLedger();
        _cache = cache;
        _database = database;
        _logDirectory = logDirectory ?? LocalEncounterHistoryReader.DefaultDirectory;
    }

    [RelayCommand]
    private async Task RefreshGroupAsync() => await LoadRetainedHistoryAsync();

    public async Task RefreshGroupContextAsync()
    {
        var groupId = SelectedGroupId;
        if (groupId != _contextGroupId) { DeselectAll(); foreach (var row in SeenUsers) { row.Membership = GroupMembershipState.Unknown; row.InviteState = GroupInviteState.None; } }
        _contextGroupId = groupId;
        if (string.IsNullOrWhiteSpace(groupId)) { InviteHistory.Clear(); GroupContext = "Select a group first"; ApplyFilters(); return; }
        try
        {
            var snapshot = await _ledger.ReadMembershipAsync(groupId);
            // Legacy caches prove presence only; never infer non-membership from missing cache rows.
            if (snapshot == null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (_cache != null) foreach (var member in await _cache.LoadAsync<List<GroupMember>>($"group_members_{groupId}") ?? new()) ids.Add(member.UserId);
                if (_database != null) foreach (var member in await _database.GetGroupMembersAsync(groupId)) ids.Add(member.UserId);
                snapshot = new(groupId, ids, DateTime.MinValue, false);
            }
            var history = await _ledger.ReadAsync(groupId);
            if (SelectedGroupId != groupId) return;
            if (!InviteHistory.SequenceEqual(history)) InviteHistory = new ObservableCollection<GroupInviteRecord>(history);
            var states = history.ToDictionary(r => r.UserId, r => r.State, StringComparer.Ordinal);
            foreach (var row in SeenUsers)
            {
                var membership = GroupInviteEligibility.Membership(row.User.UserId, snapshot);
                row.Membership = membership == GroupMembershipState.Unknown
                    ? _checkedMembership.GetValueOrDefault((groupId, row.User.UserId), membership) : membership;
                row.InviteState = states.GetValueOrDefault(row.User.UserId, GroupInviteState.None);
            }
            GroupContext = $"Group: {groupId} · Membership cache: {(snapshot.IsComplete ? snapshot.CapturedUtc.ToLocalTime().ToString("g") : "incomplete/unknown — Sync members")} · History: {history.Count}";
            ApplyFilters();
        }
        catch (Exception ex) { foreach (var row in SeenUsers) row.Membership = GroupMembershipState.Unknown; GroupContext = $"Local data unavailable: {ex.Message}"; ApplyFilters(); }
    }

    [RelayCommand]
    private async Task SyncMembersAsync()
    {
        var groupId = SelectedGroupId;
        if (IsLoading || string.IsNullOrWhiteSpace(groupId)) return;
        try { IsLoading = true; await _apiService.GetGroupMembersAsync(groupId); await RefreshGroupContextAsync(); }
        catch (Exception ex) { StatusMessage = $"Membership sync failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        ApplyFilters();
    }

    partial void OnHideAlreadyInGroupChanged(bool value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    private Task LoadAsync() => ScanGameLogAsync();

    [RelayCommand]
    private async Task ScanGameLogAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Scanning local VRChat logs…";
        try
        {
            var result = await _logReader.ScanAsync(_logDirectory, _ledger);
            LogFiles = result.Files;
            ScanSummary = LogFiles.Count == 0
                ? $"{_logReader.Diagnostic} Folder: {_logDirectory}" : _logReader.Diagnostic;
        }
        catch (Exception ex) { ScanSummary = $"Could not scan {_logDirectory}: {ex.Message}"; }
        // A missing or locked file must not hide records from earlier scans.
        try
        {
            await LoadRetainedHistoryAsync();
        }
        catch (Exception ex) { StatusMessage = $"Could not load retained encounters: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenLogFile(GameLogFile? file)
    {
        if (file == null || !LogFiles.Contains(file)) return;
        try
        {
            var editor = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
            editor.ArgumentList.Add(file.FullPath);
            if (!File.Exists(file.FullPath)) { StatusMessage = $"File is no longer available: {file.Name}"; return; }
            Process.Start(editor)?.Dispose();
        }
        catch (Exception ex) { StatusMessage = $"Could not open {file.Name}: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task RefreshProfileDetailsAsync()
    {
        if (IsLoading || !_apiService.IsLoggedIn) return;
        IsLoading = true;
        try
        {
            await FetchUserDetailsAsync();
            await RefreshGroupContextAsync();
            StatusMessage = "Finished checking visible profiles.";
        }
        catch (Exception ex) { StatusMessage = $"Could not refresh profiles: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task FetchUserDetailsAsync()
    {
        if (!_apiService.IsLoggedIn) return;

        var rows = FilteredUsers.ToArray();
        var updated = new List<SeenUser>();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            StatusMessage = $"Checking profile {i + 1} of {rows.Length}…";
            if (string.IsNullOrEmpty(row.User.UserId))
            {
                var matches = (await _apiService.SearchUsersAsync(row.User.DisplayName))
                    .Where(user => string.Equals(user.DisplayName, row.User.DisplayName, StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToList();
                if (matches.Count != 1 || !ApprovalPublisher.Id(matches[0].UserId, "usr")) continue;
                var match = matches[0];
                row.User.UserId = match.UserId;
                row.User.ProfilePicUrl = match.ProfilePicUrl ?? string.Empty;
                row.User.Tags = match.Tags ?? new List<string>();
                row.User.IsAgeVerified = match.IsAgeVerified;
                row.User.Source = "Profile matched by display name";
                row.OnPropertiesChanged();
                continue;
            }
            if (!ApprovalPublisher.Id(row.User.UserId, "usr")) continue;
            var details = await _apiService.GetUserAsync(row.User.UserId);
            if (details?.UserId != row.User.UserId) continue;
            row.User.ProfilePicUrl = details.ProfilePicUrl ?? string.Empty;
            row.User.IsAgeVerified = details.IsAgeVerified;
            row.User.Tags = details.Tags ?? new List<string>();
            row.OnPropertiesChanged();
            if (row.HasRetainedId) updated.Add(row.User);
        }
        if (updated.Count > 0) await _ledger.SaveEncountersAsync(updated);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var visible = SeenUsers.Where(row =>
            (!HideAlreadyInGroup || row.Membership == GroupMembershipState.CachedNonMember) &&
            (!HidePriorInvites || row.InviteState is not (GroupInviteState.Sent or GroupInviteState.Unknown or GroupInviteState.InFlight)) &&
            (!Only18Plus || row.User.IsAgeVerified) &&
            (row.User.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             row.User.UserId.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        if (!FilteredUsers.SequenceEqual(visible))
            FilteredUsers = new ObservableCollection<GameLogUserViewModel>(visible);
        var visibleRows = visible.ToHashSet();
        foreach (var row in SeenUsers.Where(r => r.IsSelected && (!visibleRows.Contains(r) || !r.CanInvite)))
            row.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        var visible = FilteredUsers.ToHashSet();
        foreach (var user in SeenUsers)
        {
            user.IsSelected = visible.Contains(user) && user.CanInvite;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var user in SeenUsers)
        {
            user.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    internal bool IsSelf(string id) => id == _apiService.CurrentUserId;

    public async Task LoadRetainedHistoryAsync()
    {
        var users = await _ledger.ReadEncountersAsync();
        SeenUsers = new ObservableCollection<GameLogUserViewModel>(users.Select(user => new GameLogUserViewModel(user, this)));
        TotalUsersFound = SeenUsers.Count;
        await RefreshGroupContextAsync();
        StatusMessage = $"{TotalUsersFound} retained encounters";
    }

    [RelayCommand]
    private async Task CheckVisibleMembershipAsync()
    {
        var group=SelectedGroupId; var account=_apiService.CurrentUserId; var auth=_apiService.InvitationSession;
        if(IsLoading || !_apiService.IsLoggedIn || !ApprovalPublisher.Id(group??"","grp")) return;
        IsLoading=true;
        try
        {
            foreach(var row in FilteredUsers.Where(r=>r.Membership is GroupMembershipState.Unknown or GroupMembershipState.ApiError && ApprovalPublisher.Id(r.User.UserId,"usr") && !IsSelf(r.User.UserId) && r.InviteState is GroupInviteState.None or GroupInviteState.NotSent or GroupInviteState.Failed).Take(20).ToList())
            {
                var state=await _apiService.CheckGroupMembershipAsync(group!,row.User.UserId);
                if(SelectedGroupId!=group || _apiService.CurrentUserId!=account || _apiService.InvitationSession!=auth || !_apiService.IsLoggedIn) return;
                row.Membership=state;
                _checkedMembership[(group!,row.User.UserId)]=state;
            }
            ApplyFilters(); StatusMessage="Reviewed at most 20 visible unknown memberships; 200 null alone proves non-member. Unknown/API errors remain pending; no automatic retry.";
        } finally { IsLoading=false; }
    }
    private readonly Dictionary<(string Group,string User),GroupMembershipState> _checkedMembership=new();

    public void UpdateSelectedCount()
    {
        SelectedCount = SeenUsers.Count(u => u.IsSelected);
    }

    public Func<string, IReadOnlyList<string>, Task<bool>> ConfirmGroupInvites { get; set; }
    public Func<Task> InvitePacing { get; set; } = () => Task.Delay(500);

    [ObservableProperty] private GroupBanTarget? _actionTarget;
    public bool HasActionTarget => ActionTarget != null;
    partial void OnActionTargetChanged(GroupBanTarget? value) => OnPropertyChanged(nameof(HasActionTarget));
    [RelayCommand]
    private void OpenUserActions(GameLogUserViewModel? row)
    {
        if (row == null || !row.HasUserId || string.IsNullOrWhiteSpace(SelectedGroupId)) { StatusMessage = "Select a group and a resolved user identity first."; return; }
        ActionTarget = new(SelectedGroupId!, row.User.UserId, row.User.DisplayName, _apiService.CurrentUserId ?? "");
        StatusMessage = "User actions opened. No moderation action has been sent.";
    }
    [RelayCommand] private void CloseUserActions() => ActionTarget = null;
    public Func<GroupBanTarget, Task<bool>>? CheckBanPermission { get; set; }
    public Func<GroupBanTarget, Task<ModerationActionRequest?>> ConfirmGroupBan { get; set; } = async target =>
    {
        if (MessageBox.Show($"Ban {target.DisplayName} ({target.UserId}) from group {target.GroupId}?\n\nThis also applies to non-members. Continue to the moderation reason dialog?", "Confirm group ban", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return null;
        return await ModerationDialogHelper.ShowModerationDialogAsync("ban", target.GroupId, target.UserId, target.DisplayName);
    };
    public Func<GroupBanTarget, ModerationActionRequest, Task<bool>>? ExecuteGroupBan { get; set; }
    [RelayCommand]
    private async Task BanLogUserAsync()
    {
        var target = ActionTarget;
        if (IsLoading || target == null) return;
        IsLoading = true;
        try
        {
            StatusMessage = await GroupBanWorkflow.RunAsync(target,
                () => ActionTarget == null ? null : ActionTarget with { GroupId = SelectedGroupId ?? "", ActingUserId = _apiService.CurrentUserId ?? "" },
                CheckBanPermission ?? (t => GroupBanWorkflow.CanBanAsync(_apiService, t)),
                ConfirmGroupBan,
                ExecuteGroupBan ?? ((t, request) => ModerationDialogHelper.ExecuteModerationActionAsync(t.GroupId, request, _apiService, App.Services.GetRequiredService<IModerationService>())));
        }
        catch (Exception ex) { StatusMessage = $"Ban not confirmed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task InviteToGroupAsync()
    {
        if (IsLoading) return;
        var groupId = SelectedGroupId;
        if (string.IsNullOrWhiteSpace(groupId)) { StatusMessage = "Select a group first"; return; }
        var actor = _apiService.CurrentUserId; var auth = _apiService.InvitationSession;
        bool ContextValid() => _apiService.IsLoggedIn && actor == _apiService.CurrentUserId && auth == _apiService.InvitationSession && SelectedGroupId == groupId;
        if (!ContextValid() || !ApprovalPublisher.Id(groupId,"grp")) { StatusMessage="Authenticate and select an exact group first."; return; }
        IsLoading = true;
        try
        {
            await RefreshGroupContextAsync();
            var selected = FilteredUsers.Where(u => u.IsSelected && u.CanInvite).GroupBy(u => u.User.UserId, StringComparer.Ordinal).Select(g => g.First()).ToList();
            if (selected.Count == 0) { StatusMessage = "Select eligible visible non-members. Unknown membership requires visible exact membership checks or a member sync."; return; }
            if (!await ConfirmGroupInvites(groupId, selected.Select(u => $"{u.User.DisplayName} ({u.User.UserId})").ToList())) { StatusMessage = "Invitations cancelled"; return; }
            int sent = 0, failed = 0, suppressed = 0;
            foreach (var row in selected)
            {
                await InvitePacing();
                if (!ContextValid()) { StatusMessage = "Group/account/authentication changed. Remaining invitations not sent."; return; }
                if (!FilteredUsers.Contains(row) || !row.IsSelected || !row.CanInvite) continue;
                try
                {
                    var success = await _apiService.SendGroupInviteGuardedAsync(groupId, row.User.UserId, () => ContextValid() && row.IsSelected && FilteredUsers.Contains(row) && row.CanInvite);
                    row.InviteState = success ? GroupInviteState.Sent : GroupInviteState.Failed;
                    if (success) sent++; else failed++;
                }
                catch (GroupInviteSuppressedException) { suppressed++; row.InviteState = await _ledger.GetStateAsync(groupId, row.User.UserId); }
                row.IsSelected = false;
            }
            StatusMessage = $"Group invitations: {sent} sent, {failed} rejected, {suppressed} suppressed (see history for Sent/InFlight/Unknown). No automatic retries.";
        }
        catch (Exception ex) { StatusMessage = $"Stopped safely: {ex.Message}"; }
        finally { IsLoading = false; await RefreshGroupContextAsync(); UpdateSelectedCount(); }
    }
}

public class SeenUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime LastSeenAt { get; set; }
    public string? WorldId { get; set; }
    public string Source { get; set; } = "Game Log import — untrusted discovery, not verified live presence";
}

public partial class GameLogUserViewModel : ObservableObject
{
    private readonly GameLogViewModel? _parent;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private GroupMembershipState _membership = GroupMembershipState.Unknown;
    [ObservableProperty]
    private GroupInviteState _inviteState = GroupInviteState.None;
    public bool CanInvite => ApprovalPublisher.Id(User.UserId,"usr") && (_parent?.IsSelf(User.UserId) != true) && GroupInviteEligibility.CanSelect(User.UserId, Membership, InviteState);
    public string GroupStatus => $"{Membership} · Invite: {InviteState} · {User.Source}";
    partial void OnMembershipChanged(GroupMembershipState value) { OnPropertyChanged(nameof(CanInvite)); OnPropertyChanged(nameof(GroupStatus)); if (!CanInvite) IsSelected = false; }
    partial void OnInviteStateChanged(GroupInviteState value) { OnPropertyChanged(nameof(CanInvite)); OnPropertyChanged(nameof(GroupStatus)); if (!CanInvite) IsSelected = false; }
    public SeenUser User { get; }
    internal bool HasRetainedId { get; }

    public GameLogUserViewModel(SeenUser user, GameLogViewModel? parent)
    {
        User = user;
        HasRetainedId = ApprovalPublisher.Id(user.UserId, "usr");
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_parent != null) _parent.SelectedCount += value ? 1 : -1;
    }

    public string AgeVerificationDisplay => User.IsAgeVerified ? "✓ 18+" : "";
    public string LastSeenDisplay => User.LastSeenAt == DateTime.MinValue 
        ? "Unknown" 
        : User.LastSeenAt.ToString("MMM dd, HH:mm");

    public string TrustLevel
    {
        get
        {
            if (User.Tags == null || User.Tags.Count == 0) return "Unknown";
            if (User.Tags.Contains("system_trust_legend")) return "Trusted+";
            if (User.Tags.Contains("system_trust_veteran")) return "Trusted";
            if (User.Tags.Contains("system_trust_trusted")) return "Known";
            if (User.Tags.Contains("system_trust_known")) return "User";
            if (User.Tags.Contains("system_trust_basic")) return "New User";
            return "Visitor";
        }
    }

    public Brush TrustLevelBrush
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted+" => new SolidColorBrush(Color.FromRgb(255, 215, 0)),  // Gold
                "Trusted" => new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple
                "Known" => new SolidColorBrush(Color.FromRgb(255, 123, 0)),    // Orange
                "User" => new SolidColorBrush(Color.FromRgb(43, 181, 43)),     // Green
                "New User" => new SolidColorBrush(Color.FromRgb(30, 144, 255)), // Blue
                "Visitor" => new SolidColorBrush(Color.FromRgb(128, 128, 128)), // Gray
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
        }
    }

    public bool HasUserId => !string.IsNullOrEmpty(User.UserId);

    public void OnPropertiesChanged()
    {
        OnPropertyChanged(nameof(AgeVerificationDisplay));
        OnPropertyChanged(nameof(TrustLevel));
        OnPropertyChanged(nameof(TrustLevelBrush));
        OnPropertyChanged(nameof(HasUserId));
        OnPropertyChanged(nameof(CanInvite));
        OnPropertyChanged(nameof(User));
    }
}
