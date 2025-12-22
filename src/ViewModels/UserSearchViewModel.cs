using System;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class UserSearchViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasSearched;  // True once user has performed at least one search

    [ObservableProperty]
    private bool _isLoadingUser;

    [ObservableProperty]
    private string _statusMessage = "Enter a username or User ID to search";

    [ObservableProperty]
    private ObservableCollection<UserSearchResultDisplay> _searchResults = new();

    [ObservableProperty]
    private UserSearchResultDisplay? _selectedUser;

    [ObservableProperty]
    private UserProfileDisplay? _selectedUserProfile;

    [ObservableProperty]
    private ObservableCollection<GroupRoleDisplay> _groupRoles = new();

    [ObservableProperty]
    private ObservableCollection<GroupRoleDisplay> _userRoles = new();

    [ObservableProperty]
    private GroupRoleDisplay? _selectedRoleToAssign;

    [ObservableProperty]
    private bool _showUserPanel;

    [ObservableProperty]
    private bool _isGroupMember;

    [ObservableProperty]
    private string? _memberJoinedAt;

    public UserSearchViewModel(IVRChatApiService apiService, MainViewModel mainViewModel)
    {
        ArgumentNullException.ThrowIfNull(apiService);
        ArgumentNullException.ThrowIfNull(mainViewModel);
        _apiService = apiService;
        _mainViewModel = mainViewModel;
        Console.WriteLine("[USER-SEARCH] ViewModel initialized");
        Console.WriteLine($"[USER-SEARCH] API Service received: {apiService != null}");
        Console.WriteLine($"[USER-SEARCH] MainViewModel received: {mainViewModel != null}");
        Console.WriteLine($"[USER-SEARCH] Initial state: IsSearching={_isSearching}, HasSearched={_hasSearched}");
    }

    partial void OnSelectedUserChanged(UserSearchResultDisplay? value)
    {
        if (value != null)
        {
            _ = LoadUserProfileAsync(value.UserId);
        }
        else
        {
            ShowUserPanel = false;
            SelectedUserProfile = null;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            Console.WriteLine($"[USER-SEARCH] ========== SEARCH STARTED ==========");
            Console.WriteLine($"[USER-SEARCH] Query: '{SearchQuery}'");
            Console.WriteLine($"[USER-SEARCH] API Service null? {_apiService == null}");
            
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                Console.WriteLine("[USER-SEARCH] Empty query - aborting");
                StatusMessage = "Please enter a username or User ID to search";
                return;
            }

            IsSearching = true;
            HasSearched = true;
            Console.WriteLine($"[USER-SEARCH] IsSearching={IsSearching}, HasSearched={HasSearched}");
            SearchResults.Clear();
            var queryTrimmed = SearchQuery.Trim();
            Console.WriteLine($"[USER-SEARCH] Trimmed query: '{queryTrimmed}'");

            // Check if it's a User ID (starts with "usr_")
            if (queryTrimmed.StartsWith("usr_", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Looking up User ID: {queryTrimmed}...";
                Console.WriteLine($"[USER-SEARCH] Mode: Direct User ID lookup");
                Console.WriteLine($"[USER-SEARCH] Calling API: GetUserAsync({queryTrimmed})");
                
                // Direct user lookup
                var user = await _apiService!.GetUserAsync(queryTrimmed);
                Console.WriteLine($"[USER-SEARCH] API Response: {(user != null ? $"Found '{user.DisplayName}'" : "User not found")}");
                
                if (user != null)
                {
                    SearchResults.Add(new UserSearchResultDisplay
                    {
                        UserId = user.UserId,
                        DisplayName = user.DisplayName,
                        ProfilePicUrl = user.ProfilePicUrl,
                        StatusDescription = user.Bio?.Length > 50 ? user.Bio.Substring(0, 50) + "..." : user.Bio
                    });
                    StatusMessage = $"✓ Found user: {user.DisplayName}";
                }
                else
                {
                    StatusMessage = $"✗ User not found: {queryTrimmed}";
                }
            }
            else
            {
                StatusMessage = $"Searching for \"{queryTrimmed}\"...";
                Console.WriteLine($"[USER-SEARCH] Mode: Name/username search");
                Console.WriteLine($"[USER-SEARCH] Calling API: SearchUsersAsync({queryTrimmed})");
                
                // Search by name
                var results = await _apiService!.SearchUsersAsync(queryTrimmed);
                Console.WriteLine($"[USER-SEARCH] API Response: {results.Count} users found");
                foreach (var r in results.Take(5))
                {
                    Console.WriteLine($"[USER-SEARCH]   - {r.DisplayName} ({r.UserId})");
                }
                if (results.Count > 5) Console.WriteLine($"[USER-SEARCH]   ... and {results.Count - 5} more");
                
                foreach (var result in results)
                {
                    SearchResults.Add(new UserSearchResultDisplay
                    {
                        UserId = result.UserId,
                        DisplayName = result.DisplayName,
                        ProfilePicUrl = result.ProfilePicUrl,
                        StatusDescription = result.StatusDescription
                    });
                }
                
                if (SearchResults.Count > 0)
                {
                    StatusMessage = $"✓ Found {SearchResults.Count} user{(SearchResults.Count == 1 ? "" : "s")} matching \"{queryTrimmed}\"";
                }
                else
                {
                    StatusMessage = $"✗ No users found matching \"{queryTrimmed}\"";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Search error: {ex.Message}";
            Console.WriteLine($"[USER-SEARCH] ERROR: {ex.Message}");
            Console.WriteLine($"[USER-SEARCH] Stack: {ex.StackTrace}");
        }
        finally
        {
            IsSearching = false;
            Console.WriteLine($"[USER-SEARCH] ========== SEARCH COMPLETE ==========");
            Console.WriteLine($"[USER-SEARCH] Results count: {SearchResults.Count}");
            Console.WriteLine($"[USER-SEARCH] Status: {StatusMessage}");
            Console.WriteLine($"[USER-SEARCH] IsSearching={IsSearching}, HasSearched={HasSearched}");
        }
    }

    private async Task LoadUserProfileAsync(string userId)
    {
        Console.WriteLine($"[USER-SEARCH] Loading profile for: {userId}");
        IsLoadingUser = true;
        ShowUserPanel = true;
        IsGroupMember = false;
        UserRoles.Clear();

        try
        {
            // Load user details
            Console.WriteLine($"[USER-SEARCH] Calling API: GetUserAsync({userId})");
            var user = await _apiService.GetUserAsync(userId);
            if (user != null)
            {
                SelectedUserProfile = new UserProfileDisplay
                {
                    UserId = user.UserId,
                    DisplayName = user.DisplayName,
                    Bio = user.Bio ?? "No bio",
                    ProfilePicUrl = user.ProfilePicUrl,
                    IsAgeVerified = user.IsAgeVerified,
                    Tags = user.Tags
                };

                // Check if user is in the group (using the GroupId from MainViewModel)
                var groupId = _mainViewModel.GroupId;
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    // Load group roles if not already loaded
                    if (GroupRoles.Count == 0)
                    {
                        var roles = await _apiService.GetGroupRolesAsync(groupId);
                        GroupRoles = new ObservableCollection<GroupRoleDisplay>(
                            roles.Select(r => new GroupRoleDisplay
                            {
                                RoleId = r.RoleId,
                                Name = r.Name,
                                Description = r.Description,
                                Permissions = r.Permissions
                            }));
                    }

                    // Check group membership
                    var memberInfo = await _apiService.GetGroupMemberAsync(groupId, userId);
                    if (memberInfo != null)
                    {
                        IsGroupMember = true;
                        MemberJoinedAt = memberInfo.JoinedAt;
                        UserRoles = new ObservableCollection<GroupRoleDisplay>(
                            GroupRoles.Where(r => memberInfo.RoleIds.Contains(r.RoleId)));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadUserProfile: {ex}");
        }
        finally
        {
            IsLoadingUser = false;
        }
    }

    [RelayCommand]
    private async Task AssignRoleAsync()
    {
        if (SelectedUserProfile == null || SelectedRoleToAssign == null)
        {
            StatusMessage = "Select a user and role first";
            return;
        }

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusMessage = "Please set a Group ID in the sidebar first";
            return;
        }

        IsLoadingUser = true;
        try
        {
            var success = await _apiService.AssignGroupRoleAsync(groupId, SelectedUserProfile.UserId, SelectedRoleToAssign.RoleId);
            if (success)
            {
                StatusMessage = $"Assigned {SelectedRoleToAssign.Name} to {SelectedUserProfile.DisplayName}";
                if (!UserRoles.Any(r => r.RoleId == SelectedRoleToAssign.RoleId))
                {
                    UserRoles.Add(SelectedRoleToAssign);
                }
            }
            else
            {
                StatusMessage = "Failed to assign role";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingUser = false;
        }
    }

    [RelayCommand]
    private async Task RemoveRoleAsync(GroupRoleDisplay role)
    {
        if (SelectedUserProfile == null || role == null) return;

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;

        IsLoadingUser = true;
        try
        {
            var success = await _apiService.RemoveGroupRoleAsync(groupId, SelectedUserProfile.UserId, role.RoleId);
            if (success)
            {
                StatusMessage = $"Removed {role.Name} from {SelectedUserProfile.DisplayName}";
                UserRoles.Remove(role);
            }
            else
            {
                StatusMessage = "Failed to remove role";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingUser = false;
        }
    }

    [RelayCommand]
    private async Task KickUserAsync()
    {
        if (SelectedUserProfile == null) return;

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusMessage = "Please set a Group ID in the sidebar first";
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to kick {SelectedUserProfile.DisplayName} from the group?",
            "Confirm Kick",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsLoadingUser = true;
        try
        {
            var success = await _apiService.KickGroupMemberAsync(groupId, SelectedUserProfile.UserId);
            if (success)
            {
                StatusMessage = $"Kicked {SelectedUserProfile.DisplayName} from the group";
                IsGroupMember = false;
                UserRoles.Clear();
            }
            else
            {
                StatusMessage = "Failed to kick user";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingUser = false;
        }
    }

    [RelayCommand]
    private async Task BanUserAsync()
    {
        if (SelectedUserProfile == null) return;

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusMessage = "Please set a Group ID in the sidebar first";
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to BAN {SelectedUserProfile.DisplayName} from the group?\n\nThis will prevent them from rejoining.",
            "Confirm Ban",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsLoadingUser = true;
        try
        {
            var success = await _apiService.BanGroupMemberAsync(groupId, SelectedUserProfile.UserId);
            if (success)
            {
                StatusMessage = $"Banned {SelectedUserProfile.DisplayName} from the group";
                IsGroupMember = false;
                UserRoles.Clear();
            }
            else
            {
                StatusMessage = "Failed to ban user";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingUser = false;
        }
    }

    [RelayCommand]
    private void CloseUserPanel()
    {
        ShowUserPanel = false;
        SelectedUser = null;
        SelectedUserProfile = null;
    }
}

public class UserSearchResultDisplay
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? ProfilePicUrl { get; set; }
    public string? StatusDescription { get; set; }
}

public class UserProfileDisplay
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";
    public string? ProfilePicUrl { get; set; }
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class GroupRoleDisplay
{
    public string RoleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
}
