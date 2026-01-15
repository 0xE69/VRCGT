using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class GroupInfoViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDiscordPresenceService _discordPresence;
    private readonly ICacheService _cacheService;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _groupId = string.Empty;
    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private string _createdAt = string.Empty;
    [ObservableProperty] private string _joinedAt = string.Empty;
    [ObservableProperty] private string _groupUrl = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _iconUrl = string.Empty;
    [ObservableProperty] private string _bannerUrl = string.Empty;

    public GroupInfoViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _discordPresence = App.Services.GetRequiredService<IDiscordPresenceService>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
    }

    /// <summary>
    /// Atomically applies all group data properties on the UI thread.
    /// </summary>
    public void ApplyGroupData(
        string name,
        string groupId,
        int memberCount,
        int onlineCount,
        string createdAt,
        string joinedAt,
        string groupUrl,
        string description,
        string iconUrl,
        string bannerUrl)
    {
        Name = name;
        GroupId = groupId;
        MemberCount = memberCount;
        OnlineCount = onlineCount;
        CreatedAt = createdAt;
        JoinedAt = joinedAt;
        GroupUrl = groupUrl;
        Description = description;
        IconUrl = iconUrl;
        BannerUrl = bannerUrl;
        ErrorMessage = string.Empty;

        LoggingService.Info("GroupInfo", $"ApplyGroupData: Name='{name}', Members={memberCount}, Online={onlineCount}, Url='{groupUrl}'");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            ErrorMessage = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            LoggingService.Debug("GroupInfo", $"Fetching group info for {groupId}");

            var info = await _apiService.GetGroupAsync(groupId);
            if (info == null)
            {
                ErrorMessage = "Failed to load group info (null response).";
                return;
            }

            var createdAt = info.CreatedAt?.ToLocalTime().ToString("g") ?? "Unknown";
            var groupUrl = $"https://vrchat.com/home/group/{info.Id}";

            var joinedAt = "Not in group";
            if (!string.IsNullOrWhiteSpace(_apiService.CurrentUserId))
            {
                var memberInfo = await _apiService.GetGroupMemberAsync(groupId, _apiService.CurrentUserId);
                if (memberInfo != null)
                {
                    joinedAt = string.IsNullOrWhiteSpace(memberInfo.JoinedAt)
                        ? "Member"
                        : memberInfo.JoinedAt;
                }
            }

            // Apply all data atomically on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyGroupData(
                    name: info.Name ?? string.Empty,
                    groupId: info.Id ?? string.Empty,
                    memberCount: info.MemberCount,
                    onlineCount: info.OnlineCount,
                    createdAt: createdAt,
                    joinedAt: joinedAt,
                    groupUrl: groupUrl,
                    description: info.Description ?? string.Empty,
                    iconUrl: info.IconUrl ?? string.Empty,
                    bannerUrl: info.BannerUrl ?? string.Empty);
            });

            await _cacheService.SaveAsync($"group_info_{groupId}", new GroupInfoCache
            {
                Name = info.Name ?? string.Empty,
                GroupId = info.Id ?? string.Empty,
                MemberCount = info.MemberCount,
                OnlineCount = info.OnlineCount,
                CreatedAt = createdAt,
                JoinedAt = joinedAt,
                GroupUrl = groupUrl,
                Description = info.Description ?? string.Empty,
                IconUrl = info.IconUrl ?? string.Empty,
                BannerUrl = info.BannerUrl ?? string.Empty
            });

            _discordPresence.UpdateGroupPresence(info.Name ?? string.Empty, info.Id ?? string.Empty, info.MemberCount, info.OnlineCount);
            LoggingService.Info("GroupInfo", "Refresh complete");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            _discordPresence.ClearPresence();
            LoggingService.Error("GroupInfo", ex, "RefreshAsync failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadFromCacheAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            ErrorMessage = "Set a Group ID first.";
            return;
        }

        ErrorMessage = string.Empty;
        var cached = await _cacheService.LoadAsync<GroupInfoCache>($"group_info_{groupId}");
        if (cached == null)
        {
            ErrorMessage = "No cached group info found.";
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ApplyGroupData(
                name: cached.Name,
                groupId: cached.GroupId,
                memberCount: cached.MemberCount,
                onlineCount: cached.OnlineCount,
                createdAt: cached.CreatedAt,
                joinedAt: cached.JoinedAt,
                groupUrl: cached.GroupUrl,
                description: cached.Description,
                iconUrl: cached.IconUrl,
                bannerUrl: cached.BannerUrl);
        });
    }

    private class GroupInfoCache
    {
        public string Name { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int OnlineCount { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string JoinedAt { get; set; } = string.Empty;
        public string GroupUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string BannerUrl { get; set; } = string.Empty;
    }
}
