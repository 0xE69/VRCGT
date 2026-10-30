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
    Task<bool> CloseInstanceAsync(string worldId, string instanceId);
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
    public string? AccessType { get; set; }
}

public class AutoCloserService : IAutoCloserService, IDisposable
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;

    // Tracks instances we recently closed. VRChat keeps soft-closed instances listed in the
    // group-instances response (and returns 403 "not allowed to close an already closed
    // instance" on a re-close attempt), so we skip them for a cooldown window instead of
    // re-attempting the close every polling cycle.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _recentlyClosedInstances = new();
    private static readonly TimeSpan ClosedInstanceCooldown = TimeSpan.FromMinutes(10);
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
                        CreatedAt = instance.CreatedAt,
                        AccessType = instance.AccessType
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
    
    public async Task<bool> CloseInstanceAsync(string worldId, string instanceId)
    {
        try
        {
            if (string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(instanceId))
            {
                return false;
            }
            
            LoggingService.Info("AUTO-CLOSER", $"Attempting to close instance: {instanceId}");
            
            var success = await _apiService.CloseGroupInstanceAsync(worldId, instanceId);
            
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
            
            var allowedAccess = string.IsNullOrWhiteSpace(settings.AutoCloserAllowedAccessTypes)
                ? null
                : settings.AutoCloserAllowedAccessTypes.Split(',').Select(a => a.Trim().ToLower()).Where(a => a.Length > 0).ToList();
            
            foreach (var instance in instances)
            {
                // Skip instances we recently closed — VRChat keeps soft-closed instances
                // listed and returns 403 on re-close, so avoid re-attempting every cycle.
                if (IsRecentlyClosed(instance.InstanceId))
                {
                    LoggingService.Debug("AUTO-CLOSER", $"Skipping recently-closed instance: {instance.InstanceId}");
                    continue;
                }

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
                    if (!IsRegionAllowed(instance.Region, allowedRegions))
                    {
                        shouldClose = true;
                        reason = $"Instance region '{instance.Region}' is not in allowed regions";
                    }
                }
                
                // Check access type restriction (e.g., only allow Group / Group+, close Public)
                if (!shouldClose && allowedAccess != null && allowedAccess.Count > 0)
                {
                    if (!IsAccessAllowed(instance.AccessType, allowedAccess))
                    {
                        shouldClose = true;
                        reason = $"Instance access '{instance.AccessType ?? "unknown"}' is not in allowed access types";
                    }
                }
                
                if (shouldClose)
                {
                    LoggingService.Warn("AUTO-CLOSER", $"Closing instance: {instance.WorldName} ({instance.InstanceId}) - {reason}");
                    
                    var success = await CloseInstanceAsync(instance.WorldId, instance.InstanceId);
                    
                    if (success)
                    {
                        MarkInstanceClosed(instance.InstanceId);

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
                (allowedRegions != null && !IsRegionAllowed(i.Region, allowedRegions)) ||
                (allowedAccess != null && !IsAccessAllowed(i.AccessType, allowedAccess)));
            
            StatusChanged?.Invoke(this, $"✓ Checked {instances.Count} instances | Closed: {_closedInstanceCount}");
        }
        catch (Exception ex)
        {
            LoggingService.Error("AUTO-CLOSER", ex, "Error during instance check");
            StatusChanged?.Invoke(this, $"✗ Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Returns true if the given instance was closed within the cooldown window, so the
    /// auto-closer skips re-attempting it (VRChat keeps soft-closed instances listed).
    /// </summary>
    private bool IsRecentlyClosed(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return false;
        return _recentlyClosedInstances.TryGetValue(instanceId, out var closedAt) &&
               (DateTime.UtcNow - closedAt) < ClosedInstanceCooldown;
    }

    /// <summary>
    /// Records a successful close so the instance is skipped during the cooldown window.
    /// Also prunes stale entries to keep the dictionary small.
    /// </summary>
    private void MarkInstanceClosed(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return;
        _recentlyClosedInstances[instanceId] = DateTime.UtcNow;

        var cutoff = DateTime.UtcNow - ClosedInstanceCooldown;
        foreach (var kvp in _recentlyClosedInstances)
        {
            if (kvp.Value < cutoff)
                _recentlyClosedInstances.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Normalizes access type values so both the app's vocabulary (members/plus/public)
    /// and VRChat's raw values (group/group_fof/friends/private) compare consistently.
    /// </summary>
    private static string? NormalizeAccess(string? access)
    {
        var value = (access ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "members" or "group" => "group",
            "plus" or "group_fof" => "plus",
            "public" => "public",
            "friends" => "friends",
            "private" => "private",
            _ => value.Length > 0 ? value : null
        };
    }
    
    private static bool IsAccessAllowed(string? access, List<string> allowed)
    {
        var normalized = NormalizeAccess(access);
        // Leave instances with an unknown/missing access type alone — safer not to close what we can't identify
        if (normalized == null)
        {
            return true;
        }
        return allowed.Any(a => NormalizeAccess(a) == normalized);
    }

    /// <summary>
    /// Normalizes a region value so "us_west", "us-west" and "US West" all compare
    /// consistently as "us_west".
    /// </summary>
    private static string? NormalizeRegion(string? region)
    {
        var value = (region ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return null;
        value = value.Replace('-', '_').Replace(' ', '_');
        while (value.Contains("__")) value = value.Replace("__", "_");
        return value;
    }

    /// <summary>
    /// True if the instance's region is within the allowed set. Handles the fine-grained
    /// US regions (us_west / us_east) and keeps legacy "us" settings working by treating
    /// them as a prefix covering both US sub-regions. A null/empty allowed list means
    /// "allow all".
    /// </summary>
    private static bool IsRegionAllowed(string? region, List<string>? allowed)
    {
        if (allowed == null || allowed.Count == 0) return true;
        var r = NormalizeRegion(region);
        if (r == null) return false;
        return allowed.Any(a =>
        {
            var allowedRegion = NormalizeRegion(a);
            if (allowedRegion == null) return false;
            // Exact match, or the instance region is a sub-region of an allowed one
            // (so a legacy "us" still covers "us_west" / "us_east").
            return r == allowedRegion
                || r.StartsWith(allowedRegion + "_", StringComparison.Ordinal);
        });
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
