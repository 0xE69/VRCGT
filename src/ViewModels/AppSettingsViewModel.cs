using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
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
        ApplyTheme(SelectedTheme);
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Settings;
        settings.Theme = SelectedTheme;
        settings.TimeZoneId = SelectedTimeZoneId;
        settings.DefaultRegion = DefaultRegion;
        _settingsService.Save();
        ApplyTheme(SelectedTheme);
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
}
