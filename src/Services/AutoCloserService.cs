using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json.Linq;
using Timer = System.Timers.Timer;

namespace VRCGroupTools.Services;

public interface IAutoCloserService
{
    event EventHandler<AutoCloserEventArgs>? InstanceClosed;
    event EventHandler<string>? StatusChanged;
    
    Task StartMonitoringAsync(string groupId);
    void StopMonitoring();
    Task<List<GroupInstanceInfo>> GetActiveInstancesAsync();
    Task<bool> CloseInstanceAsync(string instanceId);
    bool IsMonitoring { get; }
    int ClosedInstanceCount { get; }
}

public class AutoCloserEventArgs : EventArgs
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; }
}

public class GroupInstanceInfo
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AutoCloserService : IAutoCloserService, IDisposable
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IDiscordWebhookService _discordService;
    
    private Timer? _monitorTimer;
    private string? _currentGroupId;
    private bool _isMonitoring;
    private int _closedInstanceCount;
    
    public event EventHandler<AutoCloserEventArgs>? InstanceClosed;
    public event EventHandler<string>? StatusChanged;
    
    public bool IsMonitoring => _isMonitoring;
    public int ClosedInstanceCount => _closedInstanceCount;
    
    public AutoCloserService(
        IVRChatApiService apiService,
        ISettingsService settingsService,
        IDiscordWebhookService discordService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _discordService = discordService;
    }
    
    public async Task StartMonitoringAsync(string groupId)
    {
        if (!_settingsService.Settings.AutoCloserEnabled)
        {
            LoggingService.Info("AUTO-CLOSER", "Auto Closer is disabled in settings");
            return;
        }
        
        _currentGroupId = groupId;
        _closedInstanceCount = 0;
        
        LoggingService.Info("AUTO-CLOSER", $"Starting instance monitoring for group: {groupId}");
        StatusChanged?.Invoke(this, "Starting instance monitoring...");
        
        // Initial check
        await CheckInstancesAsync();
        
        // Set up timer for periodic checks
        var intervalMs = _settingsService.Settings.AutoCloserCheckIntervalSeconds * 1000;
        _monitorTimer?.Dispose();
        _monitorTimer = new Timer(intervalMs);
        _monitorTimer.Elapsed += async (s, e) => await CheckInstancesAsync();
        _monitorTimer.AutoReset = true;
        _monitorTimer.Start();
        _isMonitoring = true;
        
        LoggingService.Info("AUTO-CLOSER", $"Monitoring started (checking every {_settingsService.Settings.AutoCloserCheckIntervalSeconds}s)");
        StatusChanged?.Invoke(this, $"▶ Monitoring active - checking every {_settingsService.Settings.AutoCloserCheckIntervalSeconds}s");
    }
    
    public void StopMonitoring()
    {
        LoggingService.Info("AUTO-CLOSER", "Stopping instance monitoring");
        _monitorTimer?.Stop();
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        _isMonitoring = false;
        StatusChanged?.Invoke(this, "⏸ Monitoring stopped");
    }
    
    public async Task<List<GroupInstanceInfo>> GetActiveInstancesAsync()
    {
        var instances = new List<GroupInstanceInfo>();
        
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            return instances;
        }
        
        try
        {
            // Get group instances from the API
            var response = await _apiService.GetGroupInstancesAsync(_currentGroupId);
            
            if (response != null)
            {
                foreach (var instance in response)
                {
                    instances.Add(new GroupInstanceInfo
                    {
                        InstanceId = instance.InstanceId,
                        WorldId = instance.WorldId,
                        WorldName = instance.WorldName,
                        Region = instance.Region,
                        AgeGated = instance.AgeGated,
                        UserCount = instance.UserCount,
                        OwnerId = instance.OwnerId,
                        OwnerName = instance.OwnerName,
                        CreatedAt = instance.CreatedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("AUTO-CLOSER", ex, "Failed to get group instances");
        }
        
        return instances;
    }
    
    public async Task<bool> CloseInstanceAsync(string instanceId)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentGroupId))
            {
                return false;
            }
            
            LoggingService.Info("AUTO-CLOSER", $"Attempting to close instance: {instanceId}");
            
            var success = await _apiService.DeleteGroupInstanceAsync(_currentGroupId, instanceId);
            
            if (success)
            {
                _closedInstanceCount++;
                LoggingService.Info("AUTO-CLOSER", $"✓ Successfully closed instance: {instanceId}");
            }
            else
            {
                LoggingService.Warn("AUTO-CLOSER", $"✗ Failed to close instance: {instanceId}");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            LoggingService.Error("AUTO-CLOSER", ex, $"Error closing instance: {instanceId}");
            return false;
        }
    }
    
    private async Task CheckInstancesAsync()
    {
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            return;
        }
        
        var settings = _settingsService.Settings;
        
        try
        {
            LoggingService.Debug("AUTO-CLOSER", "Checking group instances...");
            
            var instances = await GetActiveInstancesAsync();
            
            if (instances.Count == 0)
            {
                StatusChanged?.Invoke(this, $"✓ No active instances | Closed: {_closedInstanceCount}");
                return;
            }
            
            var allowedRegions = string.IsNullOrWhiteSpace(settings.AutoCloserAllowedRegions)
                ? null
                : settings.AutoCloserAllowedRegions.Split(',').Select(r => r.Trim().ToLower()).ToList();
            
            foreach (var instance in instances)
            {
                var shouldClose = false;
                var reason = "";
                
                // Check age gate requirement
                if (settings.AutoCloserRequireAgeGate && !instance.AgeGated)
                {
                    shouldClose = true;
                    reason = "Instance is not age-gated (18+)";
                }
                
                // Check region restriction
                if (!shouldClose && allowedRegions != null && allowedRegions.Count > 0)
                {
                    if (!allowedRegions.Contains(instance.Region.ToLower()))
                    {
                        shouldClose = true;
                        reason = $"Instance region '{instance.Region}' is not in allowed regions";
                    }
                }
                
                if (shouldClose)
                {
                    LoggingService.Warn("AUTO-CLOSER", $"Closing instance: {instance.WorldName} ({instance.InstanceId}) - {reason}");
                    
                    var closed = await CloseInstanceAsync(instance.InstanceId);
                    
                    if (closed)
                    {
                        var eventArgs = new AutoCloserEventArgs
                        {
                            InstanceId = instance.InstanceId,
                            WorldName = instance.WorldName,
                            Reason = reason,
                            ClosedAt = DateTime.UtcNow
                        };
                        
                        InstanceClosed?.Invoke(this, eventArgs);
                        
                        // Send Discord notification
                        if (settings.AutoCloserNotifyDiscord && _discordService.IsConfigured)
                        {
                            await SendDiscordNotificationAsync(instance, reason);
                        }
                    }
                    
                    // Rate limit between closes
                    await Task.Delay(1000);
                }
            }
            
            var nonCompliantCount = instances.Count(i => 
                (settings.AutoCloserRequireAgeGate && !i.AgeGated) ||
                (allowedRegions != null && !allowedRegions.Contains(i.Region.ToLower())));
            
            StatusChanged?.Invoke(this, $"✓ Checked {instances.Count} instances | Closed: {_closedInstanceCount}");
        }
        catch (Exception ex)
        {
            LoggingService.Error("AUTO-CLOSER", ex, "Error during instance check");
            StatusChanged?.Invoke(this, $"✗ Error: {ex.Message}");
        }
    }
    
    private async Task SendDiscordNotificationAsync(GroupInstanceInfo instance, string reason)
    {
        try
        {
            if (_discordService is DiscordWebhookService discordSvc)
            {
                // Build description with instance details
                var description = $"An instance was automatically closed by VRCGT Auto Closer\n\n" +
                    $"**World:** {instance.WorldName}\n" +
                    $"**Region:** {instance.Region}\n" +
                    $"**Age Gated:** {(instance.AgeGated ? "Yes" : "No")}\n" +
                    $"**Reason:** {reason}\n" +
                    $"**Instance ID:** `{instance.InstanceId}`";
                
                await discordSvc.SendMessageAsync("🚫 Instance Auto-Closed", description, 0xFF5722, null, _currentGroupId);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("AUTO-CLOSER", ex, "Failed to send Discord notification");
        }
    }
    
    public void Dispose()
    {
        StopMonitoring();
    }
}
