using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class InviteToGroupViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _userId = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<UserSearchResult> _searchResults = new();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    
    // Profile panel properties
    [ObservableProperty] private bool _showUserPanel;
    [ObservableProperty] private bool _isLoadingProfile;
    [ObservableProperty] private UserDetails? _selectedUserProfile;
    [ObservableProperty] private bool _hasBadges;
    
    public bool HasResults => SearchResults.Count > 0;

    public InviteToGroupViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    partial void OnSearchResultsChanged(ObservableCollection<UserSearchResult> value)
    {
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        Console.WriteLine($"[INVITE] SearchAsync called with query: '{query}'");
        
        if (string.IsNullOrWhiteSpace(query))
        {
            Status = "Enter a name or userId to search.";
            return;
        }

        IsBusy = true;
        Status = "Searching users...";
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasResults));
        
        try
        {
            Console.WriteLine($"[INVITE] Calling API SearchUsersAsync...");
            var results = await _apiService.SearchUsersAsync(query);
            Console.WriteLine($"[INVITE] Got {results.Count} results");
            
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }
            OnPropertyChanged(nameof(HasResults));
            Status = SearchResults.Count == 0 ? "No users found." : $"Found {SearchResults.Count} users.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INVITE] Search error: {ex.Message}");
            Status = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ViewUserProfileAsync(UserSearchResult? user)
    {
        if (user == null) return;
        
        ShowUserPanel = true;
        IsLoadingProfile = true;
        SelectedUserProfile = null;
        HasBadges = false;
        
        try
        {
            var profile = await _apiService.GetUserAsync(user.UserId);
            if (profile != null)
            {
                SelectedUserProfile = profile;
                HasBadges = profile.Badges?.Count > 0;
            }
            else
            {
                Status = "Failed to load user profile.";
                ShowUserPanel = false;
            }
        }
        catch (Exception ex)
        {
            Status = $"Error loading profile: {ex.Message}";
            ShowUserPanel = false;
        }
        finally
        {
            IsLoadingProfile = false;
        }
    }

    [RelayCommand]
    private void CloseUserPanel()
    {
        ShowUserPanel = false;
        SelectedUserProfile = null;
    }

    [RelayCommand]
    private void UseUser(UserSearchResult? user)
    {
        if (user == null)
        {
            return;
        }
        UserId = user.UserId;
        SearchText = string.Empty;
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasResults));
        Status = $"Selected {user.DisplayName}";
    }

    [RelayCommand]
    private async Task SendInviteAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(UserId))
        {
            Status = "Enter a userId to invite.";
            return;
        }

        IsBusy = true;
        Status = "Sending invite...";
        var ok = await _apiService.SendGroupInviteAsync(groupId, UserId.Trim());
        IsBusy = false;
        Status = ok ? "✓ Invite sent successfully!" : "✗ Invite failed.";
    }

    [RelayCommand]
    private async Task SendInviteToSelectedUserAsync()
    {
        if (SelectedUserProfile == null)
        {
            Status = "No user selected.";
            return;
        }

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = $"Sending invite to {SelectedUserProfile.DisplayName}...";
        var ok = await _apiService.SendGroupInviteAsync(groupId, SelectedUserProfile.UserId);
        IsBusy = false;
        Status = ok ? $"✓ Invite sent to {SelectedUserProfile.DisplayName}!" : "✗ Invite failed.";
    }
}
