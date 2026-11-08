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
    private int _instanceUserSelectedCount;

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
    private async Task LoadAsync()
    {
        await LoadFriendsAsync();
        UpdateCurrentInstance();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StopAutoInvite("Auto Invite off: manual refresh. Review and explicitly enable again.");
        Console.WriteLine("[INSTANCE-VM] RefreshAsync started");
        await LoadFriendsAsync();
        Console.WriteLine("[INSTANCE-VM] Calling UpdateCurrentInstance...");
        UpdateCurrentInstance();
        Console.WriteLine("[INSTANCE-VM] RefreshAsync completed");
    }

    private async Task LoadFriendsAsync()
    {
        if (IsLoading)
        {
            Console.WriteLine("[INSTANCE-VM] LoadFriendsAsync skipped - already loading");
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Loading friends...";
            Console.WriteLine("[INSTANCE-VM] Loading friends from API...");

            var friendsList = await _apiService.GetFriendsAsync(OnlineOnly);
            Console.WriteLine($"[INSTANCE-VM] Friends loaded: {friendsList.Count}");

            Friends.Clear();
            foreach (var friend in friendsList)
            {
                Friends.Add(new FriendItemViewModel(friend, this));
            }

            ApplyFilters();
            StatusMessage = $"Loaded {Friends.Count} friends";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-VM] Error loading friends: {ex}");
            StatusMessage = $"Error loading friends: {ex.Message}";
            MessageBox.Show($"Failed to load friends: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCurrentInstance()
    {
        Console.WriteLine("[INSTANCE-VM] UpdateCurrentInstance started");
        CurrentInstance = _instanceInviterService.GetCurrentInstance();
        Console.WriteLine($"[INSTANCE-VM] CurrentInstance result: {(CurrentInstance == null ? "null" : CurrentInstance.WorldId)}");
        
        if (CurrentInstance != null)
        {
            CurrentInstanceDisplay = $"Current Instance: {CurrentInstance.WorldId} (Detected at {CurrentInstance.DetectedAt:HH:mm:ss})";
            StatusMessage = "Ready to send invites";
            
            // Fetch instance details (including world owner) asynchronously
            _ = FetchInstanceDetailsAsync();
            
            // Load instance users
            _ = RefreshInstanceUsersAsync();
        }
        else
        {
            CurrentInstanceDisplay = "No instance detected. Please join a VRChat world.";
            StatusMessage = "Not in an instance";
            CurrentInstanceDetails = null;
            InstanceUsers.Clear();
        }
    }

    private async Task FetchInstanceDetailsAsync()
    {
        if (CurrentInstance == null) return;
        
        try
        {
            CurrentInstanceDetails = await _apiService.GetInstanceAsync(CurrentInstance.WorldId, CurrentInstance.InstanceId);
            if (CurrentInstanceDetails != null)
            {
                Console.WriteLine($"[INSTANCE-VM] Instance details fetched. World owner: {CurrentInstanceDetails.OwnerId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-VM] Error fetching instance details: {ex.Message}");
            CurrentInstanceDetails = null;
        }
    }

    private async Task RefreshInstanceUsersAsync()
    {
        try
        {
            var group = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
            if (!string.IsNullOrWhiteSpace(group)) await LoadLocalEligibilityAsync(group);
            var users = await _instanceInviterService.GetCurrentInstanceUsersAsync(_apiService);
            // Explicit manual refresh resolves only NEW eligible IDs whose enabled filters need metadata.
            // Auto polling uses its separate one-new-lookup-per-cycle budget; neither path re-fetches unchanged users.
            if (NeedsFilterDetails)
                foreach (var user in users.Where(u => BasicEligibility(u, false)))
                    await ResolveFilterDetailsAsync(user);
            InstanceUsers.Clear();
            foreach (var user in users)
            {
                ApplyKnownDetails(user);
                InstanceUsers.Add(new InstanceUserViewModel(user, this) { GroupInviteResult = $"{GroupInviteEligibility.Membership(user.UserId, _membership)} · {_history.GetValueOrDefault(user.UserId)}" });
            }
            
            // Apply filters to instance users
            ApplyInstanceUserFilters();
            
            // Refresh filters to exclude instance users from friend list
            ApplyFilters();
            
            // Loading only discovers users. Sending always requires manual selection and confirmation.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-INVITER-VM] Error refreshing instance users: {ex.Message}");
        }
    }

    private void ApplyInstanceUserFilters()
    {
        FilteredInstanceUsers.Clear();

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

        foreach (var user in filtered)
        {
            FilteredInstanceUsers.Add(user);
        }
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
        foreach (var user in FilteredInstanceUsers)
        {
            user.IsSelected = BasicEligibility(user.User, false) && PassDetailFilters(user.User);
        }
        UpdateInstanceUserSelectedCount();
    }

    [RelayCommand]
    private void DeselectAllInstanceUsers()
    {
        foreach (var user in InstanceUsers)
        {
            user.IsSelected = false;
        }
        UpdateInstanceUserSelectedCount();
    }

    public void UpdateInstanceUserSelectedCount()
    {
        InstanceUserSelectedCount = InstanceUsers.Count(u => u.IsSelected);
    }

    [RelayCommand]
    private async Task InviteInstanceUsersAsync()
    {
        await InviteInstanceUsersInternal(false);
    }

    private async Task InviteInstanceUsersInternal(bool silent)
    {
        if (IsLoading) return;
        StopAutoInvite("Auto Invite off: manual invitation batch started.");
        var activeGroup = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
        if (string.IsNullOrWhiteSpace(activeGroup)) return;
        await LoadLocalEligibilityAsync(activeGroup);
        var selectedUsers = FilteredInstanceUsers.Where(u => u.IsSelected && BasicEligibility(u.User, false) && PassDetailFilters(u.User)).ToList();
        if (selectedUsers.Count == 0)
        {
            if (!silent)
                MessageBox.Show("Please select at least one user to invite.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Get the current group ID from settings
        var groupId = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
        if (string.IsNullOrEmpty(groupId))
        {
            if (!silent)
                MessageBox.Show("No group configured. Please set up a group first.", "No Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                StatusMessage = "Auto-invite failed: No group configured";
            return;
        }

        // Check if the current instance allows group invites (anti-abuse restriction)
        // Only allow invites from: the group's own instances OR non-group instances
        // Exception: World owners can always send invites from their own worlds
        // Block invites when in another group's instance to prevent abuse
        var isWorldOwner = !string.IsNullOrEmpty(_apiService.CurrentUserId) && 
                          CurrentInstanceDetails?.OwnerId == _apiService.CurrentUserId;
        
        if (CurrentInstance != null && !isWorldOwner && !CurrentInstance.IsGroupInviteAllowed(groupId))
        {
            var instanceGroupId = CurrentInstance.GetInstanceGroupId();
            if (!silent)
                MessageBox.Show(
                    $"Instance invite is restricted when you're in another group's instance.\n\n" +
                    $"You can only send group invites from:\n" +
                    $"• Your own group's instances\n" +
                    $"• Non-group (public/friends/private) instances\n" +
                    $"• Worlds you own (any instance type)\n\n" +
                    $"Current instance belongs to group: {instanceGroupId}",
                    "Instance Restriction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            else
                StatusMessage = "Auto-invite blocked: Cannot invite from another group's instance";
            return;
        }

        var confirmedContext = ReadAutoContext();
        if (confirmedContext == null || confirmedContext.GroupId != groupId) return;
        if (!GroupInvitePresentation.Confirm(groupId, selectedUsers.Select(u => $"{u.User.DisplayName} ({u.User.UserId})"))) return;
        if (ReadAutoContext() != confirmedContext) return;
        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;
            var suppressedCount = 0;

            foreach (var userVM in selectedUsers)
            {
                StatusMessage = $"Inviting {userVM.User.DisplayName} to group...";

                // Invite to group using group ID
                var outcome = await _apiService.SendTrackedAsync(groupId, userVM.User.UserId, () => ReadAutoContext() == confirmedContext && BasicEligibility(userVM.User, false, claimed: true) && PassDetailFilters(userVM.User));
                userVM.GroupInviteResult = outcome.Message;
                if (!outcome.Sent && outcome.State != GroupInviteState.Failed) { suppressedCount++; userVM.IsSelected = false; continue; }
                var success = outcome.Sent;

                if (success)
                {
                    successCount++;
                    userVM.IsSelected = false;
                }
                else
                {
                    failCount++;
                }

                // Random delay between 4 and 13 seconds
                var delay = Random.Shared.Next(4000, 13000);
                await Task.Delay(delay);
            }

            StatusMessage = $"Invites: {successCount} sent, {failCount} rejected, {suppressedCount} suppressed/unknown. See row status and Game Log history.";
            if (!silent) MessageBox.Show(StatusMessage, "Group invitations", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sending invites: {ex.Message}";
            if (!silent)
                MessageBox.Show($"Failed to send invites: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
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
    public InstanceUser User { get; }
    public bool CanInvite => _parent.CanSelectInstanceUser(User);

    public InstanceUserViewModel(InstanceUser user, InstanceInviterViewModel parent)
    {
        User = user;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent.UpdateInstanceUserSelectedCount();
    }

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
