using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class InstanceInviterViewModel : ObservableObject, IDisposable
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IInstanceInviterService _instanceInviterService;
    private readonly ICacheService _cacheService;

    [ObservableProperty]
    private ObservableCollection<FriendItemViewModel> _friends = new();

    [ObservableProperty]
    private ObservableCollection<FriendItemViewModel> _filteredFriends = new();

    [ObservableProperty]
    private ObservableCollection<InstanceUserViewModel> _instanceUsers = new();

    [ObservableProperty]
    private ObservableCollection<InstanceUserViewModel> _filteredInstanceUsers = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _only18Plus;

    [ObservableProperty]
    private bool _onlineOnly = true;

    [ObservableProperty]
    private bool _skipVisitors;

    [ObservableProperty]
    private bool _skipNewUsers;

    [ObservableProperty]
    private bool _skipUsers;

    [ObservableProperty]
    private bool _skipKnown;

    [ObservableProperty]
    private bool _skipJustJoined;

    [ObservableProperty]
    private int _minInstanceTimeSeconds = 60;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private CurrentInstanceInfo? _currentInstance;

    [ObservableProperty]
    private string? _currentInstanceDisplay;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _autoInviteEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedInstanceUsers))]
    private int _instanceUserSelectedCount;

    public bool HasSelectedInstanceUsers => InstanceUserSelectedCount > 0;

    [ObservableProperty]
    private InstanceDetails? _currentInstanceDetails;

    public InstanceInviterViewModel(
        IVRChatApiService apiService, 
        ISettingsService settingsService,
        IInstanceInviterService instanceInviterService,
        ICacheService cacheService, GroupInviteLedger? inviteLedger = null)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _instanceInviterService = instanceInviterService;
        _cacheService = cacheService;
        _inviteLedger = inviteLedger ?? new GroupInviteLedger();
        
        _currentInstanceDisplay = "Click refresh to detect current instance";

        _only18Plus = _settingsService.Settings.InstanceInviterOnly18Plus;
        _onlineOnly = !_settingsService.Settings.InstanceInviterShowOfflineFriends;
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
        ApplyInstanceUserFilters();
        StopAutoInvite();
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review and explicitly enable again.");
        _settingsService.Settings.InstanceInviterOnly18Plus = value;
        _settingsService.Save();
        ApplyFilters();
        ApplyInstanceUserFilters();
    }

    partial void OnSkipVisitorsChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipNewUsersChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipUsersChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipKnownChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }

    partial void OnSkipJustJoinedChanged(bool value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }

    partial void OnMinInstanceTimeSecondsChanged(int value)
    {
        StopAutoInvite("Auto Invite off: filters changed. Review filters and explicitly enable again.");
        ApplyFilters();
        ApplyInstanceUserFilters();
    }

    [RelayCommand]
    private Task LoadAsync() => RefreshHistoryAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StopAutoInvite("Auto Invite off. Review the refreshed history to enable it again.");
        await RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            StatusMessage = "Reading local VRChat logs and retained encounters…";
            await RefreshInstanceUsersAsync();
            StatusMessage = $"{InstanceUsers.Count:N0} retained encounters. {_instanceInviterService.EncounterHistoryDiagnostic}";
            UpdateCurrentInstance();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not refresh encounter history. {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private void UpdateCurrentInstance()
    {
        CurrentInstance = _instanceInviterService.GetCurrentInstance();
        CurrentInstanceDisplay = CurrentInstance == null
            ? _instanceInviterService.DetectionDiagnostic.Display
            : $"Current Instance: {CurrentInstance.WorldId} (detected at {CurrentInstance.DetectedAt:HH:mm:ss})";
    }

    private async Task RefreshInstanceUsersAsync()
    {
        var users = await DiscoverAutoUsersAsync();
        var context = _identityScope;
        if (!NeedsFilterDetails || context == null) return;
        var candidates = users.Where(u => BasicEligibility(u, false, allowPendingMembership: true)).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!NeedsFilterDetails || ReadAutoContext() != context) break;
            StatusMessage = $"Checking profile filters ({i + 1:N0} of {candidates.Count:N0})…";
            await ResolveFilterDetailsAsync(candidates[i]);
        }
        ShowEncounters(users);
    }

    private void ApplyInstanceUserFilters()
    {
        var filtered = InstanceUsers.Where(u =>
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery) && !u.User.DisplayName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) && !u.User.UserId.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) return false;
            // Apply 18+ filter
            if (Only18Plus && !u.User.IsAgeVerified)
                return false;

            // Apply trust level filters
            if (SkipVisitors && u.TrustLevel == "Visitor")
                return false;
            if (SkipNewUsers && u.TrustLevel == "New User")
                return false;
            if (SkipUsers && u.TrustLevel == "User")
                return false;
            if (SkipKnown && u.TrustLevel == "Known User")
                return false;

            if (SkipJustJoined)
            {
                var duration = DateTime.Now - u.User.JoinTime;
                if (MinInstanceTimeSeconds < 0 || u.User.JoinTime == DateTime.MinValue || duration.TotalSeconds < MinInstanceTimeSeconds)
                    return false;
            }

            return true;
        });

        var visible = filtered.ToList();
        if (!FilteredInstanceUsers.SequenceEqual(visible))
            FilteredInstanceUsers = new ObservableCollection<InstanceUserViewModel>(visible);
        UpdateInstanceUserSelectedCount();
    }

    private void ApplyFilters()
    {
        FilteredFriends.Clear();

        var query = SearchQuery?.ToLowerInvariant() ?? string.Empty;
        
        // Get list of users already in instance to exclude them
        var instanceUserNames = InstanceUsers.Select(u => u.User.DisplayName.ToLowerInvariant()).ToHashSet();

        var filtered = Friends.Where(f =>
        {
            // Exclude friends already in the instance
            if (instanceUserNames.Contains(f.Friend.DisplayName.ToLowerInvariant()))
                return false;

            // Apply 18+ filter
            if (Only18Plus && !f.Friend.IsAgeVerified)
                return false;

            // Apply trust level filters
            if (SkipVisitors && f.TrustLevel == "Visitor")
                return false;
            if (SkipNewUsers && f.TrustLevel == "New User")
                return false;
            if (SkipUsers && f.TrustLevel == "User")
                return false;
            if (SkipKnown && f.TrustLevel == "Known User")
                return false;

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(query))
            {
                var matchesName = f.Friend.DisplayName.ToLowerInvariant().Contains(query);
                var matchesStatus = f.Friend.StatusDescription?.ToLowerInvariant().Contains(query) ?? false;
                if (!matchesName && !matchesStatus)
                    return false;
            }

            return true;
        });

        foreach (var friend in filtered)
        {
            FilteredFriends.Add(friend);
        }
    }

    [RelayCommand]
    private async Task InviteSelectedAsync()
    {
        if (CurrentInstance == null)
        {
            MessageBox.Show("No instance detected. Please join a VRChat world first.", "No Instance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedFriends = Friends.Where(f => f.IsSelected).ToList();
        if (selectedFriends.Count == 0)
        {
            MessageBox.Show("Please select at least one friend to invite.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Only18Plus && selectedFriends.Any(f => !f.Friend.IsAgeVerified))
        {
            MessageBox.Show("Cannot invite non-18+ users when 18+ filter is enabled.", "Filter Violation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;
            var skippedCount = 0;

            using (var db = new AppDbContext())
            {
                foreach (var friendVM in selectedFriends)
                {
                    // Check if already invited to this instance
                    var alreadyInvited = await db.InvitedUsers
                        .AnyAsync(u => u.UserId == friendVM.Friend.UserId 
                                    && u.WorldId == CurrentInstance.WorldId 
                                    && u.InstanceId == CurrentInstance.InstanceId);

                    if (alreadyInvited)
                    {
                        StatusMessage = $"Skipping {friendVM.Friend.DisplayName} (already invited)...";
                        skippedCount++;
                        continue;
                    }

                    StatusMessage = $"Inviting {friendVM.Friend.DisplayName}...";
                    
                    var success = await _instanceInviterService.InviteToCurrentInstanceAsync(friendVM.Friend.UserId, _apiService);
                    
                    // Create database record
                    var inviteRecord = new InvitedUser
                    {
                        UserId = friendVM.Friend.UserId,
                        DisplayName = friendVM.Friend.DisplayName,
                        ProfilePicUrl = friendVM.Friend.ProfilePicUrl,
                        IsAgeVerified = friendVM.Friend.IsAgeVerified,
                        TrustLevel = friendVM.TrustLevel,
                        InvitedAt = DateTime.UtcNow,
                        WorldId = CurrentInstance.WorldId,
                        InstanceId = CurrentInstance.InstanceId,
                        InviteSuccessful = success
                    };

                    db.InvitedUsers.Add(inviteRecord);
                    await db.SaveChangesAsync();

                    // Verify the record was saved
                    var verified = await db.InvitedUsers
                        .AnyAsync(u => u.UserId == friendVM.Friend.UserId 
                                    && u.WorldId == CurrentInstance.WorldId 
                                    && u.InstanceId == CurrentInstance.InstanceId);

                    if (verified && success)
                    {
                        successCount++;
                        friendVM.IsSelected = false;
                    }
                    else if (!success)
                    {
                        failCount++;
                    }

                    // Small delay to avoid rate limiting
                    await Task.Delay(500);
                }
            }

            StatusMessage = $"Invites sent: {successCount} succeeded, {failCount} failed, {skippedCount} skipped";
            MessageBox.Show($"Sent {successCount} invites successfully.\n{failCount} failed.\n{skippedCount} already invited.", "Invites Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sending invites: {ex.Message}";
            MessageBox.Show($"Failed to send invites: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            UpdateSelectedCount();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var friend in FilteredFriends)
        {
            friend.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var friend in Friends)
        {
            friend.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Friends.Count(f => f.IsSelected);
    }

    [RelayCommand]
    private void SelectAllInstanceUsers()
    {
        SetSelection(FilteredInstanceUsers, true);
    }

    [RelayCommand]
    private void DeselectAllInstanceUsers() => SetSelection(InstanceUsers, false);

    private bool _updatingSelection;
    private void SetSelection(IEnumerable<InstanceUserViewModel> rows, bool select)
    {
        _updatingSelection = true;
        try
        {
            foreach (var row in rows) row.IsSelected = select && row.CanInvite;
        }
        finally { _updatingSelection = false; }
        UpdateInstanceUserSelectedCount();
    }

    public void UpdateInstanceUserSelectedCount()
    {
        if (!_updatingSelection) InstanceUserSelectedCount = FilteredInstanceUsers.Count(u => u.IsSelected);
    }

    internal void OnEncounterSelectionEdited()
    {
        if (AutoInviteEnabled) StopAutoInvite("Auto Invite off. Review the changed selection to enable it again.");
        UpdateInstanceUserSelectedCount();
    }

    public Func<string, IReadOnlyList<string>, bool> ConfirmHistoryInvites { get; set; } = GroupInvitePresentation.Confirm;

    [RelayCommand]
    private async Task InviteInstanceUsersAsync()
    {
        if (IsLoading) { GroupInvitePresentation.WriteActivity("Manual batch not started: an operation is already in progress."); return; }
        var context = ReadAutoContext();
        if (context == null)
        {
            StatusMessage = "Log in and select a group to send invitations.";
            GroupInvitePresentation.WriteActivity("Manual batch not started: no authenticated account or selected group.");
            return;
        }
        StopAutoInvite("Auto Invite off. Sending the selected group invitations.");
        IsLoading = true;
        var total = 0;
        var sent = 0;
        var failed = 0;
        var skipped = 0;
        var unknown = 0;
        var inFlight = 0;
        var confirmed = false;
        var requesting = false;
        string? currentRow = null;
        string? stopReason = null;
        bool StopForChangedContext()
        {
            if (ReadAutoContext() == context) return false;
            StatusMessage = "Invites stopped. The account or group changed.";
            stopReason = "account or group changed";
            GroupInvitePresentation.WriteActivity($"{currentRow} result=NotSent reason={stopReason}");
            skipped++;
            return true;
        }

        try
        {
            EnsureIdentityScope();
            await LoadLocalEligibilityAsync(context.GroupId);
            var selected = FilteredInstanceUsers.Where(row => row.IsSelected && row.CanInvite).ToList();
            total = selected.Count;
            if (total == 0)
            {
                StatusMessage = "Select a user with a valid VRChat ID to invite.";
                GroupInvitePresentation.WriteActivity("Manual batch not started: no eligible recipients selected.");
                return;
            }
            if (ReadAutoContext() != context) { GroupInvitePresentation.WriteActivity("Manual batch not started: account or group changed."); return; }
            if (!ConfirmHistoryInvites(context.GroupId, selected.Select(r => $"{r.User.DisplayName} ({r.User.UserId})").ToList()))
            {
                GroupInvitePresentation.WriteActivity("Manual batch cancelled before sending.");
                return;
            }

            confirmed = true;
            GroupInvitePresentation.WriteActivity($"Manual batch started group={context.GroupId} recipients={total}");
            for (var i = 0; i < total; i++)
            {
                var row = selected[i];
                var user = row.User;
                var idForLog = ApprovalPublisher.Id(user.UserId, "usr") ? user.UserId : "(invalid user ID)";
                currentRow = $"[{i + 1}/{total}] {idForLog}";
                if (StopForChangedContext()) return;
                if (MembershipOf(user.UserId) is GroupMembershipState.Unknown or GroupMembershipState.ApiError)
                {
                    StatusMessage = $"Checking membership for {user.DisplayName}…";
                    GroupInvitePresentation.WriteActivity($"{currentRow} checking membership name={user.DisplayName}");
                    var membership = await _apiService.CheckGroupMembershipAsync(context.GroupId, user.UserId);
                    if (StopForChangedContext()) return;
                    _exactMembership[user.UserId] = membership;
                    GroupInvitePresentation.WriteActivity($"{currentRow} membership={membership}");
                }
                if (!BasicEligibility(user, false) || !PassDetailFilters(user))
                {
                    var membership = MembershipOf(user.UserId);
                    var prior = _history.GetValueOrDefault(user.UserId);
                    var reason = membership != GroupMembershipState.CachedNonMember ? $"membership={membership}"
                        : prior is GroupInviteState.Sent or GroupInviteState.InFlight or GroupInviteState.Unknown ? $"prior invite={prior}"
                        : "recipient eligibility or filters changed";
                    row.GroupInviteResult = $"Skipped · {membership}";
                    row.NotifyEligibilityChanged();
                    GroupInvitePresentation.WriteActivity($"{currentRow} result=Skipped reason={reason}");
                    skipped++;
                    currentRow = null;
                    continue;
                }
                StatusMessage = $"Inviting {user.DisplayName} to the group…";
                GroupInvitePresentation.WriteActivity($"{currentRow} requesting group invitation name={user.DisplayName}");
                requesting = true;
                var outcome = await _apiService.SendTrackedAsync(context.GroupId, user.UserId,
                    () => ReadAutoContext() == context && BasicEligibility(user, false, claimed: true) && PassDetailFilters(user));
                requesting = false;
                string result;
                switch (outcome.State)
                {
                    case GroupInviteState.Sent when outcome.Sent:
                        sent++;
                        result = "Sent";
                        break;
                    case GroupInviteState.Sent:
                        skipped++;
                        result = "AlreadySent reason=duplicate in local history";
                        break;
                    case GroupInviteState.Failed:
                        failed++;
                        result = "Rejected";
                        break;
                    case GroupInviteState.InFlight:
                        inFlight++;
                        result = "InFlight reason=prior request pending or interrupted";
                        break;
                    case GroupInviteState.NotSent:
                        skipped++;
                        result = "NotSent reason=context or eligibility changed before dispatch";
                        break;
                    default:
                        unknown++;
                        result = "Unknown reason=delivery could not be confirmed";
                        break;
                }
                GroupInvitePresentation.WriteActivity($"{currentRow} result={result}");
                currentRow = null;
                row.GroupInviteResult = outcome.Message;
                _history[user.UserId] = outcome.State;
                row.NotifyEligibilityChanged();
                if (i + 1 < total)
                {
                    var delayMs = Random.Shared.Next(4000, 13000);
                    GroupInvitePresentation.WriteActivity($"Waiting {delayMs / 1000.0:F1}s before recipient {i + 2}/{total}.");
                    await Task.Delay(delayMs);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Group invitations stopped. {ex.Message}";
            stopReason = ex.GetType().Name;
            if (currentRow != null)
            {
                GroupInvitePresentation.WriteActivity($"{currentRow} result={(requesting ? "Unknown" : "NotSent")} reason={stopReason}");
                if (requesting) unknown++;
                else skipped++;
            }
        }
        finally
        {
            if (confirmed || stopReason != null)
            {
                var completion = stopReason == null ? "complete" : $"stopped reason={stopReason}";
                var remaining = total - sent - failed - skipped - unknown - inFlight;
                GroupInvitePresentation.WriteActivity($"Manual batch {completion} sent={sent} rejected={failed} skipped={skipped} unknown={unknown} inFlight={inFlight} remaining={remaining}");
                var summary = $"{sent} sent, {failed} rejected, {skipped} skipped, {unknown} unknown, {inFlight} in flight.";
                StatusMessage = stopReason == null ? $"Group invitations complete. {summary}" : $"{StatusMessage} {summary} {remaining} remaining.";
            }
            IsLoading = false;
            UpdateInstanceUserSelectedCount();
        }
    }
}

public partial class FriendItemViewModel : ObservableObject
{
    private readonly InstanceInviterViewModel? _parent;

    [ObservableProperty]
    private bool _isSelected;

    public FriendInfo Friend { get; }

    public FriendItemViewModel(FriendInfo friend, InstanceInviterViewModel? parent)
    {
        Friend = friend;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent?.UpdateSelectedCount();
    }

    public string AgeVerificationDisplay => Friend.IsAgeVerified ? "✓ 18+" : "";
    public string OnlineStatusDisplay => Friend.IsOnline ? "🟢 Online" : "⚫ Offline";
    
    public string? TrustLevel
    {
        get
        {
            // Debug: Log all tags
            Console.WriteLine($"[TRUST-DEBUG] {Friend.DisplayName} ALL tags: {string.Join(", ", Friend.Tags)}");
            
            // VRChat trust level tags - check all possible formats
            // Check for exact system tags first
            if (Friend.Tags.Contains("system_trust_legend")) return "Trusted User";
            if (Friend.Tags.Contains("system_trust_veteran")) return "Trusted User";
            if (Friend.Tags.Contains("system_trust_trusted")) return "Known User";
            if (Friend.Tags.Contains("system_trust_known")) return "User";
            if (Friend.Tags.Contains("system_trust_basic")) return "New User";
            
            // Check for partial matches
            var tagsLower = Friend.Tags.Select(t => t.ToLowerInvariant()).ToList();
            if (tagsLower.Any(t => t.Contains("legend"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("veteran"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("trusted"))) return "Known User";
            if (tagsLower.Any(t => t.Contains("known"))) return "User";
            if (tagsLower.Any(t => t.Contains("basic"))) return "New User";
            
            // Default for users without trust tags
            Console.WriteLine($"[TRUST-DEBUG] {Friend.DisplayName} has NO trust tags - defaulting to Visitor");
            return "Visitor";
        }
    }
    
    public string TrustLevelDisplay => TrustLevel ?? "";
    
    public bool HasTrustLevel => !string.IsNullOrEmpty(TrustLevel);
    
    public Brush TrustLevelColor
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted User" => new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple
                "Known User" => new SolidColorBrush(Color.FromRgb(255, 123, 0)), // Orange
                "User" => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // Green
                "New User" => new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Blue
                "Visitor" => new SolidColorBrush(Color.FromRgb(149, 165, 166)), // Gray
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }
    
    public Brush TrustLevelBrush => TrustLevelColor;
    
    public Brush StatusBrush
    {
        get
        {
            return Friend.Status switch
            {
                "active" => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // Green
                "join me" => new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Blue
                "ask me" => new SolidColorBrush(Color.FromRgb(241, 196, 15)), // Yellow
                "busy" => new SolidColorBrush(Color.FromRgb(231, 76, 60)), // Red
                "offline" => new SolidColorBrush(Color.FromRgb(149, 165, 166)), // Gray
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }
}

public partial class InstanceUserViewModel : ObservableObject
{
    private readonly InstanceInviterViewModel _parent;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty] private string? _groupInviteResult;
    public InstanceUser User { get; private set; }
    public bool CanInvite => _parent.CanSelectInstanceUser(User);

    public InstanceUserViewModel(InstanceUser user, InstanceInviterViewModel parent)
    {
        User = user;
        _parent = parent;
    }

    internal void UpdateEncounter(InstanceUser user)
    {
        var changed = ReferenceEquals(User, user) || User.JoinTime != user.JoinTime || User.DisplayName != user.DisplayName ||
            User.ProfilePicUrl != user.ProfilePicUrl || User.IsAgeVerified != user.IsAgeVerified || !User.Tags.SequenceEqual(user.Tags);
        User = user;
        if (!changed) return;
        OnPropertyChanged(nameof(User));
        OnPropertyChanged(nameof(TimeInInstance));
        OnPropertyChanged(nameof(TrustLevel));
        OnPropertyChanged(nameof(TrustLevelBrush));
        NotifyEligibilityChanged();
    }

    internal void NotifyEligibilityChanged()
    {
        OnPropertyChanged(nameof(CanInvite));
        if (_isSelected && !CanInvite)
        {
            SetProperty(ref _isSelected, false, nameof(IsSelected));
            _parent.UpdateInstanceUserSelectedCount();
        }
    }

    partial void OnIsSelectedChanged(bool value) => _parent.OnEncounterSelectionEdited();

    public string TimeInInstance
    {
        get
        {
            if (User.JoinTime == DateTime.MinValue)
                return "Unknown";
                
            var duration = DateTime.Now - User.JoinTime;
            
            if (duration.TotalSeconds < 0) 
                return "Just joined";

            if (duration.TotalSeconds < 60)
                return $"{duration.Seconds}s";
            if (duration.TotalMinutes < 60)
                return $"{duration.Minutes}m {duration.Seconds}s";
            
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }
    }

    public string? TrustLevel
    {
        get
        {
            if (User.Tags == null || User.Tags.Count == 0)
                return "Visitor";

            if (User.Tags.Contains("system_trust_legend")) return "Trusted User";
            if (User.Tags.Contains("system_trust_veteran")) return "Trusted User";
            if (User.Tags.Contains("system_trust_trusted")) return "Known User";
            if (User.Tags.Contains("system_trust_known")) return "User";
            if (User.Tags.Contains("system_trust_basic")) return "New User";

            var tagsLower = User.Tags.Select(t => t.ToLowerInvariant()).ToList();
            if (tagsLower.Any(t => t.Contains("legend"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("veteran"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("trusted"))) return "Known User";
            if (tagsLower.Any(t => t.Contains("known"))) return "User";
            if (tagsLower.Any(t => t.Contains("basic"))) return "New User";

            return "Visitor";
        }
    }

    public Brush TrustLevelBrush
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted User" => new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple
                "Known User" => new SolidColorBrush(Color.FromRgb(255, 123, 0)), // Orange
                "User" => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // Green
                "New User" => new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Blue
                "Visitor" => new SolidColorBrush(Color.FromRgb(149, 165, 166)), // Gray
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }
}
