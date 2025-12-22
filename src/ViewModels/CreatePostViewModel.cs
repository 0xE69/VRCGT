using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class CreatePostViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _visibility = "public";
    [ObservableProperty] private bool _sendNotification = true;
    [ObservableProperty] private ObservableCollection<string> _availableRoles = new();
    [ObservableProperty] private ObservableCollection<string> _selectedRoles = new();
    public ObservableCollection<string> Visibilities { get; } = new(new[] { "public", "plus", "members" });
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public CreatePostViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    [RelayCommand]
    private async Task LoadRolesAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading roles...";
        AvailableRoles.Clear();
        var roles = await _apiService.GetGroupRolesAsync(groupId);
        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role.RoleId))
            {
                AvailableRoles.Add(role.RoleId);
            }
        }
        Status = roles.Count == 0 ? "No roles found." : "Roles loaded.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Text))
        {
            Status = "Title and text are required.";
            return;
        }

        IsBusy = true;
        Status = "Creating post...";
        var result = await _apiService.CreateGroupPostAsync(groupId, Title, Text, SelectedRoles, Visibility, SendNotification, imageId: null);
        IsBusy = false;

        if (result == null)
        {
            Status = "Post creation failed.";
            return;
        }

        Status = "Post created.";
        Title = string.Empty;
        Text = string.Empty;
        SelectedRoles.Clear();
    }

    [RelayCommand]
    private void ToggleRole(string? roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return;
        }

        if (SelectedRoles.Contains(roleId))
        {
            SelectedRoles.Remove(roleId);
        }
        else
        {
            SelectedRoles.Add(roleId);
        }
    }
}
