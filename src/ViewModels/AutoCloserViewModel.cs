using System;
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
            settings.AutoCloserAllowedRegions = AutoCloserAllowedRegions;

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
        try
        {
            IsLoadingInstances = true;
            ActiveInstances.Clear();

            var instances = await _autoCloserService.GetActiveInstancesAsync();

            foreach (var instance in instances)
            {
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
                    AgeGatedColor = instance.AgeGated ? "#4CAF50" : "#F44336"
                };

                ActiveInstances.Add(displayItem);
            }

            StatusMessage = $"Found {instances.Count} active instances";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to load instances: {ex.Message}";
            LoggingService.Error("AUTO-CLOSER-VM", ex, "Failed to refresh instances");
        }
        finally
        {
            IsLoadingInstances = false;
        }
    }

    [RelayCommand]
    private async Task CloseInstanceAsync(string instanceId)
    {
        try
        {
            StatusMessage = $"Closing instance {instanceId}...";
            
            var success = await _autoCloserService.CloseInstanceAsync(instanceId);
            
            if (success)
            {
                StatusMessage = $"✓ Instance closed successfully";
                await RefreshInstancesAsync();
            }
            else
            {
                StatusMessage = "✗ Failed to close instance";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            LoggingService.Error("AUTO-CLOSER-VM", ex, $"Failed to close instance {instanceId}");
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
}
