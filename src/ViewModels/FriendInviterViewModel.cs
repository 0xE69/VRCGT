using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class FriendInviterViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<FriendInviterItemViewModel> _friends = new();

    [ObservableProperty]
    private ObservableCollection<FriendInviterItemViewModel> _filteredFriends = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _only18Plus;

    [ObservableProperty]
    private bool _onlineOnly = true;

    [ObservableProperty]
    private bool _showTrustLevels = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedCount;

    public FriendInviterViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        
        _ = LoadFriendsAsync();
    }

    private FriendInviterItemViewModel CreateFriendViewModel(FriendInfo friend)
    {
        return new FriendInviterItemViewModel(friend, this);
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        ApplyFilters();
    }

    partial void OnOnlineOnlyChanged(bool value)
    {
        _ = LoadFriendsAsync();
    }

    partial void OnShowTrustLevelsChanged(bool value)
    {
        // Refresh the filtered list to update the UI
        var currentFiltered = FilteredFriends.ToList();
        FilteredFriends.Clear();
        foreach (var friend in currentFiltered)
        {
            FilteredFriends.Add(friend);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadFriendsAsync();
    }

    private async Task LoadFriendsAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Loading friends...";

            Friends.Clear();

            var allFriends = new List<FriendInfo>();
            int offset = 0;
            int n = 100;
            
            while (true)
            {
                var friendsList = await _apiService.GetFriendsAsync(OnlineOnly, n, offset);
                
                if (friendsList.Count == 0)
                    break;
                
                allFriends.AddRange(friendsList);
                
                if (friendsList.Count < n)
                    break;
                
                offset += n;
                
                // Update status if taking a while
                StatusMessage = $"Loading friends... ({allFriends.Count})";

                // Safety limit to prevent infinite loops if API behaves weirdly
                if (allFriends.Count >= 5000)
                    break;
            }

            foreach (var friend in allFriends)
            {
                Friends.Add(CreateFriendViewModel(friend));
            }

            ApplyFilters();
            UpdateSelectedCount();
            StatusMessage = $"Loaded {Friends.Count} friends";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading friends: {ex.Message}";
            MessageBox.Show($"Failed to load friends: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        FilteredFriends.Clear();

        var query = SearchQuery?.ToLowerInvariant() ?? string.Empty;

        var filtered = Friends.Where(f =>
        {
            // Apply 18+ filter
            if (Only18Plus && !f.Friend.IsAgeVerified)
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
    private async Task InviteToGroupAsync()
    {
        var selectedFriends = Friends.Where(f => f.IsSelected).ToList();
        if (selectedFriends.Count == 0)
        {
            MessageBox.Show("Please select at least one friend to invite.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
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

            foreach (var friendVM in selectedFriends)
            {
                StatusMessage = $"Inviting {friendVM.Friend.DisplayName} to group...";

                // Invite to group using group ID
                var success = await _apiService.SendGroupInviteAsync(groupId, friendVM.Friend.UserId);

                if (success)
                {
                    successCount++;
                    friendVM.IsSelected = false;
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
            UpdateSelectedCount();
        }
    }
}

public partial class FriendInviterItemViewModel : ObservableObject
{
    private readonly FriendInviterViewModel? _parent;

    [ObservableProperty]
    private bool _isSelected;

    public FriendInfo Friend { get; }

    public FriendInviterItemViewModel(FriendInfo friend, FriendInviterViewModel? parent)
    {
        Friend = friend;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent?.UpdateSelectedCount();
    }

    public string? TrustLevel
    {
        get
        {
            if (Friend.Tags == null || Friend.Tags.Count == 0)
                return "Visitor";

            if (Friend.Tags.Contains("system_trust_legend")) return "Trusted User";
            if (Friend.Tags.Contains("system_trust_veteran")) return "Trusted User";
            if (Friend.Tags.Contains("system_trust_trusted")) return "Known User";
            if (Friend.Tags.Contains("system_trust_known")) return "User";
            if (Friend.Tags.Contains("system_trust_basic")) return "New User";

            var tagsLower = Friend.Tags.Select(t => t.ToLowerInvariant()).ToList();
            if (tagsLower.Any(t => t.Contains("legend"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("veteran"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("trusted"))) return "Known User";
            if (tagsLower.Any(t => t.Contains("known"))) return "User";
            if (tagsLower.Any(t => t.Contains("basic"))) return "New User";

            return "Visitor";
        }
    }

    public System.Windows.Media.Brush TrustLevelBrush
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted User" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 43, 226)),
                "Known User" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 123, 0)),
                "User" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)),
                "New User" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                "Visitor" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray)
            };
        }
    }

    public System.Windows.Media.Brush StatusBrush
    {
        get
        {
            return Friend.Status switch
            {
                "active" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)),
                "join me" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                "ask me" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 196, 15)),
                "busy" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)),
                "offline" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray)
            };
        }
    }
}
