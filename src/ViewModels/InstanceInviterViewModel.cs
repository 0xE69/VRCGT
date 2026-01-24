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

public partial class InstanceInviterViewModel : ObservableObject
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

    public InstanceInviterViewModel(
        IVRChatApiService apiService, 
        ISettingsService settingsService,
        IInstanceInviterService instanceInviterService,
        ICacheService cacheService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _instanceInviterService = instanceInviterService;
        _cacheService = cacheService;
        
        _currentInstanceDisplay = "Click refresh to detect current instance";

        _only18Plus = _settingsService.Settings.InstanceInviterOnly18Plus;
        _onlineOnly = !_settingsService.Settings.InstanceInviterShowOfflineFriends;
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        _settingsService.Settings.InstanceInviterOnly18Plus = value;
        _settingsService.Save();
        ApplyFilters();
        ApplyInstanceUserFilters();
    }

    partial void OnSkipVisitorsChanged(bool value)
    {
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipNewUsersChanged(bool value)
    {
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipUsersChanged(bool value)
    {
        ApplyFilters();
        ApplyInstanceUserFilters();
    }
    
    partial void OnSkipKnownChanged(bool value)
    {
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
            
            // Load instance users
            _ = RefreshInstanceUsersAsync();
        }
        else
        {
            CurrentInstanceDisplay = "No instance detected. Please join a VRChat world.";
            StatusMessage = "Not in an instance";
            InstanceUsers.Clear();
        }
    }

    private async Task RefreshInstanceUsersAsync()
    {
        try
        {
            var users = await _instanceInviterService.GetCurrentInstanceUsersAsync(_apiService);
            InstanceUsers.Clear();
            foreach (var user in users)
            {
                InstanceUsers.Add(new InstanceUserViewModel(user, this));
            }
            
            // Apply filters to instance users
            ApplyInstanceUserFilters();
            
            // Refresh filters to exclude instance users from friend list
            ApplyFilters();
            
            // Auto-invite if enabled
            if (AutoInviteEnabled && FilteredInstanceUsers.Count > 0)
            {
                _ = InviteInstanceUsersAsync();
            }
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
            user.IsSelected = true;
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
        var selectedUsers = InstanceUsers.Where(u => u.IsSelected).ToList();
        if (selectedUsers.Count == 0)
        {
            MessageBox.Show("Please select at least one user to invite.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Get the current group ID from settings
        var groupId = _settingsService.Settings.GroupId;
        if (string.IsNullOrEmpty(groupId))
        {
            MessageBox.Show("No group configured. Please set up a group first.", "No Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;

            foreach (var userVM in selectedUsers)
            {
                StatusMessage = $"Inviting {userVM.User.DisplayName} to group...";

                // Invite to group using group ID
                var success = await _apiService.SendGroupInviteAsync(groupId, userVM.User.UserId);

                if (success)
                {
                    successCount++;
                    userVM.IsSelected = false;
                }
                else
                {
                    failCount++;
                }

                // Small delay to avoid rate limiting
                await Task.Delay(500);
            }

            StatusMessage = $"Invites sent: {successCount} succeeded, {failCount} failed";
            MessageBox.Show($"Sent {successCount} group invites successfully.\n{failCount} failed.", "Invites Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sending invites: {ex.Message}";
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

    public InstanceUser User { get; }

    public InstanceUserViewModel(InstanceUser user, InstanceInviterViewModel parent)
    {
        User = user;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent.UpdateInstanceUserSelectedCount();
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
