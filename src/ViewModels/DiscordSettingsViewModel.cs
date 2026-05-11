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

    // Webhook URLs per category
    [ObservableProperty]
    private string _webhookMemberEvents = "";

    [ObservableProperty]
    private string _webhookRoleEvents = "";

    [ObservableProperty]
    private string _webhookInstanceEvents = "";

    [ObservableProperty]
    private string _webhookGroupEvents = "";

    [ObservableProperty]
    private string _webhookInviteEvents = "";

    [ObservableProperty]
    private string _webhookAnnouncementEvents = "";

    [ObservableProperty]
    private string _webhookGalleryEvents = "";

    [ObservableProperty]
    private string _webhookPostEvents = "";

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
        var groupConfig = _settingsService.GetCurrentGroupConfig();
        
        if (groupConfig != null)
        {
            // User Events
            NotifyUserJoins = groupConfig.DiscordNotifyUserJoins;
            NotifyUserLeaves = groupConfig.DiscordNotifyUserLeaves;
            NotifyUserKicked = groupConfig.DiscordNotifyUserKicked;
            NotifyUserBanned = groupConfig.DiscordNotifyUserBanned;
            NotifyUserUnbanned = groupConfig.DiscordNotifyUserUnbanned;
            NotifyUserRoleAdd = groupConfig.DiscordNotifyUserRoleAdd;
            NotifyUserRoleRemove = groupConfig.DiscordNotifyUserRoleRemove;
            
            // Role Events
            NotifyRoleCreate = groupConfig.DiscordNotifyRoleCreate;
            NotifyRoleUpdate = groupConfig.DiscordNotifyRoleUpdate;
            NotifyRoleDelete = groupConfig.DiscordNotifyRoleDelete;
            
            // Instance Events
            NotifyInstanceCreate = groupConfig.DiscordNotifyInstanceCreate;
            NotifyInstanceDelete = groupConfig.DiscordNotifyInstanceDelete;
            NotifyInstanceOpened = groupConfig.DiscordNotifyInstanceOpened;
            NotifyInstanceClosed = groupConfig.DiscordNotifyInstanceClosed;
            NotifyInstanceWarn = groupConfig.DiscordNotifyInstanceWarn;
            
            // Group Events
            NotifyGroupUpdate = groupConfig.DiscordNotifyGroupUpdate;
            
            // Invite Events
            NotifyInviteCreate = groupConfig.DiscordNotifyInviteCreate;
            NotifyInviteAccept = groupConfig.DiscordNotifyInviteAccept;
            NotifyInviteReject = groupConfig.DiscordNotifyInviteReject;
            NotifyJoinRequests = groupConfig.DiscordNotifyJoinRequests;
            
            // Announcement Events
            NotifyAnnouncementCreate = groupConfig.DiscordNotifyAnnouncementCreate;
            NotifyAnnouncementDelete = groupConfig.DiscordNotifyAnnouncementDelete;
            
            // Gallery Events
            NotifyGalleryCreate = groupConfig.DiscordNotifyGalleryCreate;
            NotifyGalleryDelete = groupConfig.DiscordNotifyGalleryDelete;
            
            // Post Events
            NotifyPostCreate = groupConfig.DiscordNotifyPostCreate;
            NotifyPostDelete = groupConfig.DiscordNotifyPostDelete;
            
            // Load webhook URLs for each category
            WebhookMemberEvents = groupConfig.DiscordWebhookMemberEvents ?? "";
            WebhookRoleEvents = groupConfig.DiscordWebhookRoleEvents ?? "";
            WebhookInstanceEvents = groupConfig.DiscordWebhookInstanceEvents ?? "";
            WebhookGroupEvents = groupConfig.DiscordWebhookGroupEvents ?? "";
            WebhookInviteEvents = groupConfig.DiscordWebhookInviteEvents ?? "";
            WebhookAnnouncementEvents = groupConfig.DiscordWebhookAnnouncementEvents ?? "";
            WebhookGalleryEvents = groupConfig.DiscordWebhookGalleryEvents ?? "";
            WebhookPostEvents = groupConfig.DiscordWebhookPostEvents ?? "";
        }
        
        _isLoading = false; // Enable auto-save after load
    }

    // Webhook URL change handlers
    partial void OnWebhookMemberEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookRoleEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookInstanceEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookGroupEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookInviteEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookAnnouncementEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookGalleryEventsChanged(string value) => AutoSaveSettings();
    partial void OnWebhookPostEventsChanged(string value) => AutoSaveSettings();

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
    private void SaveSettings()
    {
        Console.WriteLine("[DISCORD-VM] SaveSettings called");
        var settings = _settingsService.Settings;
        var groupConfig = _settingsService.GetCurrentGroupConfig();
        
        if (groupConfig != null)
        {
            // User Events
            groupConfig.DiscordNotifyUserJoins = NotifyUserJoins;
            groupConfig.DiscordNotifyUserLeaves = NotifyUserLeaves;
            groupConfig.DiscordNotifyUserKicked = NotifyUserKicked;
            groupConfig.DiscordNotifyUserBanned = NotifyUserBanned;
            groupConfig.DiscordNotifyUserUnbanned = NotifyUserUnbanned;
            groupConfig.DiscordNotifyUserRoleAdd = NotifyUserRoleAdd;
            groupConfig.DiscordNotifyUserRoleRemove = NotifyUserRoleRemove;
            
            // Role Events
            groupConfig.DiscordNotifyRoleCreate = NotifyRoleCreate;
            groupConfig.DiscordNotifyRoleUpdate = NotifyRoleUpdate;
            groupConfig.DiscordNotifyRoleDelete = NotifyRoleDelete;
            
            // Instance Events
            groupConfig.DiscordNotifyInstanceCreate = NotifyInstanceCreate;
            groupConfig.DiscordNotifyInstanceDelete = NotifyInstanceDelete;
            groupConfig.DiscordNotifyInstanceOpened = NotifyInstanceOpened;
            groupConfig.DiscordNotifyInstanceClosed = NotifyInstanceClosed;
            groupConfig.DiscordNotifyInstanceWarn = NotifyInstanceWarn;
            
            // Group Events
            groupConfig.DiscordNotifyGroupUpdate = NotifyGroupUpdate;
            
            // Invite Events
            groupConfig.DiscordNotifyInviteCreate = NotifyInviteCreate;
            groupConfig.DiscordNotifyInviteAccept = NotifyInviteAccept;
            groupConfig.DiscordNotifyInviteReject = NotifyInviteReject;
            groupConfig.DiscordNotifyJoinRequests = NotifyJoinRequests;
            
            // Announcement Events
            groupConfig.DiscordNotifyAnnouncementCreate = NotifyAnnouncementCreate;
            groupConfig.DiscordNotifyAnnouncementDelete = NotifyAnnouncementDelete;
            
            // Gallery Events
            groupConfig.DiscordNotifyGalleryCreate = NotifyGalleryCreate;
            groupConfig.DiscordNotifyGalleryDelete = NotifyGalleryDelete;
            
            // Post Events
            groupConfig.DiscordNotifyPostCreate = NotifyPostCreate;
            groupConfig.DiscordNotifyPostDelete = NotifyPostDelete;
            
            // Save webhook URLs for each category
            groupConfig.DiscordWebhookMemberEvents = WebhookMemberEvents;
            groupConfig.DiscordWebhookRoleEvents = WebhookRoleEvents;
            groupConfig.DiscordWebhookInstanceEvents = WebhookInstanceEvents;
            groupConfig.DiscordWebhookGroupEvents = WebhookGroupEvents;
            groupConfig.DiscordWebhookInviteEvents = WebhookInviteEvents;
            groupConfig.DiscordWebhookAnnouncementEvents = WebhookAnnouncementEvents;
            groupConfig.DiscordWebhookGalleryEvents = WebhookGalleryEvents;
            groupConfig.DiscordWebhookPostEvents = WebhookPostEvents;
            
            _settingsService.AddOrUpdateGroup(groupConfig);
        }

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
