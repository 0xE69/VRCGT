using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class AutoCloserViewModel : ObservableObject
{
    private readonly IAutoCloserService _autoCloserService;
    private readonly ISettingsService _settingsService;
    private readonly IVRChatApiService _apiService;

    [ObservableProperty]
    private bool _autoCloserEnabled;

    [ObservableProperty]
    private bool _autoCloserRequireAgeGate;

    [ObservableProperty]
    private int _autoCloserCheckIntervalSeconds;

    [ObservableProperty]
    private bool _autoCloserNotifyDiscord;

    [ObservableProperty]
    private string _autoCloserAllowedRegions = string.Empty;

    [ObservableProperty]
    private string _autoCloserAllowedAccessTypes = string.Empty;

    // Access-type checkboxes (checked = allowed to stay open). Backed by the
    // comma-separated AutoCloserAllowedAccessTypes setting via Build/Apply helpers.
    [ObservableProperty]
    private bool _allowAccessGroup;

    [ObservableProperty]
    private bool _allowAccessGroupPlus;

    [ObservableProperty]
    private bool _allowAccessPublic;

    // Region checkboxes (checked = allowed to stay open).
    // VRChat reports US regions as us_west / us_east (not a single "us").
    [ObservableProperty]
    private bool _allowRegionUsWest;

    [ObservableProperty]
    private bool _allowRegionUsEast;

    [ObservableProperty]
    private bool _allowRegionEu;

    [ObservableProperty]
    private bool _allowRegionJp;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string _statusMessage = "Auto Closer is not active";

    [ObservableProperty]
    private int _closedInstanceCount;

    [ObservableProperty]
    private ObservableCollection<GroupInstanceDisplayItem> _activeInstances = new();

    [ObservableProperty]
    private bool _isLoadingInstances;

    public AutoCloserViewModel(
        IAutoCloserService autoCloserService,
        ISettingsService settingsService,
        IVRChatApiService apiService)
    {
        _autoCloserService = autoCloserService;
        _settingsService = settingsService;
        _apiService = apiService;

        _autoCloserService.StatusChanged += (s, status) =>
        {
            StatusMessage = status;
            IsMonitoring = _autoCloserService.IsMonitoring;
            ClosedInstanceCount = _autoCloserService.ClosedInstanceCount;
        };

        _autoCloserService.InstanceClosed += async (s, e) =>
        {
            ClosedInstanceCount = _autoCloserService.ClosedInstanceCount;
            await RefreshInstancesAsync();
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        AutoCloserEnabled = settings.AutoCloserEnabled;
        AutoCloserRequireAgeGate = settings.AutoCloserRequireAgeGate;
        AutoCloserCheckIntervalSeconds = settings.AutoCloserCheckIntervalSeconds;
        AutoCloserNotifyDiscord = settings.AutoCloserNotifyDiscord;
        AutoCloserAllowedRegions = settings.AutoCloserAllowedRegions;
        AutoCloserAllowedAccessTypes = settings.AutoCloserAllowedAccessTypes;
        ApplyAccessCheckboxes(settings.AutoCloserAllowedAccessTypes);
        ApplyRegionCheckboxes(settings.AutoCloserAllowedRegions);
        IsMonitoring = _autoCloserService.IsMonitoring;
        ClosedInstanceCount = _autoCloserService.ClosedInstanceCount;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = _settingsService.Settings;
            settings.AutoCloserEnabled = AutoCloserEnabled;
            settings.AutoCloserRequireAgeGate = AutoCloserRequireAgeGate;
            settings.AutoCloserCheckIntervalSeconds = AutoCloserCheckIntervalSeconds;
            settings.AutoCloserNotifyDiscord = AutoCloserNotifyDiscord;
            var regionCsv = BuildRegionCsv();
            var accessCsv = BuildAccessCsv();
            AutoCloserAllowedRegions = regionCsv;
            AutoCloserAllowedAccessTypes = accessCsv;
            settings.AutoCloserAllowedRegions = regionCsv;
            settings.AutoCloserAllowedAccessTypes = accessCsv;

            _settingsService.Save();
            StatusMessage = "✓ Settings saved successfully!";
            LoggingService.Info("AUTO-CLOSER-VM", "Settings saved");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to save settings: {ex.Message}";
            LoggingService.Error("AUTO-CLOSER-VM", ex, "Failed to save settings");
        }
    }

    // --- Access / region checkbox helpers (bridge to the comma-separated settings) ---

    private static HashSet<string> ParseCsvSet(string csv)
    {
        return (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();
    }

    private void ApplyAccessCheckboxes(string csv)
    {
        var set = ParseCsvSet(csv);
        AllowAccessGroup = set.Contains("group") || set.Contains("members");
        AllowAccessGroupPlus = set.Contains("plus") || set.Contains("group_fof");
        AllowAccessPublic = set.Contains("public");
    }

    private void ApplyRegionCheckboxes(string csv)
    {
        var set = ParseCsvSet(csv);
        AllowRegionUsWest = set.Contains("us_west");
        AllowRegionUsEast = set.Contains("us_east");
        // Back-compat: older settings stored a single "us" — treat it as both US regions.
        if (set.Contains("us"))
        {
            AllowRegionUsWest = true;
            AllowRegionUsEast = true;
        }
        AllowRegionEu = set.Contains("eu");
        AllowRegionJp = set.Contains("jp");
    }

    private string BuildAccessCsv()
    {
        var list = new List<string>();
        if (AllowAccessGroup) list.Add("group");
        if (AllowAccessGroupPlus) list.Add("plus");
        if (AllowAccessPublic) list.Add("public");
        return string.Join(",", list);
    }

    private string BuildRegionCsv()
    {
        var list = new List<string>();
        if (AllowRegionUsWest) list.Add("us_west");
        if (AllowRegionUsEast) list.Add("us_east");
        if (AllowRegionEu) list.Add("eu");
        if (AllowRegionJp) list.Add("jp");
        return string.Join(",", list);
    }

    [RelayCommand]
    private async Task ToggleMonitoringAsync()
    {
        try
        {
            if (IsMonitoring)
            {
                _autoCloserService.StopMonitoring();
                IsMonitoring = false;
                StatusMessage = "⏸ Monitoring stopped";
            }
            else
            {
                var groupId = _apiService.CurrentGroupId;
                if (string.IsNullOrEmpty(groupId))
                {
                    StatusMessage = "⚠ Please set a Group ID first";
                    return;
                }

                // Save settings first
                SaveSettings();

                await _autoCloserService.StartMonitoringAsync(groupId);
                IsMonitoring = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            LoggingService.Error("AUTO-CLOSER-VM", ex, "Failed to toggle monitoring");
        }
    }

    [RelayCommand]
    private async Task RefreshInstancesAsync()
    {
        // This method is invoked from BOTH the UI thread (Refresh button) and the Auto
        // Closer's background monitoring loop, so every ObservableCollection / UI-property
        // mutation must be marshalled to the WPF Dispatcher thread — otherwise WPF throws
        // NotSupportedException ("changes to SourceCollection from a thread different from
        // the Dispatcher thread").
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void OnUi(Action action)
        {
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(action);
            else
                action();
        }

        try
        {
            OnUi(() => IsLoadingInstances = true);

            var instances = await _autoCloserService.GetActiveInstancesAsync();

            OnUi(() =>
            {
                ActiveInstances.Clear();

                foreach (var instance in instances)
                {
                    var access = (instance.AccessType ?? string.Empty).Trim().ToLowerInvariant();
                    var (accessDisplay, accessColor) = access switch
                    {
                        "members" or "group" => ("Group", "#4CAF50"),
                        "plus" or "group_fof" => ("Group+", "#4CAF50"),
                        "public" => ("Public", "#F44336"),
                        "friends" => ("Friends", "#FF9800"),
                        "private" => ("Private", "#9E9E9E"),
                        _ => ("—", "#888")
                    };

                    var displayItem = new GroupInstanceDisplayItem
                    {
                        InstanceId = instance.InstanceId,
                        WorldId = instance.WorldId,
                        WorldName = instance.WorldName,
                        Region = instance.Region,
                        AgeGated = instance.AgeGated,
                        UserCount = instance.UserCount,
                        OwnerName = instance.OwnerName,
                        CreatedAt = instance.CreatedAt,
                        AgeGatedDisplay = instance.AgeGated ? "✓ 18+" : "✗ No",
                        AgeGatedColor = instance.AgeGated ? "#4CAF50" : "#F44336",
                        AccessType = instance.AccessType ?? string.Empty,
                        AccessDisplay = accessDisplay,
                        AccessColor = accessColor
                    };

                    ActiveInstances.Add(displayItem);
                }

                StatusMessage = $"Found {instances.Count} active instances";
            });
        }
        catch (Exception ex)
        {
            OnUi(() => StatusMessage = $"✗ Failed to load instances: {ex.Message}");
            LoggingService.Error("AUTO-CLOSER-VM", ex, "Failed to refresh instances");
        }
        finally
        {
            OnUi(() => IsLoadingInstances = false);
        }
    }

    [RelayCommand]
    private async Task CloseInstanceAsync(GroupInstanceDisplayItem item)
    {
        try
        {
            StatusMessage = $"Closing instance {item.InstanceId}...";
            
            var result = await _autoCloserService.CloseInstanceAsync(item.WorldId, item.InstanceId);
            
            switch (result)
            {
                case CloseInstanceResult.Closed:
                    StatusMessage = $"✓ Instance closed successfully";
                    await RefreshInstancesAsync();
                    break;
                case CloseInstanceResult.AlreadyClosed:
                    StatusMessage = $"ℹ Instance was already closed";
                    await RefreshInstancesAsync();
                    break;
                case CloseInstanceResult.Failed:
                default:
                    StatusMessage = "✗ Failed to close instance";
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            LoggingService.Error("AUTO-CLOSER-VM", ex, $"Failed to close instance {item.InstanceId}");
        }
    }
}

public class GroupInstanceDisplayItem
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string AgeGatedDisplay { get; set; } = string.Empty;
    public string AgeGatedColor { get; set; } = "#888";
    public string AccessType { get; set; } = string.Empty;
    public string AccessDisplay { get; set; } = string.Empty;
    public string AccessColor { get; set; } = "#888";
}
