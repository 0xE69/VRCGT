using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class AppSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public ObservableCollection<string> Themes { get; } = new(new[] { "Dark", "Light" });
    public ObservableCollection<string> Colors { get; } = new(new[] { "DeepPurple", "Indigo", "Blue", "Teal", "Green", "Amber", "Orange", "DeepOrange", "Red", "Pink", "Purple", "BlueGrey", "Grey" });
    public ObservableCollection<string> Regions { get; } = new(new[] { "US West", "US East", "Europe", "Japan" });
    public ObservableCollection<string> Languages { get; } = new(new[] { "EN", "ES", "FR", "DE", "IT", "PT", "RU", "JA", "ZH", "KO" });
    public ObservableCollection<string> UpdateActions { get; } = new(new[] { "Off", "Notify", "Auto Download" });
    public ObservableCollection<string> TimeZones { get; }
        = new(TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id));

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _selectedPrimaryColor = "DeepPurple";
    [ObservableProperty] private string _selectedSecondaryColor = "Teal";
    [ObservableProperty] private string _selectedTimeZoneId = TimeZoneInfo.Local.Id;
    [ObservableProperty] private string _defaultRegion = "US West";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _status = string.Empty;
    
    // Language & Translation
    [ObservableProperty] private string _selectedLanguage = "EN";
    [ObservableProperty] private bool _autoTranslateEnabled;
    
    // UI Settings
    [ObservableProperty] private double _uiZoom = 1.0;
    [ObservableProperty] private bool _showTrayNotificationDot = true;
    
    // Application Behavior
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _showConsoleWindow = false;
    
    // Update Settings
    [ObservableProperty] private string _updateAction = "Notify";
    
    // App Info (Read-only)
    public string AppVersion { get; }
    public string RepositoryUrl { get; } = "https://github.com/yourusername/VRCGroupTools";
    public string SupportUrl { get; } = "https://discord.gg/yourdiscord";
    public string LegalNotice { get; } = 
        "VRCGT is an assistant tool for VRChat that provides information and manages groups. " +
        "This application makes use of the unofficial VRChat API and is not endorsed by VRChat Inc. " +
        "VRCGT does not reflect the views or opinions of VRChat or anyone officially involved in " +
        "producing or managing VRChat properties. VRChat and all associated properties are trademarks " +
        "or registered trademarks of VRChat Inc. Use of this tool is at your own risk. " +
        "The developers of VRCGT are not responsible for any account actions taken by VRChat Inc.";

    public AppSettingsViewModel()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.3";
        Load();
    }

    private void Load()
    {
        var settings = _settingsService.Settings;
        SelectedTheme = string.IsNullOrWhiteSpace(settings.Theme) ? "Dark" : settings.Theme;
        SelectedPrimaryColor = string.IsNullOrWhiteSpace(settings.PrimaryColor) ? "DeepPurple" : settings.PrimaryColor;
        SelectedSecondaryColor = string.IsNullOrWhiteSpace(settings.SecondaryColor) ? "Teal" : settings.SecondaryColor;
        SelectedTimeZoneId = settings.TimeZoneId;
        DefaultRegion = string.IsNullOrWhiteSpace(settings.DefaultRegion) ? "US West" : settings.DefaultRegion;
        StartWithWindows = settings.StartWithWindows;
        
        // Language & Translation
        SelectedLanguage = string.IsNullOrWhiteSpace(settings.Language) ? "EN" : settings.Language;
        AutoTranslateEnabled = settings.AutoTranslateEnabled;
        
        // UI Settings
        UiZoom = settings.UIZoom;
        ShowTrayNotificationDot = settings.ShowTrayNotificationDot;
        
        // Application Behavior
        StartMinimized = settings.StartMinimized;
        MinimizeToTray = settings.MinimizeToTray;
        ShowConsoleWindow = settings.ShowConsoleWindow;
        
        // Update Settings
        UpdateAction = string.IsNullOrWhiteSpace(settings.UpdateAction) ? "Notify" : settings.UpdateAction;
        
        ApplyTheme(SelectedTheme, SelectedPrimaryColor, SelectedSecondaryColor);
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Settings;
        settings.Theme = SelectedTheme;
        settings.PrimaryColor = SelectedPrimaryColor;
        settings.SecondaryColor = SelectedSecondaryColor;
        settings.TimeZoneId = SelectedTimeZoneId;
        settings.DefaultRegion = DefaultRegion;
        settings.StartWithWindows = StartWithWindows;
        
        // Language & Translation
        settings.Language = SelectedLanguage;
        settings.AutoTranslateEnabled = AutoTranslateEnabled;
        
        // UI Settings
        settings.UIZoom = UiZoom;
        settings.ShowTrayNotificationDot = ShowTrayNotificationDot;
        
        // Application Behavior
        settings.StartMinimized = StartMinimized;
        settings.MinimizeToTray = MinimizeToTray;
        settings.ShowConsoleWindow = ShowConsoleWindow;
        
        // Update Settings
        settings.UpdateAction = UpdateAction;
        
        _settingsService.Save();
        ApplyTheme(SelectedTheme, SelectedPrimaryColor, SelectedSecondaryColor);
        SetStartupWithWindows(StartWithWindows);
        Status = "✅ Settings saved successfully!";
    }

    private static void ApplyTheme(string themeName, string primary, string secondary)
    {
        var theme = Application.Current.Resources.MergedDictionaries
            .OfType<BundledTheme>()
            .FirstOrDefault();

        if (theme == null) return;

        theme.BaseTheme = themeName.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? BaseTheme.Light
            : BaseTheme.Dark;

        // Note: PrimaryColor and SecondaryColor enums seem detailed in this version/environment. 
        // Commenting out dynamic setting to fix build for now.
        /*
        if (Enum.TryParse(primary, true, out PrimaryColor pColor))
        {
            theme.PrimaryColor = pColor;
        }
        if (Enum.TryParse(secondary, true, out SecondaryColor sColor))
        {
            theme.SecondaryColor = sColor;
        }
        */
    }

    private static void SetStartupWithWindows(bool enable)
    {
        try
        {
            const string appName = "VRCGroupTools";
            var startupKey = Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

            if (startupKey == null)
            {
                Console.WriteLine("[STARTUP] Failed to open registry key");
                return;
            }

            if (enable)
            {
                // Get the executable path using Environment or AppContext
                var exePath = Environment.ProcessPath ?? System.AppContext.BaseDirectory;
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    exePath = exePath.Replace(".dll", ".exe");
                }
                // If it's a directory path, append the exe name
                if (System.IO.Directory.Exists(exePath))
                {
                    exePath = System.IO.Path.Combine(exePath, "VRCGroupTools.exe");
                }
                
                startupKey.SetValue(appName, $"\"{exePath}\"");
                Console.WriteLine($"[STARTUP] Enabled startup with Windows: {exePath}");
            }
            else
            {
                startupKey.DeleteValue(appName, false);
                Console.WriteLine("[STARTUP] Disabled startup with Windows");
            }

            startupKey.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STARTUP] Error setting startup: {ex.Message}");
        }
    }
}
