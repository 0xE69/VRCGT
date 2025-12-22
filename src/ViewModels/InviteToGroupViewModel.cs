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

    public InviteToGroupViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            Status = "Enter a name or userId to search.";
            return;
        }

        IsBusy = true;
        Status = "Searching users...";
        SearchResults.Clear();
        var results = await _apiService.SearchUsersAsync(query);
        foreach (var r in results)
        {
            SearchResults.Add(r);
        }
        Status = SearchResults.Count == 0 ? "No users found." : $"Found {SearchResults.Count} users.";
        IsBusy = false;
    }

    [RelayCommand]
    private void UseUser(UserSearchResult? user)
    {
        if (user == null)
        {
            return;
        }
        UserId = user.UserId;
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
        Status = ok ? "Invite sent." : "Invite failed.";
    }
}
