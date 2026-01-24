using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;

namespace VRCGroupTools.Services;

public interface ISecurityMonitorService
{
    Task<bool> TrackActionAsync(string groupId, string actorUserId, string actorDisplayName, string actionType, string? targetUserId = null, string? targetDisplayName = null, object? additionalData = null);
    Task<List<SecurityIncidentEntity>> GetRecentIncidentsAsync(string groupId, int daysBack = 7);
    Task<List<SecurityActionEntity>> GetUserActionsAsync(string groupId, string userId, int hoursBack = 24);
    Task<List<SecurityActionEntity>> GetAllSecurityActionsAsync(string groupId, int daysBack = 7);
    Task<int> ClearOldSecurityActionsAsync(string groupId, int daysOld = 7);
    Task<bool> ResolveIncidentAsync(string incidentId);
    Task<bool> IsEnabled { get; }
}

public class SecurityMonitorService : ISecurityMonitorService
{
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly IVRChatApiService _apiService;
    private readonly IDiscordWebhookService _discordService;

    public Task<bool> IsEnabled => Task.FromResult(_settingsService.Settings.SecurityMonitoringEnabled);

    public SecurityMonitorService(
        ICacheService cacheService,
        ISettingsService settingsService,
        IVRChatApiService apiService,
        IDiscordWebhookService discordService)
    {
        _cacheService = cacheService;
        _settingsService = settingsService;
        _apiService = apiService;
        _discordService = discordService;
    }

