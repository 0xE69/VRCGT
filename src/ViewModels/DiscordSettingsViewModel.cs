using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class DiscordSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDiscordWebhookService _discordService;

    [ObservableProperty]
    private string _webhookUrl = "";

    // User Events
    [ObservableProperty]
    private bool _notifyUserJoins;

    [ObservableProperty]
    private bool _notifyUserLeaves;

    [ObservableProperty]
    private bool _notifyUserKicked;

    [ObservableProperty]
    private bool _notifyUserBanned;

    [ObservableProperty]
    private bool _notifyUserUnbanned;

    [ObservableProperty]
    private bool _notifyUserRoleAdd;

    [ObservableProperty]
    private bool _notifyUserRoleRemove;

    // Role Events
    [ObservableProperty]
    private bool _notifyRoleCreate;

    [ObservableProperty]
    private bool _notifyRoleUpdate;

    [ObservableProperty]
    private bool _notifyRoleDelete;

    // Instance Events
    [ObservableProperty]
    private bool _notifyInstanceCreate;

    [ObservableProperty]
    private bool _notifyInstanceDelete;

    [ObservableProperty]
    private bool _notifyInstanceOpened;

    [ObservableProperty]
    private bool _notifyInstanceClosed;

    // Group Events
    [ObservableProperty]
    private bool _notifyGroupUpdate;

    // Invite Events
    [ObservableProperty]
    private bool _notifyInviteCreate;

    [ObservableProperty]
    private bool _notifyInviteAccept;

    [ObservableProperty]
    private bool _notifyInviteReject;

    [ObservableProperty]
    private bool _notifyJoinRequests;

    // Announcement Events
    [ObservableProperty]
    private bool _notifyAnnouncementCreate;

    [ObservableProperty]
    private bool _notifyAnnouncementDelete;

    // Gallery Events
    [ObservableProperty]
    private bool _notifyGalleryCreate;

    [ObservableProperty]
    private bool _notifyGalleryDelete;

    // Post Events
    [ObservableProperty]
    private bool _notifyPostCreate;

    [ObservableProperty]
    private bool _notifyPostDelete;

    [ObservableProperty]
    private bool _presenceEnabled;

    [ObservableProperty]
    private string _presenceAppId = "";

    [ObservableProperty]
    private bool _presenceShowRepoButton = true;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isWebhookValid;

    public DiscordSettingsViewModel()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _discordService = App.Services.GetRequiredService<IDiscordWebhookService>();

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        WebhookUrl = settings.DiscordWebhookUrl ?? "";
        
        // User Events
        NotifyUserJoins = settings.DiscordNotifyUserJoins;
        NotifyUserLeaves = settings.DiscordNotifyUserLeaves;
        NotifyUserKicked = settings.DiscordNotifyUserKicked;
        NotifyUserBanned = settings.DiscordNotifyUserBanned;
        NotifyUserUnbanned = settings.DiscordNotifyUserUnbanned;
        NotifyUserRoleAdd = settings.DiscordNotifyUserRoleAdd;
        NotifyUserRoleRemove = settings.DiscordNotifyUserRoleRemove;
        
        // Role Events
        NotifyRoleCreate = settings.DiscordNotifyRoleCreate;
        NotifyRoleUpdate = settings.DiscordNotifyRoleUpdate;
        NotifyRoleDelete = settings.DiscordNotifyRoleDelete;
        
        // Instance Events
        NotifyInstanceCreate = settings.DiscordNotifyInstanceCreate;
        NotifyInstanceDelete = settings.DiscordNotifyInstanceDelete;
        NotifyInstanceOpened = settings.DiscordNotifyInstanceOpened;
        NotifyInstanceClosed = settings.DiscordNotifyInstanceClosed;
        
        // Group Events
        NotifyGroupUpdate = settings.DiscordNotifyGroupUpdate;
        
        // Invite Events
        NotifyInviteCreate = settings.DiscordNotifyInviteCreate;
        NotifyInviteAccept = settings.DiscordNotifyInviteAccept;
        NotifyInviteReject = settings.DiscordNotifyInviteReject;
        NotifyJoinRequests = settings.DiscordNotifyJoinRequests;
        
        // Announcement Events
        NotifyAnnouncementCreate = settings.DiscordNotifyAnnouncementCreate;
        NotifyAnnouncementDelete = settings.DiscordNotifyAnnouncementDelete;
        
        // Gallery Events
        NotifyGalleryCreate = settings.DiscordNotifyGalleryCreate;
        NotifyGalleryDelete = settings.DiscordNotifyGalleryDelete;
        
        // Post Events
        NotifyPostCreate = settings.DiscordNotifyPostCreate;
        NotifyPostDelete = settings.DiscordNotifyPostDelete;
        
        // Discord Presence
        PresenceEnabled = settings.DiscordPresenceEnabled;
        PresenceAppId = settings.DiscordPresenceAppId ?? "";
        PresenceShowRepoButton = settings.DiscordPresenceShowRepoButton;
        
        IsWebhookValid = !string.IsNullOrWhiteSpace(WebhookUrl);
    }

    partial void OnWebhookUrlChanged(string value)
    {
        IsWebhookValid = !string.IsNullOrWhiteSpace(value) && 
                         (value.StartsWith("https://discord.com/api/webhooks/") ||
                          value.StartsWith("https://discordapp.com/api/webhooks/"));
    }

    [RelayCommand]
    private async Task TestWebhookAsync()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            StatusMessage = "⚠️ Please enter a webhook URL";
            return;
        }

        if (!WebhookUrl.StartsWith("https://discord.com/api/webhooks/") &&
            !WebhookUrl.StartsWith("https://discordapp.com/api/webhooks/"))
        {
            StatusMessage = "⚠️ Invalid webhook URL format";
            return;
        }

        IsTesting = true;
        StatusMessage = "Testing webhook...";

        var success = await _discordService.TestWebhookAsync(WebhookUrl);

        if (success)
        {
            StatusMessage = "✅ Webhook connected successfully!";
            IsWebhookValid = true;
        }
        else
        {
            StatusMessage = "❌ Failed to connect. Check the webhook URL.";
            IsWebhookValid = false;
        }

        IsTesting = false;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = _settingsService.Settings;
        settings.DiscordWebhookUrl = WebhookUrl;
        
        // User Events
        settings.DiscordNotifyUserJoins = NotifyUserJoins;
        settings.DiscordNotifyUserLeaves = NotifyUserLeaves;
        settings.DiscordNotifyUserKicked = NotifyUserKicked;
        settings.DiscordNotifyUserBanned = NotifyUserBanned;
        settings.DiscordNotifyUserUnbanned = NotifyUserUnbanned;
        settings.DiscordNotifyUserRoleAdd = NotifyUserRoleAdd;
        settings.DiscordNotifyUserRoleRemove = NotifyUserRoleRemove;
        
        // Role Events
        settings.DiscordNotifyRoleCreate = NotifyRoleCreate;
        settings.DiscordNotifyRoleUpdate = NotifyRoleUpdate;
        settings.DiscordNotifyRoleDelete = NotifyRoleDelete;
        
        // Instance Events
        settings.DiscordNotifyInstanceCreate = NotifyInstanceCreate;
        settings.DiscordNotifyInstanceDelete = NotifyInstanceDelete;
        settings.DiscordNotifyInstanceOpened = NotifyInstanceOpened;
        settings.DiscordNotifyInstanceClosed = NotifyInstanceClosed;
        
        // Group Events
        settings.DiscordNotifyGroupUpdate = NotifyGroupUpdate;
        
        // Invite Events
        settings.DiscordNotifyInviteCreate = NotifyInviteCreate;
        settings.DiscordNotifyInviteAccept = NotifyInviteAccept;
        settings.DiscordNotifyInviteReject = NotifyInviteReject;
        settings.DiscordNotifyJoinRequests = NotifyJoinRequests;
        
        // Announcement Events
        settings.DiscordNotifyAnnouncementCreate = NotifyAnnouncementCreate;
        settings.DiscordNotifyAnnouncementDelete = NotifyAnnouncementDelete;
        
        // Gallery Events
        settings.DiscordNotifyGalleryCreate = NotifyGalleryCreate;
        settings.DiscordNotifyGalleryDelete = NotifyGalleryDelete;
        
        // Post Events
        settings.DiscordNotifyPostCreate = NotifyPostCreate;
        settings.DiscordNotifyPostDelete = NotifyPostDelete;
        
        // Discord Presence
        settings.DiscordPresenceEnabled = PresenceEnabled;
        settings.DiscordPresenceAppId = PresenceAppId;
        settings.DiscordPresenceShowRepoButton = PresenceShowRepoButton;

        _settingsService.Save();
        StatusMessage = "✅ Settings saved!";
    }

    [RelayCommand]
    private void SelectAll()
    {
        // User Events
        NotifyUserJoins = true;
        NotifyUserLeaves = true;
        NotifyUserKicked = true;
        NotifyUserBanned = true;
        NotifyUserUnbanned = true;
        NotifyUserRoleAdd = true;
        NotifyUserRoleRemove = true;
        
        // Role Events
        NotifyRoleCreate = true;
        NotifyRoleUpdate = true;
        NotifyRoleDelete = true;
        
        // Instance Events
        NotifyInstanceCreate = true;
        NotifyInstanceDelete = true;
        NotifyInstanceOpened = true;
        NotifyInstanceClosed = true;
        
        // Group Events
        NotifyGroupUpdate = true;
        
        // Invite Events
        NotifyInviteCreate = true;
        NotifyInviteAccept = true;
        NotifyInviteReject = true;
        NotifyJoinRequests = true;
        
        // Announcement Events
        NotifyAnnouncementCreate = true;
        NotifyAnnouncementDelete = true;
        
        // Gallery Events
        NotifyGalleryCreate = true;
        NotifyGalleryDelete = true;
        
        // Post Events
        NotifyPostCreate = true;
        NotifyPostDelete = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        // User Events
        NotifyUserJoins = false;
        NotifyUserLeaves = false;
        NotifyUserKicked = false;
        NotifyUserBanned = false;
        NotifyUserUnbanned = false;
        NotifyUserRoleAdd = false;
        NotifyUserRoleRemove = false;
        
        // Role Events
        NotifyRoleCreate = false;
        NotifyRoleUpdate = false;
        NotifyRoleDelete = false;
        
        // Instance Events
        NotifyInstanceCreate = false;
        NotifyInstanceDelete = false;
        NotifyInstanceOpened = false;
        NotifyInstanceClosed = false;
        
        // Group Events
        NotifyGroupUpdate = false;
        
        // Invite Events
        NotifyInviteCreate = false;
        NotifyInviteAccept = false;
        NotifyInviteReject = false;
        NotifyJoinRequests = false;
        
        // Announcement Events
        NotifyAnnouncementCreate = false;
        NotifyAnnouncementDelete = false;
        
        // Gallery Events
        NotifyGalleryCreate = false;
        NotifyGalleryDelete = false;
        
        // Post Events
        NotifyPostCreate = false;
        NotifyPostDelete = false;
    }
}
