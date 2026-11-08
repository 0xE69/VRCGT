using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Data;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class FriendInviterViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _dbService;
    private readonly ICacheService _cacheService;
    private readonly CurrentInstanceInviteWorkflow _instanceInviteWorkflow;

    [ObservableProperty]
    private bool _isInvitingInstance;

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
    private bool _hideInGroup;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private int _filteredCount;

    public FriendInviterViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService,
        IDatabaseService dbService,
        ICacheService cacheService,
        CurrentInstanceInviteWorkflow instanceInviteWorkflow)
    {
        _instanceInviteWorkflow = instanceInviteWorkflow;
        _apiService = apiService;
        _settingsService = settingsService;
        _dbService = dbService;
        _cacheService = cacheService;
        
        _ = LoadFriendsAsync();
    }

    public bool CanManageFriends => !IsLoading && !IsInvitingInstance;
    public bool CanInviteCurrentInstance => CanManageFriends && SelectedCount > 0;

    partial void OnIsLoadingChanged(bool value) => RefreshActionState();
    partial void OnIsInvitingInstanceChanged(bool value) => RefreshActionState();
    partial void OnSelectedCountChanged(int value) => RefreshActionState();

    private void RefreshActionState()
    {
        OnPropertyChanged(nameof(CanManageFriends));
        OnPropertyChanged(nameof(CanInviteCurrentInstance));
        RefreshCommand.NotifyCanExecuteChanged();
        SyncMembersCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        DeselectAllCommand.NotifyCanExecuteChanged();
        InviteToGroupCommand.NotifyCanExecuteChanged();
        InviteToCurrentInstanceCommand.NotifyCanExecuteChanged();
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

    partial void OnHideInGroupChanged(bool value)
    {
        ApplyFilters();
    }

    [RelayCommand(CanExecute = nameof(CanManageFriends))]
    private async Task RefreshAsync()
    {
        Console.WriteLine("[FRIEND-MANAGER] RefreshAsync called");
        await LoadFriendsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanManageFriends))]
    private async Task SyncMembersAsync()
    {
        if (!CanManageFriends) return;

        var currentGroupId = _settingsService.CurrentGroupId;
        if (string.IsNullOrEmpty(currentGroupId))
        {
            StatusMessage = "No group selected";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Syncing group members...";
            Console.WriteLine($"[FRIEND-MANAGER] Starting member sync for group {currentGroupId}");

            var members = await _apiService.GetGroupMembersAsync(currentGroupId, (count, _) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"Syncing members... ({count})";
                });
            });

            // Save to cache
            await _cacheService.SaveAsync($"group_members_{currentGroupId}", members);
            Console.WriteLine($"[FRIEND-MANAGER] Cached {members.Count} members");

            StatusMessage = $"Synced {members.Count} members! Refreshing...";

            // Now reload friends with updated cache
            await LoadFriendsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FRIEND-MANAGER] Sync error: {ex.Message}");
            StatusMessage = $"Sync failed: {ex.Message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MessageBox.Show($"Failed to sync members: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFriendsAsync()
    {
        Console.WriteLine("[FRIEND-MANAGER] LoadFriendsAsync called");
        if (!CanManageFriends)
        {
            Console.WriteLine("[FRIEND-MANAGER] Already loading, skipping");
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Loading friends...";
            Console.WriteLine("[FRIEND-MANAGER] Starting to load friends...");

            // Clear on UI thread
            Application.Current?.Dispatcher?.Invoke(() => Friends.Clear());

            // Get current group ID for membership check
            var currentGroupId = _settingsService.CurrentGroupId;
            HashSet<string> groupMemberIds = new();
            
            // Try to get CACHED group members for quick "In Group" check
            // First try JSON cache (from Member List tab), then try database
            if (!string.IsNullOrEmpty(currentGroupId))
            {
                try
                {
                    // First try JSON cache (populated by Member List tab)
                    var jsonCachedMembers = await _cacheService.LoadAsync<List<GroupMember>>($"group_members_{currentGroupId}");
                    if (jsonCachedMembers != null && jsonCachedMembers.Count > 0)
                    {
                        groupMemberIds = jsonCachedMembers.Select(m => m.UserId).ToHashSet();
                        Console.WriteLine($"[FRIEND-MANAGER] Loaded {groupMemberIds.Count} members from JSON cache");
                    }
                    else
                    {
                        // Fallback to database
                        var cachedMembers = await _dbService.GetGroupMembersAsync(currentGroupId);
                        if (cachedMembers.Count > 0)
                        {
                            groupMemberIds = cachedMembers.Select(m => m.UserId).ToHashSet();
                            Console.WriteLine($"[FRIEND-MANAGER] Loaded {groupMemberIds.Count} members from database");
                        }
                        else
                        {
                            Console.WriteLine($"[FRIEND-MANAGER] No cached members found. Use Member List tab to sync members first for 'In Group' badges.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FRIEND-MANAGER] Could not load cached group members: {ex.Message}");
                }
            }

            var allFriends = new List<FriendInfo>();
            int offset = 0;
            int n = 100;
            
            while (true)
            {
                StatusMessage = $"Loading friends... ({allFriends.Count})";
                
                // Load ALL friends (offline=false means get everyone including offline)
                var friendsList = await _apiService.GetFriendsAsync(false, n, offset);
                
                if (friendsList.Count == 0)
                    break;
                
                allFriends.AddRange(friendsList);
                
                if (friendsList.Count < n)
                    break;
                
                offset += n;

                // Safety limit to prevent infinite loops if API behaves weirdly
                if (allFriends.Count >= 5000)
                    break;
            }

            // Add all friends on UI thread
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                int inGroupCount = 0;
                foreach (var friend in allFriends)
                {
                    var isInGroup = groupMemberIds.Contains(friend.UserId);
                    if (isInGroup) inGroupCount++;
                    Friends.Add(new FriendInviterItemViewModel(friend, this, isInGroup));
                }

                Console.WriteLine($"[FRIEND-MANAGER] Loaded {Friends.Count} friends, {inGroupCount} are in group, applying filters...");
                ApplyFilters();
                UpdateSelectedCount();
                Console.WriteLine($"[FRIEND-MANAGER] After filtering: {FilteredFriends.Count} friends visible");
                StatusMessage = $"Loaded {Friends.Count} friends";
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FRIEND-MANAGER] Error: {ex.Message}");
            StatusMessage = $"Error loading friends: {ex.Message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MessageBox.Show($"Failed to load friends: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            IsLoading = false;
            Console.WriteLine("[FRIEND-MANAGER] LoadFriendsAsync completed");
        }
    }

    private void ApplyFilters()
    {
        // Ensure we run on UI thread for proper binding updates
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ApplyFilters);
            return;
        }

        FilteredFriends.Clear();

        var query = SearchQuery?.ToLowerInvariant() ?? string.Empty;

        var filtered = Friends.Where(f =>
        {
            // Apply 18+ filter
            if (Only18Plus && !f.Friend.IsAgeVerified)
                return false;

            // Apply "hide in group" filter
            if (HideInGroup && f.IsInGroup)
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
        
        FilteredCount = FilteredFriends.Count;
        Console.WriteLine($"[FRIEND-MANAGER] FilteredCount updated to: {FilteredCount}, on UI thread: {Application.Current?.Dispatcher?.CheckAccess()}");
    }

    [RelayCommand(CanExecute = nameof(CanManageFriends))]
    private void SelectAll()
    {
        foreach (var friend in FilteredFriends)
        {
            friend.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand(CanExecute = nameof(CanManageFriends))]
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

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanInviteCurrentInstance))]
    private async Task InviteToCurrentInstanceAsync(System.Threading.CancellationToken cancellationToken)
    {
        if (IsLoading || IsInvitingInstance) return;
        var selected = Friends.Where(f => f.IsSelected).ToArray();
        try
        {
            IsInvitingInstance = true;
            StatusMessage = "Preparing current-instance invites...";
            var report = await _instanceInviteWorkflow.RunAsync(selected.Select(f => f.Friend.UserId), cancellationToken);
            foreach (var outcome in report.Outcomes)
            {
                foreach (var friend in selected.Where(f => f.Friend.UserId == outcome.UserId))
                {
                    friend.InstanceInviteResult = outcome.Status switch
                    {
                        InstanceInviteStatus.Unknown => "Unknown (check VRChat before retrying)",
                        InstanceInviteStatus.NotSent => "Not sent",
                        _ => outcome.Status.ToString()
                    };
                    if (outcome.Status is InstanceInviteStatus.Sent or InstanceInviteStatus.Unknown) friend.IsSelected = false;
                }
            }
            StatusMessage = $"{report.Outcomes.Count(o => o.Status == InstanceInviteStatus.Sent)} sent, " +
                $"{report.Outcomes.Count(o => o.Status == InstanceInviteStatus.Failed)} failed, " +
                $"{report.Outcomes.Count(o => o.Status == InstanceInviteStatus.Unknown)} unknown, " +
                $"{report.Outcomes.Count(o => o.Status == InstanceInviteStatus.NotSent)} not sent. {report.Message}";
        }
        finally
        {
            IsInvitingInstance = false;
            UpdateSelectedCount();
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageFriends))]
    private async Task InviteToGroupAsync()
    {
        if (!CanManageFriends) return;
        var selectedFriends = Friends.Where(f => f.IsSelected).ToList();
        if (selectedFriends.Count == 0)
        {
            MessageBox.Show("Please select at least one friend to invite.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Pin the explicitly selected group for this batch.
        var groupId = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
        if (string.IsNullOrEmpty(groupId))
        {
            MessageBox.Show("No group configured. Please set up a group first.", "No Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!GroupInvitePresentation.Confirm(groupId, selectedFriends.Select(f => $"{f.Friend.DisplayName} ({f.Friend.UserId})"))) return;
        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;
            var suppressedCount = 0;

            foreach (var friendVM in selectedFriends)
            {
                StatusMessage = $"Inviting {friendVM.Friend.DisplayName} to group...";

                // Invite to group using group ID
                var outcome = await _apiService.SendTrackedAsync(groupId, friendVM.Friend.UserId, () => (_settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId) == groupId);
                friendVM.GroupInviteResult = outcome.Message;
                if (!outcome.Sent && outcome.State != GroupInviteState.Failed) { suppressedCount++; friendVM.IsSelected = false; continue; }
                var success = outcome.Sent;

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

            StatusMessage = $"Invites: {successCount} sent, {failCount} rejected, {suppressedCount} suppressed/unknown. See row status and Game Log history.";
            MessageBox.Show(StatusMessage, "Group invitations", MessageBoxButton.OK, MessageBoxImage.Information);
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

    [ObservableProperty]
    private bool _isInGroup;

    public FriendInfo Friend { get; }

    [ObservableProperty]
    private string? _instanceInviteResult;

    [ObservableProperty] private string? _groupInviteResult;

    public FriendInviterItemViewModel(FriendInfo friend, FriendInviterViewModel? parent, bool isInGroup = false)
    {
        Friend = friend;
        _parent = parent;
        _isInGroup = isInGroup;
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
