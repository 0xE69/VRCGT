using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace VRCGroupTools.Services;

public interface IDiscordWebhookService
{
    Task<bool> SendMessageAsync(string title, string description, int color, string? thumbnailUrl = null, string? groupId = null);
    Task<WebhookTestResult> TestWebhookAsync(string webhookUrl);
    Task<bool> SendModerationActionAsync(string actionType, string targetUserId, string targetName, string actorName, string reason, string? description, DateTime actionTime, DateTime? expiresAt = null, int? infractionCount = null, string? groupId = null);
    Task<bool> SendWebhookAsync(string webhookUrl, object payload);
    bool IsConfigured { get; }
    bool IsConfiguredForGroup(string? groupId);
    Task<bool> SendAuditEventAsync(string eventType, string actorName, string? targetName, string? description, string? groupId = null);
    bool ShouldSendAuditEvent(string eventType, string? groupId = null);
    string? GetWebhookUrlForEventType(string eventType, string? groupId = null);
}

public class WebhookTestResult
{
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DiscordWebhookService : IDiscordWebhookService
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settingsService.Settings.DiscordWebhookUrl);

    public bool IsConfiguredForGroup(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId)) return IsConfigured;

        var groupConfig = _settingsService.GetGroupConfig(groupId);
        if (groupConfig == null) return IsConfigured;

        // Minimal improvement: consider category webhooks as configured too
        return
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookUrl) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookMemberEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookRoleEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookInstanceEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookGroupEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookInviteEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookAnnouncementEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookGalleryEvents) ||
            !string.IsNullOrWhiteSpace(groupConfig.DiscordWebhookPostEvents) ||
            !string.IsNullOrWhiteSpace(_settingsService.Settings.DiscordWebhookUrl);
    }

    public DiscordWebhookService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient();
    }

    public async Task<bool> SendMessageAsync(string title, string description, int color, string? thumbnailUrl = null, string? groupId = null)
    {
        Console.WriteLine($"[DISCORD] SendMessageAsync called - Title: {title}");

        string? webhookUrl = null;

        if (!string.IsNullOrEmpty(groupId))
        {
            var eventType = ExtractEventTypeFromTitle(title);
            if (!string.IsNullOrEmpty(eventType))
            {
                // normalize event type so routing matches settings
                eventType = NormalizeEventType(eventType);
                webhookUrl = GetWebhookUrlForEventType(eventType, groupId);
            }

            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                webhookUrl = _settingsService.GetGroupConfig(groupId)?.DiscordWebhookUrl;
            }
        }

        // ✅ Always allow a final fallback to global webhook
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            webhookUrl = _settingsService.Settings.DiscordWebhookUrl;
        }

        webhookUrl = NormalizeWebhookUrl(webhookUrl);

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            Console.WriteLine("[DISCORD] Not configured, webhook URL is missing");
            return false;
        }

        try
        {
            // avoid empty embed description errors
            if (string.IsNullOrWhiteSpace(description))
                description = "\u200B";

            var embed = new
            {
                title = title,
                description = description,
                color = color,
                timestamp = DateTime.UtcNow.ToString("o"),
                thumbnail = thumbnailUrl != null ? new { url = thumbnailUrl } : null,
                footer = new { text = "VRC Group Tools" }
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(webhookUrl, content);
            Console.WriteLine($"[DISCORD] HTTP Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[DISCORD] Error response: {errorBody}");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCORD] Failed to send message: {ex.Message}");
            return false;
        }
    }

    public async Task<WebhookTestResult> TestWebhookAsync(string webhookUrl)
    {
        webhookUrl = NormalizeWebhookUrl(webhookUrl);

        if (string.IsNullOrWhiteSpace(webhookUrl))
            return new WebhookTestResult { Success = false, ErrorMessage = "Webhook URL is empty" };

        try
        {
            var embed = new
            {
                title = "✅ Webhook Connected!",
                description = "VRC Group Tools is now connected to this channel.\n\nYou will receive notifications based on your settings.",
                color = 0x4CAF50,
                timestamp = DateTime.UtcNow.ToString("o"),
                footer = new { text = "VRC Group Tools" }
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(webhookUrl, content);
            if (response.IsSuccessStatusCode)
            {
                return new WebhookTestResult { Success = true, StatusCode = (int)response.StatusCode };
            }

            var body = await response.Content.ReadAsStringAsync();
            return new WebhookTestResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                ErrorMessage = string.IsNullOrWhiteSpace(body) ? response.StatusCode.ToString() : body
            };
        }
        catch (Exception ex)
        {
            return new WebhookTestResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> SendWebhookAsync(string webhookUrl, object payload)
    {
        webhookUrl = NormalizeWebhookUrl(webhookUrl);

        if (string.IsNullOrWhiteSpace(webhookUrl))
            return false;

        try
        {
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCORD] SendWebhookAsync failed: {ex.Message}");
            return false;
        }
    }

    public bool ShouldSendAuditEvent(string eventType, string? groupId = null)
    {
        // ✅ Normalize event type so toggle mapping works
        var normalized = NormalizeEventType(eventType);

        // Group-specific settings
        if (!string.IsNullOrEmpty(groupId))
        {
            var groupConfig = _settingsService.GetGroupConfig(groupId);
            if (groupConfig != null)
            {
                return normalized switch
                {
                    // Member Events
                    "group.user.join" => groupConfig.DiscordNotifyUserJoins,
                    "group.user.leave" => groupConfig.DiscordNotifyUserLeaves,
                    "group.user.kick" => groupConfig.DiscordNotifyUserKicked,
                    "group.user.ban" => groupConfig.DiscordNotifyUserBanned,
                    "group.user.unban" => groupConfig.DiscordNotifyUserUnbanned,
                    "group.user.role.add" => groupConfig.DiscordNotifyUserRoleAdd,
                    "group.user.role.remove" => groupConfig.DiscordNotifyUserRoleRemove,

                    // Join Request Events
                    "group.request.create" or "group.joinrequest" => groupConfig.DiscordNotifyJoinRequests,
                    "group.request.accept" => groupConfig.DiscordNotifyJoinRequests,
                    "group.request.reject" => groupConfig.DiscordNotifyJoinRequests,
                    "group.request.block" => groupConfig.DiscordNotifyJoinRequests,
                    "group.request.unblock" => groupConfig.DiscordNotifyJoinRequests,

                    // Role Events
                    "group.role.create" => groupConfig.DiscordNotifyRoleCreate,
                    "group.role.update" => groupConfig.DiscordNotifyRoleUpdate,
                    "group.role.delete" => groupConfig.DiscordNotifyRoleDelete,

                    // Instance Events
                    "group.instance.create" => groupConfig.DiscordNotifyInstanceCreate,
                    "group.instance.delete" => groupConfig.DiscordNotifyInstanceDelete,
                    "group.instance.open" => groupConfig.DiscordNotifyInstanceOpened,
                    "group.instance.close" => groupConfig.DiscordNotifyInstanceClosed,
                    "group.instance.warn" => groupConfig.DiscordNotifyInstanceWarn,

                    // Group Events
                    "group.update" => groupConfig.DiscordNotifyGroupUpdate,

                    // Invite Events
                    "group.invite.create" or "group.user.invite" => groupConfig.DiscordNotifyInviteCreate,
                    "group.invite.accept" => groupConfig.DiscordNotifyInviteAccept,
                    "group.invite.reject" => groupConfig.DiscordNotifyInviteReject,

                    // Announcement Events
                    "group.announcement.create" => groupConfig.DiscordNotifyAnnouncementCreate,
                    "group.announcement.delete" => groupConfig.DiscordNotifyAnnouncementDelete,

                    // Gallery Events
                    "group.gallery.create" => groupConfig.DiscordNotifyGalleryCreate,
                    "group.gallery.delete" => groupConfig.DiscordNotifyGalleryDelete,

                    // Post Events
                    "group.post.create" => groupConfig.DiscordNotifyPostCreate,
                    "group.post.delete" => groupConfig.DiscordNotifyPostDelete,

                    _ => false
                };
            }
        }

        // Global fallback
        var settings = _settingsService.Settings;

        return normalized switch
        {
            // Member Events
            "group.user.join" => settings.DiscordNotifyUserJoins,
            "group.user.leave" => settings.DiscordNotifyUserLeaves,
            "group.user.kick" => settings.DiscordNotifyUserKicked,
            "group.user.ban" => settings.DiscordNotifyUserBanned,
            "group.user.unban" => settings.DiscordNotifyUserUnbanned,
            "group.user.role.add" => settings.DiscordNotifyUserRoleAdd,
            "group.user.role.remove" => settings.DiscordNotifyUserRoleRemove,

            // Join Request Events
            "group.request.create" or "group.joinrequest" => settings.DiscordNotifyJoinRequests,
            "group.request.accept" => settings.DiscordNotifyJoinRequests,
            "group.request.reject" => settings.DiscordNotifyJoinRequests,
            "group.request.block" => settings.DiscordNotifyJoinRequests,
            "group.request.unblock" => settings.DiscordNotifyJoinRequests,

            // Role Events
            "group.role.create" => settings.DiscordNotifyRoleCreate,
            "group.role.update" => settings.DiscordNotifyRoleUpdate,
            "group.role.delete" => settings.DiscordNotifyRoleDelete,

            // Instance Events
            "group.instance.create" => settings.DiscordNotifyInstanceCreate,
            "group.instance.delete" => settings.DiscordNotifyInstanceDelete,
            "group.instance.open" => settings.DiscordNotifyInstanceOpened,
            "group.instance.close" => settings.DiscordNotifyInstanceClosed,
            "group.instance.warn" => settings.DiscordNotifyInstanceWarn,

            // Group Events
            "group.update" => settings.DiscordNotifyGroupUpdate,

            // Invite Events
            "group.invite.create" or "group.user.invite" => settings.DiscordNotifyInviteCreate,
            "group.invite.accept" => settings.DiscordNotifyInviteAccept,
            "group.invite.reject" => settings.DiscordNotifyInviteReject,

            // Announcement Events
            "group.announcement.create" => settings.DiscordNotifyAnnouncementCreate,
            "group.announcement.delete" => settings.DiscordNotifyAnnouncementDelete,

            // Gallery Events
            "group.gallery.create" => settings.DiscordNotifyGalleryCreate,
            "group.gallery.delete" => settings.DiscordNotifyGalleryDelete,

            // Post Events
            "group.post.create" => settings.DiscordNotifyPostCreate,
            "group.post.delete" => settings.DiscordNotifyPostDelete,

            _ => false
        };
    }

    public async Task<bool> SendAuditEventAsync(string eventType, string actorName, string? targetName, string? description, string? groupId = null)
    {
        // ✅ Normalize for formatting and routing
        var type = NormalizeEventType(eventType);

        var webhookUrl = GetWebhookUrlForEventType(type, groupId);
        webhookUrl = NormalizeWebhookUrl(webhookUrl);

        bool configured = !string.IsNullOrWhiteSpace(webhookUrl);

        Console.WriteLine($"[DISCORD] SendAuditEventAsync called - EventType: {type} (raw: {eventType}), IsConfigured: {configured}");

        if (!configured)
        {
            Console.WriteLine("[DISCORD] Webhook not configured for this event type, skipping notification");
            return false;
        }

        bool shouldSend = ShouldSendAuditEvent(type, groupId);
        if (!shouldSend)
        {
            Console.WriteLine($"[DISCORD] Event type '{type}' is disabled in settings, skipping");
            return false;
        }

        var (title, color, emoji) = type switch
        {
            "group.user.join" => ("Member Joined", 0x4CAF50, "👋"),
            "group.user.leave" => ("Member Left", 0x9E9E9E, "🚪"),
            "group.user.kick" => ("Member Kicked", 0xFF9800, "👢"),
            "group.user.ban" => ("Member Banned", 0xF44336, "🔨"),
            "group.user.unban" => ("Member Unbanned", 0x4CAF50, "✅"),
            "group.user.role.add" => ("Role Added", 0x9C27B0, "➕"),
            "group.user.role.remove" => ("Role Removed", 0xFF5722, "➖"),

            "group.request.create" or "group.joinrequest" => ("Join Request", 0x7C4DFF, "📥"),
            "group.request.accept" => ("Request Accepted", 0x4CAF50, "✅"),
            "group.request.reject" => ("Request Rejected", 0xF44336, "❌"),
            "group.request.block" => ("Request Blocked", 0xD32F2F, "🚫"),
            "group.request.unblock" => ("Request Unblocked", 0x8BC34A, "🔓"),

            "group.role.create" => ("Role Created", 0x00BCD4, "🎭"),
            "group.role.update" => ("Role Updated", 0x9C27B0, "🏷️"),
            "group.role.delete" => ("Role Deleted", 0xF44336, "🗑️"),

            "group.instance.create" => ("Instance Created", 0x03A9F4, "🌍"),
            "group.instance.delete" => ("Instance Deleted", 0x9E9E9E, "🚫"),
            "group.instance.open" => ("Instance Opened", 0x2196F3, "🌐"),
            "group.instance.close" => ("Instance Closed", 0x9E9E9E, "🔒"),
            "group.instance.warn" => ("Instance Warning Issued", 0xFFC107, "⚠️"),

            "group.update" => ("Group Updated", 0xFFC107, "⚙️"),

            "group.invite.create" or "group.user.invite" => ("Invite Sent", 0x8BC34A, "💌"),
            "group.invite.accept" => ("Invite Accepted", 0x4CAF50, "✔️"),
            "group.invite.reject" => ("Invite Rejected", 0xFF5722, "❌"),

            "group.announcement.create" => ("Announcement Posted", 0xFF9800, "📢"),
            "group.announcement.delete" => ("Announcement Deleted", 0x757575, "🗑️"),

            "group.gallery.create" => ("Gallery Item Added", 0xE91E63, "🖼️"),
            "group.gallery.delete" => ("Gallery Item Removed", 0x9E9E9E, "🗑️"),

            "group.post.create" => ("Post Created", 0x2196F3, "📝"),
            "group.post.delete" => ("Post Deleted", 0x757575, "🗑️"),

            _ => ($"Event: {type}", 0x757575, "📋")
        };

        var desc = new StringBuilder();
        desc.AppendLine($"**Actor:** {actorName}");
        if (!string.IsNullOrEmpty(targetName))
            desc.AppendLine($"**Target:** {targetName}");
        if (!string.IsNullOrEmpty(description))
            desc.AppendLine($"**Details:** {description}");

        Console.WriteLine($"[DISCORD] Sending message: {emoji} {title} (Color: {color})");
        var success = await SendMessageAsync($"{emoji} {title}", desc.ToString(), color, null, groupId);
        Console.WriteLine($"[DISCORD] Message send result: {success}");
        return success;
    }

    public async Task<bool> SendModerationActionAsync(
        string actionType,
        string targetUserId,
        string targetName,
        string actorName,
        string reason,
        string? description,
        DateTime actionTime,
        DateTime? expiresAt = null,
        int? infractionCount = null,
        string? groupId = null)
    {
        if (!IsConfiguredForGroup(groupId))
        {
            return false;
        }

        var (title, color, emoji) = actionType.ToLower() switch
        {
            "warning" => ("User Warned", 0xFFC107, "⚠️"),
            "kick" => ("User Kicked", 0xFF9800, "👢"),
            "ban" => ("User Banned", 0xF44336, "🔨"),
            _ => ("Moderation Action", 0x757575, "📋")
        };

        var desc = new StringBuilder();
        var vrchatUrl = $"https://vrchat.com/home/user/{targetUserId}";
        desc.AppendLine($"**Target:** [{targetName}]({vrchatUrl})");
        desc.AppendLine($"**User ID:** `{targetUserId}`");
        desc.AppendLine($"**Moderator:** {actorName}");
        desc.AppendLine($"**Reason:** {reason}");

        if (!string.IsNullOrEmpty(description))
        {
            desc.AppendLine($"**Details:** {description}");
        }

        var unixTime = ((DateTimeOffset)actionTime).ToUnixTimeSeconds();
        desc.AppendLine($"**Time:** <t:{unixTime}:F>");

        if (expiresAt.HasValue)
        {
            var expiryUnixTime = ((DateTimeOffset)expiresAt.Value).ToUnixTimeSeconds();
            desc.AppendLine($"**Expires:** <t:{expiryUnixTime}:R>");
        }
        else if (actionType.ToLower() == "ban")
        {
            desc.AppendLine("**Duration:** Permanent");
        }

        if (infractionCount.HasValue && infractionCount.Value > 0)
        {
            desc.AppendLine($"**Previous Infractions:** {infractionCount.Value}");
        }

        return await SendMessageAsync($"{emoji} {title}", desc.ToString(), color, null, groupId);
    }

    private string? ExtractEventTypeFromTitle(string title)
    {
        var lower = title.ToLowerInvariant();

        if (lower.Contains("joined")) return "group.user.join";
        if (lower.Contains("left")) return "group.user.leave";
        if (lower.Contains("kicked")) return "group.user.kick";
        if (lower.Contains("banned") && !lower.Contains("unbanned")) return "group.user.ban";
        if (lower.Contains("unbanned")) return "group.user.unban";
        if (lower.Contains("role added")) return "group.user.role.add";
        if (lower.Contains("role removed")) return "group.user.role.remove";

        if (lower.Contains("role created")) return "group.role.create";
        if (lower.Contains("role updated")) return "group.role.update";
        if (lower.Contains("role deleted")) return "group.role.delete";

        if (lower.Contains("instance created")) return "group.instance.create";
        if (lower.Contains("instance deleted")) return "group.instance.delete";
        if (lower.Contains("instance opened")) return "group.instance.open";
        if (lower.Contains("instance closed")) return "group.instance.close";
        if (lower.Contains("instance warning")) return "group.instance.warn";

        if (lower.Contains("group updated")) return "group.update";

        if (lower.Contains("join request")) return "group.request.create";
        if (lower.Contains("invite sent")) return "group.invite.create";
        if (lower.Contains("invite accepted")) return "group.invite.accept";
        if (lower.Contains("invite rejected")) return "group.invite.reject";

        if (lower.Contains("announcement posted")) return "group.announcement.create";
        if (lower.Contains("announcement deleted")) return "group.announcement.delete";

        if (lower.Contains("gallery item added")) return "group.gallery.create";
        if (lower.Contains("gallery item removed")) return "group.gallery.delete";

        if (lower.Contains("post created")) return "group.post.create";
        if (lower.Contains("post deleted")) return "group.post.delete";

        return null;
    }

    // ✅ Normalizes event types so toggles + routing match VRChat variants
    private static string NormalizeEventType(string eventType)
    {
        var t = (eventType ?? string.Empty).Trim().ToLowerInvariant();

        if (t.StartsWith("group.member."))
            t = t.Replace("group.member.", "group.user.");

        // role assign/unassign -> add/remove
        if (t == "group.user.role.assign") t = "group.user.role.add";
        if (t == "group.user.role.unassign") t = "group.user.role.remove";

        // remove = moderator kick
        if (t == "group.user.remove") t = "group.user.kick";

        return t;
    }

    // ✅ Normalizes webhook URL common mistakes (http, https//, discordapp.com)
    private static string? NormalizeWebhookUrl(string? webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return null;

        var u = webhookUrl.Trim();

        if (u.StartsWith("https//", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u.Substring("https//".Length);

        if (u.StartsWith("http://discord.com/", StringComparison.OrdinalIgnoreCase))
            u = "https://discord.com/" + u.Substring("http://discord.com/".Length);

        u = u.Replace("https://discordapp.com/", "https://discord.com/", StringComparison.OrdinalIgnoreCase);

        return u;
    }

    public string? GetWebhookUrlForEventType(string eventType, string? groupId = null)
    {
        if (string.IsNullOrEmpty(groupId))
            return _settingsService.Settings.DiscordWebhookUrl;

        var groupConfig = _settingsService.GetGroupConfig(groupId);
        if (groupConfig == null)
            return null;

        var type = NormalizeEventType(eventType);

        string? categoryWebhook = null;

        // ✅ Ensure role add/remove goes to RoleEvents (do this BEFORE group.user.* catch-all)
        if (type.StartsWith("group.user.role."))
        {
            categoryWebhook = groupConfig.DiscordWebhookRoleEvents;
        }
        else if (type.StartsWith("group.user.") || type.Contains("join") || type.Contains("leave") ||
                 type.Contains("kick") || type.Contains("ban") || type.Contains("unban"))
        {
            categoryWebhook = groupConfig.DiscordWebhookMemberEvents;
        }
        else if (type.Contains("role."))
        {
            categoryWebhook = groupConfig.DiscordWebhookRoleEvents;
        }
        else if (type.Contains("instance."))
        {
            categoryWebhook = groupConfig.DiscordWebhookInstanceEvents;
        }
        else if (type == "group.update")
        {
            categoryWebhook = groupConfig.DiscordWebhookGroupEvents;
        }
        else if (type.Contains("invite") || type.Contains("request") || type.Contains("joinrequest"))
        {
            categoryWebhook = groupConfig.DiscordWebhookInviteEvents;
        }
        else if (type.Contains("announcement"))
        {
            categoryWebhook = groupConfig.DiscordWebhookAnnouncementEvents;
        }
        else if (type.Contains("gallery"))
        {
            categoryWebhook = groupConfig.DiscordWebhookGalleryEvents;
        }
        else if (type.Contains("post"))
        {
            categoryWebhook = groupConfig.DiscordWebhookPostEvents;
        }

        return !string.IsNullOrWhiteSpace(categoryWebhook)
            ? NormalizeWebhookUrl(categoryWebhook)
            : NormalizeWebhookUrl(groupConfig.DiscordWebhookUrl);
    }
}
