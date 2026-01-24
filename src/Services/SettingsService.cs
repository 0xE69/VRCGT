using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VRCGroupTools.Models;

namespace VRCGroupTools.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    List<GroupConfiguration> ManagedGroups { get; }
    string? CurrentGroupId { get; set; }
    GroupConfiguration? GetCurrentGroupConfig();
    GroupConfiguration? GetGroupConfig(string groupId);
    void AddOrUpdateGroup(GroupConfiguration config);
    void RemoveGroup(string groupId);
    void Save();
    void Load();
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;

    public AppSettings Settings { get; private set; } = new();
    public List<GroupConfiguration> ManagedGroups { get; private set; } = new();
    public string? CurrentGroupId { get; set; }

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "VRCGroupTools");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");

        Load();
    }

    public GroupConfiguration? GetCurrentGroupConfig()
    {
        if (string.IsNullOrEmpty(CurrentGroupId))
            return null;
        
        return ManagedGroups.FirstOrDefault(g => g.GroupId == CurrentGroupId);
    }

    public GroupConfiguration? GetGroupConfig(string groupId)
    {
        return ManagedGroups.FirstOrDefault(g => g.GroupId == groupId);
    }

    public void AddOrUpdateGroup(GroupConfiguration config)
    {
        var existing = ManagedGroups.FirstOrDefault(g => g.GroupId == config.GroupId);
        if (existing != null)
        {
            ManagedGroups.Remove(existing);
        }
        
        config.LastAccessed = DateTime.UtcNow;
        ManagedGroups.Add(config);
        Save();
    }

    public void RemoveGroup(string groupId)
    {
        var existing = ManagedGroups.FirstOrDefault(g => g.GroupId == groupId);
        if (existing != null)
        {
            ManagedGroups.Remove(existing);
            Save();
        }
    }

    public void Save()
    {
        try
        {
            Console.WriteLine($"[SETTINGS] Saving to: {_settingsPath}");
            var data = new
            {
                GlobalSettings = Settings,
                ManagedGroups,
                CurrentGroupId
            };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
            Console.WriteLine($"[SETTINGS] Settings saved successfully. Managing {ManagedGroups.Count} groups.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SETTINGS] Failed to save settings: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                
                if (data != null)
                {
                    // Load global settings
                    Settings = data.GlobalSettings?.ToObject<AppSettings>() ?? new AppSettings();
                    
                    // Load managed groups
                    var groups = data.ManagedGroups?.ToObject<List<GroupConfiguration>>();
                    ManagedGroups = groups ?? new List<GroupConfiguration>();
                    
                    // Load current group ID
                    CurrentGroupId = data.CurrentGroupId?.ToString();
                    
                    // If no groups exist but old GroupId setting exists, migrate it
                    if (ManagedGroups.Count == 0 && !string.IsNullOrEmpty(Settings.GroupId))
                    {
                        Console.WriteLine("[SETTINGS] Migrating old group ID to new multi-group system");
                        var config = new GroupConfiguration
                        {
                            GroupId = Settings.GroupId,
                            GroupName = "My Group",
                            AddedAt = DateTime.UtcNow,
                            LastAccessed = DateTime.UtcNow
                        };
                        ManagedGroups.Add(config);
                        CurrentGroupId = Settings.GroupId;
                        Save();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            Settings = new AppSettings();
            ManagedGroups = new List<GroupConfiguration>();
        }
    }
}

public class AppSettings
{
    public string? GroupId { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool RememberGroupId { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public string? LastExportPath { get; set; }

    // Appearance & defaults
    public string Theme { get; set; } = "Dark";
    public string PrimaryColor { get; set; } = "DeepPurple";
    public string SecondaryColor { get; set; } = "Teal";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string DefaultRegion { get; set; } = "US West";
    
    // Language & Translation
    public string Language { get; set; } = "EN";
    public bool AutoTranslateEnabled { get; set; } = false;
    
    // UI Settings
    public double UIZoom { get; set; } = 1.0;
    public bool ShowTrayNotificationDot { get; set; } = true;
    
    // Application Behavior
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowConsoleWindow { get; set; } = false;
    
    // Update Settings
    public string UpdateAction { get; set; } = "Notify"; // Off, Notify, Auto Download
    
    // Discord Webhook Settings
    public string? DiscordWebhookUrl { get; set; }
    
    // User Events
    public bool DiscordNotifyUserJoins { get; set; } = true;
    public bool DiscordNotifyUserLeaves { get; set; } = true;
    public bool DiscordNotifyUserKicked { get; set; } = true;
    public bool DiscordNotifyUserBanned { get; set; } = true;
    public bool DiscordNotifyUserUnbanned { get; set; } = true;
    public bool DiscordNotifyUserRoleAdd { get; set; } = true;
    public bool DiscordNotifyUserRoleRemove { get; set; } = true;
    
    // Role Events
    public bool DiscordNotifyRoleCreate { get; set; } = true;
    public bool DiscordNotifyRoleUpdate { get; set; } = true;
    public bool DiscordNotifyRoleDelete { get; set; } = true;
    
    // Instance Events
    public bool DiscordNotifyInstanceCreate { get; set; } = true;
    public bool DiscordNotifyInstanceDelete { get; set; } = true;
    public bool DiscordNotifyInstanceOpened { get; set; } = false;
    public bool DiscordNotifyInstanceClosed { get; set; } = false;
    public bool DiscordNotifyInstanceWarn { get; set; } = true;
    
    // Group Events
    public bool DiscordNotifyGroupUpdate { get; set; } = true;
    
    // Invite Events
    public bool DiscordNotifyInviteCreate { get; set; } = true;
    public bool DiscordNotifyInviteAccept { get; set; } = true;
    public bool DiscordNotifyInviteReject { get; set; } = true;
    public bool DiscordNotifyJoinRequests { get; set; } = true;
    
    // Announcement Events
    public bool DiscordNotifyAnnouncementCreate { get; set; } = true;
    public bool DiscordNotifyAnnouncementDelete { get; set; } = true;
    
    // Gallery Events
    public bool DiscordNotifyGalleryCreate { get; set; } = true;
    public bool DiscordNotifyGalleryDelete { get; set; } = true;
    
    // Post Events
    public bool DiscordNotifyPostCreate { get; set; } = true;
    public bool DiscordNotifyPostDelete { get; set; } = true;

    // Audit Log Settings
    public int AuditLogPollingIntervalSeconds { get; set; } = 60; // How often to check for new logs
    public int AuditLogDiscordNotificationMaxAgeMinutes { get; set; } = 10; // Only send Discord notifications for logs newer than this

    // Instance Inviter Settings
    public bool InstanceInviterOnly18Plus { get; set; } = false;
    public bool InstanceInviterShowOfflineFriends { get; set; } = false;
    public bool InstanceInviterAutoRefresh { get; set; } = true;
    public int InstanceInviterRefreshIntervalSeconds { get; set; } = 30;

    // Calendar settings
    public bool AutoGenerateRecurringEvents { get; set; } = true;
    public int RecurringGenerationDaysAhead { get; set; } = 30;

    // Discord Presence (Rich Presence)
    public bool DiscordPresenceEnabled { get; set; } = false;
    public string? DiscordPresenceAppId { get; set; }
        = ""; // Discord Application Client ID
    public bool DiscordPresenceShowRepoButton { get; set; } = true;
    
    // Security Settings
    public bool SecurityMonitoringEnabled { get; set; } = false;
    public bool SecurityAutoRemoveRoles { get; set; } = true;
    public string? SecurityAlertWebhookUrl { get; set; }
    
    // Instance kick threshold settings
    public bool SecurityMonitorInstanceKicks { get; set; } = true;
    public int SecurityInstanceKickThreshold { get; set; } = 10; // Number of instance kicks
    public int SecurityInstanceKickTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Group kick threshold settings
    public bool SecurityMonitorGroupKicks { get; set; } = true;
    public int SecurityGroupKickThreshold { get; set; } = 5; // Number of group kicks
    public int SecurityGroupKickTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Instance ban threshold settings
    public bool SecurityMonitorInstanceBans { get; set; } = true;
    public int SecurityInstanceBanThreshold { get; set; } = 10; // Number of instance bans
    public int SecurityInstanceBanTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Group ban threshold settings
    public bool SecurityMonitorGroupBans { get; set; } = true;
    public int SecurityGroupBanThreshold { get; set; } = 3; // Number of group bans
    public int SecurityGroupBanTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Role removal threshold settings
    public bool SecurityMonitorRoleRemovals { get; set; } = true;
    public int SecurityRoleRemovalThreshold { get; set; } = 5; // Number of role removals
    public int SecurityRoleRemovalTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Invite rejection threshold settings
    public bool SecurityMonitorInviteRejections { get; set; } = true;
    public int SecurityInviteRejectionThreshold { get; set; } = 10; // Number of rejections
    public int SecurityInviteRejectionTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // Post/content deletion threshold settings
    public bool SecurityMonitorPostDeletions { get; set; } = true;
    public int SecurityPostDeletionThreshold { get; set; } = 5; // Number of deletions
    public int SecurityPostDeletionTimeframeMinutes { get; set; } = 10; // Within X minutes
    
    // General settings
    public bool SecurityRequireOwnerRole { get; set; } = true;
    public bool SecurityNotifyDiscord { get; set; } = true;
    public bool SecurityLogAllActions { get; set; } = true;
    public string SecurityOwnerUserId { get; set; } = ""; // VRChat User ID of the owner
}
