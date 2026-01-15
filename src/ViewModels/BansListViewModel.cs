using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;
using VRCGroupTools.Helpers;
using VRCGroupTools.Models;

namespace VRCGroupTools.ViewModels;

public partial class BansListViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;
    private readonly IModerationService _moderationService;
    private readonly IDiscordWebhookService _discordService;
    private readonly ICacheService _cacheService;

    [ObservableProperty] private ObservableCollection<GroupBanEntry> _bans = new();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _showInstanceBans = true;
    [ObservableProperty] private bool _showGroupBans = true;
    [ObservableProperty] private bool _showMemberBans = true;
    [ObservableProperty] private bool _showNonMemberBans = true;
    
    // User search for banning
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<UserSearchResult> _searchResults = new();
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _banReason = string.Empty;
    [ObservableProperty] private UserSearchResult? _selectedSearchResult;

    public BansListViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _moderationService = (App.Services.GetService(typeof(IModerationService)) as IModerationService)!;
        _discordService = (App.Services.GetService(typeof(IDiscordWebhookService)) as IDiscordWebhookService)!;
        _cacheService = App.Services.GetRequiredService<ICacheService>();
    }

    public IEnumerable<GroupBanEntry> FilteredBans
    {
        get
        {
            var filtered = Bans.AsEnumerable();
            
            // Filter by ban type
            if (!ShowInstanceBans || !ShowGroupBans)
            {
                filtered = filtered.Where(b =>
                    (b.IsInstanceBan && ShowInstanceBans) ||
                    (!b.IsInstanceBan && ShowGroupBans));
            }
            
            // Filter by membership status
            if (!ShowMemberBans || !ShowNonMemberBans)
            {
                filtered = filtered.Where(b =>
                    (b.WasMember && ShowMemberBans) ||
                    (!b.WasMember && ShowNonMemberBans));
            }
            
            // Text filter
            if (!string.IsNullOrWhiteSpace(Filter))
            {
                filtered = filtered.Where(b =>
                    (b.DisplayName ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                    (b.UserId ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase));
            }
            
            return filtered;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading bans...";
        Bans.Clear();

        var list = await _apiService.GetGroupBansAsync(groupId, (count, _) =>
        {
            Status = $"Loaded {count} bans...";
        });

        foreach (var ban in list)
        {
            Bans.Add(ban);
        }

        await _cacheService.SaveAsync($"group_bans_{groupId}", list);

        Status = list.Count == 0 ? "No bans found." : $"Loaded {list.Count} bans.";
        IsBusy = false;
        OnPropertyChanged(nameof(FilteredBans));
    }

    [RelayCommand]
    private async Task LoadFromCacheAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        Status = "Loading cached bans...";
        var cached = await _cacheService.LoadAsync<List<GroupBanEntry>>($"group_bans_{groupId}");
        if (cached == null || cached.Count == 0)
        {
            Status = "No cached bans found.";
            return;
        }

        Bans.Clear();
        foreach (var ban in cached)
        {
            Bans.Add(ban);
        }

        Status = $"Loaded {cached.Count} cached bans.";
        OnPropertyChanged(nameof(FilteredBans));
    }

    [RelayCommand]
    private async Task UnbanAsync(GroupBanEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        Status = $"Unbanning {entry.DisplayName}...";
        var success = await _apiService.UnbanGroupMemberAsync(groupId, entry.UserId);
        if (success)
        {
            Bans.Remove(entry);
            Status = "User unbanned.";
            OnPropertyChanged(nameof(FilteredBans));
        }
        else
        {
            Status = "Failed to unban user.";
        }
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredBans));
    }

    partial void OnShowInstanceBansChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredBans));
    }

    partial void OnShowGroupBansChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredBans));
    }

    partial void OnShowMemberBansChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredBans));
    }

    partial void OnShowNonMemberBansChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredBans));
    }

    [RelayCommand]
    private async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 3)
        {
            Status = "Enter at least 3 characters to search.";
            return;
        }

        IsSearching = true;
        Status = $"Searching for '{SearchQuery}'...";
        SearchResults.Clear();

        var results = await _apiService.SearchUsersAsync(SearchQuery);
        
        foreach (var user in results)
        {
            SearchResults.Add(user);
        }

        Status = results.Count == 0 ? "No users found." : $"Found {results.Count} users.";
        IsSearching = false;
    }

    [RelayCommand]
    private async Task BanSelectedUserAsync()
    {
        if (SelectedSearchResult == null)
        {
            Status = "Select a user to ban.";
            return;
        }

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        Status = $"Preparing to ban {SelectedSearchResult.DisplayName}...";
        
        // Show moderation dialog to get reason, duration, and description
        var request = await ModerationDialogHelper.ShowModerationDialogAsync(
            "ban",
            groupId,
            SelectedSearchResult.UserId,
            SelectedSearchResult.DisplayName);

        if (request == null)
        {
            Status = "Ban cancelled.";
            return;
        }

        Status = $"Banning {SelectedSearchResult.DisplayName}...";
        var success = await ModerationDialogHelper.ExecuteModerationActionAsync(
            groupId,
            request,
            _apiService,
            _moderationService);
        
        if (success)
        {
            Status = $"✅ {SelectedSearchResult.DisplayName} has been banned.";
            SearchResults.Clear();
            SearchQuery = string.Empty;
            BanReason = string.Empty;
            SelectedSearchResult = null;
            
            // Refresh the bans list
            await RefreshAsync();
        }
        else
        {
            Status = "❌ Failed to ban user. Check permissions.";
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        BanReason = string.Empty;
        SelectedSearchResult = null;
        Status = string.Empty;
    }

    [RelayCommand]
    private void SelectSearchResult(UserSearchResult? result)
    {
        SelectedSearchResult = result;
        if (result != null)
        {
            Status = $"Selected: {result.DisplayName}. Enter ban reason and click 'Ban User'.";
        }
    }
}
