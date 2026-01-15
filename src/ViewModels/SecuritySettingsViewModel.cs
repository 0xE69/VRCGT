using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class SecuritySettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ISecurityMonitorService _securityMonitor;
    private readonly IVRChatApiService _apiService;
    private readonly IDiscordWebhookService _discordService;

    [ObservableProperty]
    private bool _securityMonitoringEnabled;

    [ObservableProperty]
    private bool _securityAutoRemoveRoles;

    [ObservableProperty]
    private string? _securityAlertWebhookUrl;

    // Instance kick settings
    [ObservableProperty]
    private bool _securityMonitorInstanceKicks;

    [ObservableProperty]
    private int _securityInstanceKickThreshold;

    [ObservableProperty]
    private int _securityInstanceKickTimeframeMinutes;

    // Group kick settings
    [ObservableProperty]
    private bool _securityMonitorGroupKicks;

    [ObservableProperty]
    private int _securityGroupKickThreshold;

    [ObservableProperty]
    private int _securityGroupKickTimeframeMinutes;

    // Instance ban settings
    [ObservableProperty]
    private bool _securityMonitorInstanceBans;

    [ObservableProperty]
    private int _securityInstanceBanThreshold;

    [ObservableProperty]
    private int _securityInstanceBanTimeframeMinutes;

    // Group ban settings
    [ObservableProperty]
    private bool _securityMonitorGroupBans;

    [ObservableProperty]
    private int _securityGroupBanThreshold;

    [ObservableProperty]
    private int _securityGroupBanTimeframeMinutes;

    // Role removal settings
    [ObservableProperty]
    private bool _securityMonitorRoleRemovals;

    [ObservableProperty]
    private int _securityRoleRemovalThreshold;

    [ObservableProperty]
    private int _securityRoleRemovalTimeframeMinutes;

    // Invite rejection settings
    [ObservableProperty]
    private bool _securityMonitorInviteRejections;

    [ObservableProperty]
    private int _securityInviteRejectionThreshold;

    [ObservableProperty]
    private int _securityInviteRejectionTimeframeMinutes;

    // Post deletion settings
    [ObservableProperty]
    private bool _securityMonitorPostDeletions;

    [ObservableProperty]
    private int _securityPostDeletionThreshold;

    [ObservableProperty]
    private int _securityPostDeletionTimeframeMinutes;

    // General settings
    [ObservableProperty]
    private bool _securityRequireOwnerRole;

    [ObservableProperty]
    private bool _securityNotifyDiscord;

    [ObservableProperty]
    private bool _securityLogAllActions;

    [ObservableProperty]
    private string _securityOwnerUserId = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isLoadingIncidents;

    [ObservableProperty]
    private ObservableCollection<SecurityIncidentViewModel> _recentIncidents = new();

    public SecuritySettingsViewModel(
        ISettingsService settingsService,
        ISecurityMonitorService securityMonitor,
        IVRChatApiService apiService,
        IDiscordWebhookService discordService)
    {
        _settingsService = settingsService;
        _securityMonitor = securityMonitor;
        _apiService = apiService;
        _discordService = discordService;

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;

        SecurityMonitoringEnabled = settings.SecurityMonitoringEnabled;
        SecurityAutoRemoveRoles = settings.SecurityAutoRemoveRoles;
        SecurityAlertWebhookUrl = settings.SecurityAlertWebhookUrl;

        SecurityMonitorInstanceKicks = settings.SecurityMonitorInstanceKicks;
        SecurityInstanceKickThreshold = settings.SecurityInstanceKickThreshold;
        SecurityInstanceKickTimeframeMinutes = settings.SecurityInstanceKickTimeframeMinutes;

        SecurityMonitorGroupKicks = settings.SecurityMonitorGroupKicks;
        SecurityGroupKickThreshold = settings.SecurityGroupKickThreshold;
        SecurityGroupKickTimeframeMinutes = settings.SecurityGroupKickTimeframeMinutes;

        SecurityMonitorInstanceBans = settings.SecurityMonitorInstanceBans;
        SecurityInstanceBanThreshold = settings.SecurityInstanceBanThreshold;
        SecurityInstanceBanTimeframeMinutes = settings.SecurityInstanceBanTimeframeMinutes;

        SecurityMonitorGroupBans = settings.SecurityMonitorGroupBans;
        SecurityGroupBanThreshold = settings.SecurityGroupBanThreshold;
        SecurityGroupBanTimeframeMinutes = settings.SecurityGroupBanTimeframeMinutes;

        SecurityMonitorRoleRemovals = settings.SecurityMonitorRoleRemovals;
        SecurityRoleRemovalThreshold = settings.SecurityRoleRemovalThreshold;
        SecurityRoleRemovalTimeframeMinutes = settings.SecurityRoleRemovalTimeframeMinutes;

        SecurityMonitorInviteRejections = settings.SecurityMonitorInviteRejections;
        SecurityInviteRejectionThreshold = settings.SecurityInviteRejectionThreshold;
        SecurityInviteRejectionTimeframeMinutes = settings.SecurityInviteRejectionTimeframeMinutes;

        SecurityMonitorPostDeletions = settings.SecurityMonitorPostDeletions;
        SecurityPostDeletionThreshold = settings.SecurityPostDeletionThreshold;
        SecurityPostDeletionTimeframeMinutes = settings.SecurityPostDeletionTimeframeMinutes;

        SecurityRequireOwnerRole = settings.SecurityRequireOwnerRole;
        SecurityNotifyDiscord = settings.SecurityNotifyDiscord;
        SecurityLogAllActions = settings.SecurityLogAllActions;
        SecurityOwnerUserId = settings.SecurityOwnerUserId ?? "";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = _settingsService.Settings;

            settings.SecurityMonitoringEnabled = SecurityMonitoringEnabled;
            settings.SecurityAutoRemoveRoles = SecurityAutoRemoveRoles;
            settings.SecurityAlertWebhookUrl = SecurityAlertWebhookUrl;

            settings.SecurityMonitorInstanceKicks = SecurityMonitorInstanceKicks;
            settings.SecurityInstanceKickThreshold = SecurityInstanceKickThreshold;
            settings.SecurityInstanceKickTimeframeMinutes = SecurityInstanceKickTimeframeMinutes;

            settings.SecurityMonitorGroupKicks = SecurityMonitorGroupKicks;
            settings.SecurityGroupKickThreshold = SecurityGroupKickThreshold;
            settings.SecurityGroupKickTimeframeMinutes = SecurityGroupKickTimeframeMinutes;

            settings.SecurityMonitorInstanceBans = SecurityMonitorInstanceBans;
            settings.SecurityInstanceBanThreshold = SecurityInstanceBanThreshold;
            settings.SecurityInstanceBanTimeframeMinutes = SecurityInstanceBanTimeframeMinutes;

            settings.SecurityMonitorGroupBans = SecurityMonitorGroupBans;
            settings.SecurityGroupBanThreshold = SecurityGroupBanThreshold;
            settings.SecurityGroupBanTimeframeMinutes = SecurityGroupBanTimeframeMinutes;

            settings.SecurityMonitorRoleRemovals = SecurityMonitorRoleRemovals;
            settings.SecurityRoleRemovalThreshold = SecurityRoleRemovalThreshold;
            settings.SecurityRoleRemovalTimeframeMinutes = SecurityRoleRemovalTimeframeMinutes;

            settings.SecurityMonitorInviteRejections = SecurityMonitorInviteRejections;
            settings.SecurityInviteRejectionThreshold = SecurityInviteRejectionThreshold;
            settings.SecurityInviteRejectionTimeframeMinutes = SecurityInviteRejectionTimeframeMinutes;

            settings.SecurityMonitorPostDeletions = SecurityMonitorPostDeletions;
            settings.SecurityPostDeletionThreshold = SecurityPostDeletionThreshold;
            settings.SecurityPostDeletionTimeframeMinutes = SecurityPostDeletionTimeframeMinutes;

            settings.SecurityRequireOwnerRole = SecurityRequireOwnerRole;
            settings.SecurityNotifyDiscord = SecurityNotifyDiscord;
            settings.SecurityLogAllActions = SecurityLogAllActions;
            settings.SecurityOwnerUserId = SecurityOwnerUserId;

            _settingsService.Save();

            StatusMessage = "✓ Security settings saved successfully!";
            LoggingService.Info("SECURITY-UI", "Security settings saved");

            // Clear status after 3 seconds
            Task.Delay(3000).ContinueWith(_ => StatusMessage = "");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to save settings: {ex.Message}";
            LoggingService.Error("SECURITY-UI", ex, "Failed to save security settings");
        }
    }

    [RelayCommand]
    private async Task LoadRecentIncidentsAsync()
    {
        try
        {
            IsLoadingIncidents = true;
            RecentIncidents.Clear();

            var groupId = _settingsService.Settings.GroupId;
            if (string.IsNullOrEmpty(groupId))
            {
                StatusMessage = "⚠ Please set a Group ID first";
                return;
            }

            var incidents = await _securityMonitor.GetRecentIncidentsAsync(groupId, 30); // Last 30 days

            foreach (var incident in incidents)
            {
                RecentIncidents.Add(new SecurityIncidentViewModel(incident, _securityMonitor));
            }

            StatusMessage = $"Loaded {incidents.Count} recent incidents";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to load incidents: {ex.Message}";
            LoggingService.Error("SECURITY-UI", ex, "Failed to load recent incidents");
        }
        finally
        {
            IsLoadingIncidents = false;
        }
    }

    [RelayCommand]
    private void SetCurrentUserAsOwner()
    {
        try
        {
            var currentUserId = _apiService.CurrentUserId;
            if (!string.IsNullOrEmpty(currentUserId))
            {
                SecurityOwnerUserId = currentUserId;
                StatusMessage = $"✓ Owner set to current user: {currentUserId}";
            }
            else
            {
                StatusMessage = "⚠ Current user ID not available";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to set owner: {ex.Message}";
            LoggingService.Error("SECURITY-UI", ex, "Failed to set current user as owner");
        }
    }

    [RelayCommand]
    private async Task TestWebhookAsync()
    {
        try
        {
            var webhookUrl = SecurityAlertWebhookUrl;
            if (string.IsNullOrEmpty(webhookUrl))
            {
                webhookUrl = _settingsService.Settings.DiscordWebhookUrl;
            }

            if (string.IsNullOrEmpty(webhookUrl))
            {
                StatusMessage = "⚠ Please enter a webhook URL first";
                return;
            }

            // Simple validation
            if (!webhookUrl.StartsWith("https://discord.com/api/webhooks/") &&
                !webhookUrl.StartsWith("https://discordapp.com/api/webhooks/"))
            {
                StatusMessage = "⚠ Invalid Discord webhook URL format";
                return;
            }

            StatusMessage = "Testing webhook...";
            var result = await _discordService.TestWebhookAsync(webhookUrl);

            if (result.Success)
            {
                StatusMessage = "✅ Webhook connected successfully!";
            }
            else
            {
                var status = result.StatusCode.HasValue ? $" (HTTP {result.StatusCode})" : "";
                var error = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" {result.ErrorMessage}";
                StatusMessage = $"❌ Failed to connect{status}.{error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
        }
    }
}

public partial class SecurityIncidentViewModel : ObservableObject
{
    private readonly SecurityIncidentEntity _incident;
    private readonly ISecurityMonitorService _securityMonitor;

    [ObservableProperty]
    private string _incidentId;

    [ObservableProperty]
    private string _actorDisplayName;

    [ObservableProperty]
    private string _actorUserId;

    [ObservableProperty]
    private string _incidentType;

    [ObservableProperty]
    private int _actionCount;

    [ObservableProperty]
    private int _threshold;

    [ObservableProperty]
    private int _timeframeMinutes;

    [ObservableProperty]
    private bool _rolesRemoved;

    [ObservableProperty]
    private bool _discordNotified;

    [ObservableProperty]
    private DateTime _detectedAt;

    [ObservableProperty]
    private bool _isResolved;

    [ObservableProperty]
    private string _details;

    public SecurityIncidentViewModel(SecurityIncidentEntity incident, ISecurityMonitorService securityMonitor)
    {
        _incident = incident;
        _securityMonitor = securityMonitor;

        IncidentId = incident.IncidentId;
        ActorDisplayName = incident.ActorDisplayName;
        ActorUserId = incident.ActorUserId;
        IncidentType = incident.IncidentType;
        ActionCount = incident.ActionCount;
        Threshold = incident.Threshold;
        TimeframeMinutes = incident.TimeframeMinutes;
        RolesRemoved = incident.RolesRemoved;
        DiscordNotified = incident.DiscordNotified;
        DetectedAt = incident.DetectedAt;
        IsResolved = incident.IsResolved;
        Details = incident.Details ?? "";
    }

    [RelayCommand]
    private async Task ResolveIncidentAsync()
    {
        var success = await _securityMonitor.ResolveIncidentAsync(IncidentId);
        if (success)
        {
            IsResolved = true;
        }
    }
}
