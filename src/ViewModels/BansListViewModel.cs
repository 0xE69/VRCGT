using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class BansListViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private ObservableCollection<GroupBanEntry> _bans = new();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filter = string.Empty;

    public BansListViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    public IEnumerable<GroupBanEntry> FilteredBans => string.IsNullOrWhiteSpace(Filter)
        ? Bans
        : Bans.Where(b =>
            (b.DisplayName ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
            (b.UserId ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase));

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

        Status = list.Count == 0 ? "No bans found." : $"Loaded {list.Count} bans.";
        IsBusy = false;
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
}