    public async Task<bool> TrackActionAsync(
        string groupId,
        string actorUserId,
        string actorDisplayName,
        string actionType,
        string? targetUserId = null,
        string? targetDisplayName = null,
        object? additionalData = null)
    {
        try
        {
            var settings = _settingsService.Settings;
            
            // Check if monitoring is enabled
            if (!settings.SecurityMonitoringEnabled)
            {
                return false;
            }

            // Check if this action type is monitored
            bool shouldMonitor = actionType.ToLower() switch
            {
                "instance_kick" => settings.SecurityMonitorInstanceKicks,
                "group_kick" => settings.SecurityMonitorGroupKicks,
                "instance_ban" => settings.SecurityMonitorInstanceBans,
                "group_ban" => settings.SecurityMonitorGroupBans,
                "role_remove" or "group.role.remove" => settings.SecurityMonitorRoleRemovals,
                "invite_reject" or "group.invite.reject" => settings.SecurityMonitorInviteRejections,
                "post_delete" or "group.post.delete" or "group.announcement.delete" or "group.gallery.delete" => settings.SecurityMonitorPostDeletions,
                _ => false
            };

            if (!shouldMonitor)
            {
                return false;
            }

            // Log the action if enabled
            if (settings.SecurityLogAllActions)
            {
                LoggingService.Info("SECURITY", $"Tracking action: {actionType} by {actorDisplayName} ({actorUserId})");
            }

            // Save the action to database
            using var context = new AppDbContext();
            var action = new SecurityActionEntity
            {
                GroupId = groupId,
                ActorUserId = actorUserId,
                ActorDisplayName = actorDisplayName,
                ActionType = actionType,
                TargetUserId = targetUserId,
                TargetDisplayName = targetDisplayName,
                ActionTime = DateTime.UtcNow,
                AdditionalData = additionalData != null ? JsonConvert.SerializeObject(additionalData) : null
            };

            context.SecurityActions.Add(action);
            await context.SaveChangesAsync();

            // Check if threshold is exceeded
            await CheckThresholdsAsync(groupId, actorUserId, actorDisplayName, actionType);

            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, $"Failed to track action: {actionType}");
            return false;
        }
    }

    private async Task CheckThresholdsAsync(string groupId, string actorUserId, string actorDisplayName, string actionType)
    {
        try
        {
            var settings = _settingsService.Settings;

            // Determine threshold and timeframe based on action type
            (int threshold, int timeframeMinutes, string incidentType) = actionType.ToLower() switch
            {
                "instance_kick" => (settings.SecurityInstanceKickThreshold, settings.SecurityInstanceKickTimeframeMinutes, "excessive_instance_kicks"),
                "group_kick" => (settings.SecurityGroupKickThreshold, settings.SecurityGroupKickTimeframeMinutes, "excessive_group_kicks"),
                "instance_ban" => (settings.SecurityInstanceBanThreshold, settings.SecurityInstanceBanTimeframeMinutes, "excessive_instance_bans"),
                "group_ban" => (settings.SecurityGroupBanThreshold, settings.SecurityGroupBanTimeframeMinutes, "excessive_group_bans"),
                "role_remove" or "group.role.remove" => (settings.SecurityRoleRemovalThreshold, settings.SecurityRoleRemovalTimeframeMinutes, "excessive_role_removals"),
                "invite_reject" or "group.invite.reject" => (settings.SecurityInviteRejectionThreshold, settings.SecurityInviteRejectionTimeframeMinutes, "excessive_invite_rejections"),
                "post_delete" or "group.post.delete" or "group.announcement.delete" or "group.gallery.delete" => (settings.SecurityPostDeletionThreshold, settings.SecurityPostDeletionTimeframeMinutes, "excessive_deletions"),
                _ => (0, 0, "unknown")
            };

            if (threshold == 0 || timeframeMinutes == 0)
            {
                return;
            }

            // Get recent actions of this type by this user
            var cutoffTime = DateTime.UtcNow.AddMinutes(-timeframeMinutes);
            using var context = new AppDbContext();
            
            var recentActions = await context.SecurityActions
                .Where(a => a.GroupId == groupId 
                    && a.ActorUserId == actorUserId 
                    && a.ActionType == actionType
                    && a.ActionTime >= cutoffTime)
                .OrderByDescending(a => a.ActionTime)
                .ToListAsync();

            var actionCount = recentActions.Count;

            LoggingService.Debug("SECURITY", $"User {actorDisplayName} has {actionCount}/{threshold} {actionType} actions in last {timeframeMinutes} minutes");

            // Check if threshold exceeded
            if (actionCount >= threshold)
            {
                LoggingService.Warn("SECURITY", $"⚠️ THRESHOLD EXCEEDED: {actorDisplayName} performed {actionCount} {actionType} actions in {timeframeMinutes} minutes (threshold: {threshold})");

                // Check if we already created an incident for this recently (avoid duplicates)
                var recentIncident = await context.SecurityIncidents
                    .Where(i => i.GroupId == groupId
                        && i.ActorUserId == actorUserId
                        && i.IncidentType == incidentType
                        && i.DetectedAt >= cutoffTime)
                    .FirstOrDefaultAsync();

                if (recentIncident != null)
                {
                    LoggingService.Debug("SECURITY", "Similar incident already exists, skipping duplicate");
                    return;
                }

                // Create incident
                var incident = new SecurityIncidentEntity
                {
                    IncidentId = Guid.NewGuid().ToString(),
                    GroupId = groupId,
                    ActorUserId = actorUserId,
                    ActorDisplayName = actorDisplayName,
                    IncidentType = incidentType,
                    ActionCount = actionCount,
                    TimeframeMinutes = timeframeMinutes,
                    Threshold = threshold,
                    RolesRemoved = false,
                    DiscordNotified = false,
                    DetectedAt = DateTime.UtcNow,
                    Details = $"User performed {actionCount} {actionType} actions within {timeframeMinutes} minutes, exceeding threshold of {threshold}"
                };

                context.SecurityIncidents.Add(incident);
                await context.SaveChangesAsync();

                // Take action if auto-remove is enabled
                if (settings.SecurityAutoRemoveRoles)
                {
                    await RemoveUserRolesAsync(groupId, actorUserId, actorDisplayName, incident);
                }

                // Send Discord notification
                if (settings.SecurityNotifyDiscord)
                {
                    await SendSecurityAlertAsync(incident, recentActions);
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to check thresholds");
        }
    }

    private async Task RemoveUserRolesAsync(string groupId, string userId, string displayName, SecurityIncidentEntity incident)
    {
        try
        {
            var settings = _settingsService.Settings;

            // Check if owner role requirement is enabled
            if (settings.SecurityRequireOwnerRole)
            {
                var currentUser = _apiService.CurrentUserId;
                if (string.IsNullOrEmpty(currentUser))
                {
                    LoggingService.Warn("SECURITY", "Cannot remove roles: Current user ID is not available");
                    incident.Details += "\n⚠️ Failed to remove roles: Current user not authenticated";
                    return;
                }

                // Optionally check if current user is the designated owner
                if (!string.IsNullOrEmpty(settings.SecurityOwnerUserId) && currentUser != settings.SecurityOwnerUserId)
                {
                    LoggingService.Warn("SECURITY", $"Cannot remove roles: Only the designated owner ({settings.SecurityOwnerUserId}) can remove roles");
                    incident.Details += $"\n⚠️ Failed to remove roles: Only the designated owner can perform this action";
                    return;
                }
            }

            LoggingService.Info("SECURITY", $"Attempting to remove roles from user: {displayName} ({userId})");

            // Get the user's current roles in the group
            var memberRoles = await _apiService.GetGroupMemberRolesAsync(groupId, userId);
            
            if (memberRoles == null || memberRoles.Count == 0)
            {
                LoggingService.Warn("SECURITY", $"User {displayName} has no roles to remove");
                incident.Details += "\n⚠️ User had no roles to remove";
                return;
            }

            var removedRoleIds = new List<string>();

            // Remove each role
            foreach (var roleId in memberRoles)
            {
                try
                {
                    var success = await _apiService.RemoveGroupMemberRoleAsync(groupId, userId, roleId);
                    if (success)
                    {
                        removedRoleIds.Add(roleId);
                        LoggingService.Info("SECURITY", $"✓ Removed role {roleId} from {displayName}");
                    }
                    else
                    {
                        LoggingService.Warn("SECURITY", $"✗ Failed to remove role {roleId} from {displayName}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Error("SECURITY", ex, $"Error removing role {roleId} from {displayName}");
                }
            }

            // Update incident
            if (removedRoleIds.Count > 0)
            {
                using var context = new AppDbContext();
                var dbIncident = await context.SecurityIncidents.FirstOrDefaultAsync(i => i.IncidentId == incident.IncidentId);
                if (dbIncident != null)
                {
                    dbIncident.RolesRemoved = true;
                    dbIncident.RemovedRoleIds = string.Join(",", removedRoleIds);
                    dbIncident.Details += $"\n✓ Automatically removed {removedRoleIds.Count} role(s) from user";
                    await context.SaveChangesAsync();
                }

                LoggingService.Info("SECURITY", $"✓ Successfully removed {removedRoleIds.Count} roles from {displayName}");
            }
            else
            {
                LoggingService.Warn("SECURITY", "No roles were successfully removed");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, $"Failed to remove roles from user {userId}");
            incident.Details += $"\n✗ Error removing roles: {ex.Message}";
        }
    }

    private async Task SendSecurityAlertAsync(SecurityIncidentEntity incident, List<SecurityActionEntity> recentActions)
    {
        try
        {
            var settings = _settingsService.Settings;
            string? webhookUrl = !string.IsNullOrEmpty(settings.SecurityAlertWebhookUrl) 
                ? settings.SecurityAlertWebhookUrl 
                : settings.DiscordWebhookUrl;

            if (string.IsNullOrEmpty(webhookUrl))
            {
                LoggingService.Warn("SECURITY", "No Discord webhook URL configured for security alerts");
                return;
            }

            // Create embed for Discord
            var embed = new
            {
                title = $"🚨 Security Alert: {incident.IncidentType.Replace("_", " ").ToUpper()}",
                description = incident.Details,
                color = 0xFF0000, // Red
                fields = new[]
                {
                    new { name = "User", value = $"{incident.ActorDisplayName}\n`{incident.ActorUserId}`", inline = true },
                    new { name = "Action Count", value = $"{incident.ActionCount}/{incident.Threshold}", inline = true },
                    new { name = "Timeframe", value = $"{incident.TimeframeMinutes} minutes", inline = true },
                    new { name = "Roles Removed", value = incident.RolesRemoved ? "✓ Yes" : "✗ No", inline = true },
                    new { name = "Detected At", value = incident.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"), inline = false }
                },
                footer = new { text = "VRC Group Tools - Security Monitor" },
                timestamp = incident.DetectedAt.ToString("o")
            };

            // Add recent actions as a field
            if (recentActions.Count > 0)
            {
                var actionsList = string.Join("\n", recentActions.Take(10).Select(a => 
                    $"• {a.ActionTime:HH:mm:ss} - {a.ActionType} → {a.TargetDisplayName ?? "N/A"}"));
                
                var fieldsList = embed.fields.ToList();
                fieldsList.Add(new { name = $"Recent Actions ({recentActions.Count} total)", value = actionsList, inline = false });
                
                embed = new
                {
                    embed.title,
                    embed.description,
                    embed.color,
                    fields = fieldsList.ToArray(),
                    embed.footer,
                    embed.timestamp
                };
            }

            var payload = new
            {
                content = "⚠️ **SECURITY INCIDENT DETECTED** ⚠️",
                embeds = new[] { embed }
            };

            var success = await _discordService.SendWebhookAsync(webhookUrl, payload);

            if (success)
            {
                using var context = new AppDbContext();
                var dbIncident = await context.SecurityIncidents.FirstOrDefaultAsync(i => i.IncidentId == incident.IncidentId);
                if (dbIncident != null)
                {
                    dbIncident.DiscordNotified = true;
                    dbIncident.DiscordNotifiedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }

                LoggingService.Info("SECURITY", "✓ Security alert sent to Discord");
            }
            else
            {
                LoggingService.Warn("SECURITY", "Failed to send security alert to Discord");
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to send security alert");
        }
    }

    public async Task<List<SecurityIncidentEntity>> GetRecentIncidentsAsync(string groupId, int daysBack = 7)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysBack);
            using var context = new AppDbContext();
            
            return await context.SecurityIncidents
                .Where(i => i.GroupId == groupId && i.DetectedAt >= cutoffDate)
                .OrderByDescending(i => i.DetectedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to get recent incidents");
            return new List<SecurityIncidentEntity>();
        }
    }

    public async Task<List<SecurityActionEntity>> GetUserActionsAsync(string groupId, string userId, int hoursBack = 24)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-hoursBack);
            using var context = new AppDbContext();
            
            return await context.SecurityActions
                .Where(a => a.GroupId == groupId 
                    && a.ActorUserId == userId 
                    && a.ActionTime >= cutoffTime)
                .OrderByDescending(a => a.ActionTime)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to get user actions");
            return new List<SecurityActionEntity>();
        }
    }

    public async Task<bool> ResolveIncidentAsync(string incidentId)
    {
        try
        {
            using var context = new AppDbContext();
            var incident = await context.SecurityIncidents.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            
            if (incident != null)
            {
                incident.IsResolved = true;
                incident.ResolvedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                
                LoggingService.Info("SECURITY", $"Incident {incidentId} marked as resolved");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, $"Failed to resolve incident {incidentId}");
            return false;
        }
    }

    public async Task<List<SecurityActionEntity>> GetAllSecurityActionsAsync(string groupId, int daysBack = 7)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-daysBack);
            using var context = new AppDbContext();
            
            return await context.SecurityActions
                .Where(a => a.GroupId == groupId && a.ActionTime >= cutoffTime)
                .OrderByDescending(a => a.ActionTime)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to get all security actions");
            return new List<SecurityActionEntity>();
        }
    }

    public async Task<int> ClearOldSecurityActionsAsync(string groupId, int daysOld = 7)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-daysOld);
            using var context = new AppDbContext();
            
            var oldActions = await context.SecurityActions
                .Where(a => a.GroupId == groupId && a.ActionTime < cutoffTime)
                .ToListAsync();

            var count = oldActions.Count;
            if (count > 0)
            {
                context.SecurityActions.RemoveRange(oldActions);
                await context.SaveChangesAsync();
                LoggingService.Info("SECURITY", $"Cleared {count} old security actions (older than {daysOld} days)");
            }

            return count;
        }
        catch (Exception ex)
        {
            LoggingService.Error("SECURITY", ex, "Failed to clear old security actions");
            return 0;
        }
    }
}
