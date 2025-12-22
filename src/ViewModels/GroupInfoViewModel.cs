using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class GroupInfoViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _shortCode = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _privacy = string.Empty;
    [ObservableProperty] private string _ownerId = string.Empty;
    [ObservableProperty] private string _iconUrl = string.Empty;
    [ObservableProperty] private string _bannerUrl = string.Empty;
    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public GroupInfoViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
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
        Status = "Loading group info...";
        var info = await _apiService.GetGroupAsync(groupId);
        IsBusy = false;

        if (info == null)
        {
            Status = "Failed to load group info.";
            return;
        }

        Name = info.Name;
        ShortCode = info.ShortCode ?? string.Empty;
        Description = info.Description ?? string.Empty;
        Privacy = info.Privacy ?? string.Empty;
        OwnerId = info.OwnerId ?? string.Empty;
        IconUrl = info.IconUrl ?? string.Empty;
        BannerUrl = info.BannerUrl ?? string.Empty;
        MemberCount = info.MemberCount;
        OnlineCount = info.OnlineCount;
        Status = "Group info updated.";
    }
}
