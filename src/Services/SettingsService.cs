using System;
using System.IO;
using Newtonsoft.Json;

namespace VRCGroupTools.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Load();
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "VRCGroupTools");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");

        Load();
    }

    public void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
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
                Settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            Settings = new AppSettings();
        }
    }
}

public class AppSettings
{
    public string? GroupId { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool RememberGroupId { get; set; } = true;
    public string? LastExportPath { get; set; }

    // Appearance & defaults
    public string Theme { get; set; } = "Dark";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string DefaultRegion { get; set; } = "US West";
    
    // Discord Webhook Settings
    public string? DiscordWebhookUrl { get; set; }
    public bool DiscordNotifyUserJoins { get; set; } = true;
    public bool DiscordNotifyUserLeaves { get; set; } = true;
    public bool DiscordNotifyUserKicked { get; set; } = true;
    public bool DiscordNotifyUserBanned { get; set; } = true;
    public bool DiscordNotifyUserUnbanned { get; set; } = true;
    public bool DiscordNotifyInstanceOpened { get; set; } = false;
    public bool DiscordNotifyInstanceClosed { get; set; } = false;
    public bool DiscordNotifyJoinRequests { get; set; } = true;
    public bool DiscordNotifyRoleUpdate { get; set; } = true;

    // Calendar settings
    public bool AutoGenerateRecurringEvents { get; set; } = true;
    public int RecurringGenerationDaysAhead { get; set; } = 30;
}
