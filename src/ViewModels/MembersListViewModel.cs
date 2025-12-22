using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class MembersListViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private ObservableCollection<GroupMember> _members = new();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _roleFilters = new();
    [ObservableProperty] private string _selectedRole = "(All)";

    public MembersListViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    public IEnumerable<GroupMember> FilteredMembers => Members.Where(m =>
        (string.IsNullOrWhiteSpace(Filter) ||
            (m.DisplayName ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
            (m.UserId ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase)) &&
        (SelectedRole == "(All)" || m.RoleIds?.Contains(SelectedRole) == true));

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
        Status = "Loading members...";
        Members.Clear();
        RoleFilters.Clear();
        RoleFilters.Add("(All)");

        var list = await _apiService.GetGroupMembersAsync(groupId, (count, _) =>
        {
            Status = $"Loaded {count} members...";
        });

        foreach (var member in list)
        {
            Members.Add(member);
            if (member.RoleIds != null)
            {
                foreach (var role in member.RoleIds)
                {
                    if (!string.IsNullOrWhiteSpace(role) && !RoleFilters.Contains(role))
                    {
                        RoleFilters.Add(role);
                    }
                }
            }
        }

        Status = list.Count == 0 ? "No members found." : $"Loaded {list.Count} members.";
        IsBusy = false;
        OnPropertyChanged(nameof(FilteredMembers));
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMembers));
    }

    partial void OnSelectedRoleChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMembers));
    }
}
