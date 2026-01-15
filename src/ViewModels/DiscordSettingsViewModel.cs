using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class DiscordSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDiscordWebhookService _discordService;
    private bool _isLoading = false; // Prevent auto-save during initial load

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

    [ObservableProperty]
    private bool _notifyInstanceWarn;

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
        _isLoading = true; // Prevent auto-save during load
        
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
        NotifyInstanceWarn = settings.DiscordNotifyInstanceWarn;
        
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
        
        _isLoading = false; // Enable auto-save after load
    }

    partial void OnWebhookUrlChanged(string value)
    {
        IsWebhookValid = !string.IsNullOrWhiteSpace(value) && 
                         (value.StartsWith("https://discord.com/api/webhooks/") ||
                          value.StartsWith("https://discordapp.com/api/webhooks/"));
        AutoSaveSettings();
    }

    // Auto-save when any notification setting changes
    partial void OnNotifyUserJoinsChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserJoins changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserLeavesChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserLeaves changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserKickedChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserKicked changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserBannedChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserBanned changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserUnbannedChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserUnbanned changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserRoleAddChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserRoleAdd changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyUserRoleRemoveChanged(bool value) { Console.WriteLine($"[DISCORD-VM] NotifyUserRoleRemove changed to: {value}"); AutoSaveSettings(); }
    partial void OnNotifyRoleCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyRoleUpdateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyRoleDeleteChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInstanceCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInstanceDeleteChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInstanceOpenedChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInstanceClosedChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInstanceWarnChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyGroupUpdateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInviteCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInviteAcceptChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyInviteRejectChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyJoinRequestsChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyAnnouncementCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyAnnouncementDeleteChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyGalleryCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyGalleryDeleteChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyPostCreateChanged(bool value) => AutoSaveSettings();
    partial void OnNotifyPostDeleteChanged(bool value) => AutoSaveSettings();
    partial void OnPresenceEnabledChanged(bool value) => AutoSaveSettings();
    partial void OnPresenceAppIdChanged(string value) => AutoSaveSettings();
    partial void OnPresenceShowRepoButtonChanged(bool value) => AutoSaveSettings();

    private void AutoSaveSettings()
    {
        if (_isLoading)
        {
            Console.WriteLine("[DISCORD-VM] AutoSaveSettings skipped - still loading");
            return; // Don't save during initial load
        }
        
        Console.WriteLine("[DISCORD-VM] AutoSaveSettings triggered - saving now");
        SaveSettings();
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

        var result = await _discordService.TestWebhookAsync(WebhookUrl);

        if (result.Success)
        {
            StatusMessage = "✅ Webhook connected successfully!";
            IsWebhookValid = true;
        }
        else
        {
            var status = result.StatusCode.HasValue ? $" (HTTP {result.StatusCode})" : "";
            var error = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" {result.ErrorMessage}";
            StatusMessage = $"❌ Failed to connect{status}.{error}";
            IsWebhookValid = false;
        }

        IsTesting = false;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        Console.WriteLine("[DISCORD-VM] SaveSettings called");
        var settings = _settingsService.Settings;
        settings.DiscordWebhookUrl = WebhookUrl;
        Console.WriteLine("[DISCORD-VM] Saving webhook URL (redacted)");
        
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
        settings.DiscordNotifyInstanceWarn = NotifyInstanceWarn;
        
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

        Console.WriteLine("[DISCORD-VM] Calling _settingsService.Save()...");
        _settingsService.Save();
        Console.WriteLine("[DISCORD-VM] Settings saved successfully!");
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
        NotifyInstanceWarn = true;
        
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
        NotifyInstanceWarn = false;
        
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
