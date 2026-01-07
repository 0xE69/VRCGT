using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    public ObservableCollection<string> Regions { get; } = new(new[] { "US West", "US East", "Europe", "Japan" });
    public ObservableCollection<string> TimeZones { get; }
        = new(TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id));

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _selectedTimeZoneId = TimeZoneInfo.Local.Id;
    [ObservableProperty] private string _defaultRegion = "US West";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _status = string.Empty;

    public AppSettingsViewModel()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        Load();
    }

    private void Load()
    {
        var settings = _settingsService.Settings;
        SelectedTheme = string.IsNullOrWhiteSpace(settings.Theme) ? "Dark" : settings.Theme;
        SelectedTimeZoneId = settings.TimeZoneId;
        DefaultRegion = string.IsNullOrWhiteSpace(settings.DefaultRegion) ? "US West" : settings.DefaultRegion;
        StartWithWindows = settings.StartWithWindows;
        ApplyTheme(SelectedTheme);
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Settings;
        settings.Theme = SelectedTheme;
        settings.TimeZoneId = SelectedTimeZoneId;
        settings.DefaultRegion = DefaultRegion;
        settings.StartWithWindows = StartWithWindows;
        _settingsService.Save();
        ApplyTheme(SelectedTheme);
        SetStartupWithWindows(StartWithWindows);
        Status = "Settings saved";
    }

    private static void ApplyTheme(string themeName)
    {
        var theme = Application.Current.Resources.MergedDictionaries
            .OfType<BundledTheme>()
            .FirstOrDefault();

        if (theme == null) return;

        theme.BaseTheme = themeName.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? BaseTheme.Light
            : BaseTheme.Dark;
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
