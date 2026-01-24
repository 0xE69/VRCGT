using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace VRCGroupTools.Services;

public interface IDiscordWebhookService
{
    Task<bool> SendMessageAsync(string title, string description, int color, string? thumbnailUrl = null);
    Task<WebhookTestResult> TestWebhookAsync(string webhookUrl);
    Task<bool> SendModerationActionAsync(string actionType, string targetUserId, string targetName, string actorName, string reason, string? description, DateTime actionTime, DateTime? expiresAt = null, int? infractionCount = null);
    Task<bool> SendWebhookAsync(string webhookUrl, object payload);
    bool IsConfigured { get; }
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

    public DiscordWebhookService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient();
    }

    public async Task<bool> SendMessageAsync(string title, string description, int color, string? thumbnailUrl = null)
    {
        Console.WriteLine($"[DISCORD] SendMessageAsync called - Title: {title}");
        
        if (!IsConfigured)
        {
            Console.WriteLine("[DISCORD] Not configured, webhook URL is missing");
            return false;
        }

        try
        {
            var embed = new
            {
                title = title,
                description = description,
                color = color,
                timestamp = DateTime.UtcNow.ToString("o"),
                thumbnail = thumbnailUrl != null ? new { url = thumbnailUrl } : null,
                footer = new
                {
                    text = "VRC Group Tools"
                }
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_settingsService.Settings.DiscordWebhookUrl, content);
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
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return new WebhookTestResult { Success = false, ErrorMessage = "Webhook URL is empty" };

        try
        {
            var embed = new
            {
                title = "✅ Webhook Connected!",
                description = "VRC Group Tools is now connected to this channel.\n\nYou will receive notifications based on your settings.",
                color = 0x4CAF50, // Green
                timestamp = DateTime.UtcNow.ToString("o"),
                footer = new
                {
                    text = "VRC Group Tools"
                }
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

    public bool ShouldSendAuditEvent(string eventType)
    {
        var settings = _settingsService.Settings;
        return eventType switch
        {
            // Member Events - support both group.user.* and group.member.* formats
            "group.member.join" or "group.user.join" => settings.DiscordNotifyUserJoins,
            "group.member.leave" or "group.user.leave" => settings.DiscordNotifyUserLeaves,
            "group.member.kick" or "group.user.kick" => settings.DiscordNotifyUserKicked,
            "group.member.ban" or "group.user.ban" => settings.DiscordNotifyUserBanned,
            "group.member.unban" or "group.user.unban" => settings.DiscordNotifyUserUnbanned,
            "group.member.role.add" or "group.user.role.add" => settings.DiscordNotifyUserRoleAdd,
            "group.member.role.remove" or "group.user.role.remove" => settings.DiscordNotifyUserRoleRemove,
            
            // Join Request Events (actual VRChat event types)
            "group.request.create" => settings.DiscordNotifyJoinRequests,
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
            "group.invite.create" => settings.DiscordNotifyInviteCreate,
            "group.user.invite" => settings.DiscordNotifyInviteCreate,
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

    // Helper method to send audit log events
    public async Task<bool> SendAuditEventAsync(string eventType, string actorName, string? targetName, string? description)
    {
        Console.WriteLine($"[DISCORD] SendAuditEventAsync called - EventType: {eventType}, IsConfigured: {IsConfigured}");
        
        if (!IsConfigured)
        {
            Console.WriteLine($"[DISCORD] Webhook not configured, skipping notification");
            return false;
        }
        
        Console.WriteLine("[DISCORD] Webhook URL configured: true");
        
        // Check if this event type is enabled
        bool shouldSend = ShouldSendAuditEvent(eventType);

        Console.WriteLine($"[DISCORD] Event type '{eventType}' shouldSend: {shouldSend}");
        
        if (!shouldSend)
        {
            Console.WriteLine($"[DISCORD] Event type '{eventType}' is disabled in settings, skipping");
            return false;
        }

        var (title, color, emoji) = eventType switch
        {
            // User Events
            "group.user.join" => ("Member Joined", 0x4CAF50, "👋"),
            "group.user.leave" => ("Member Left", 0x9E9E9E, "🚪"),
            "group.user.kick" => ("Member Kicked", 0xFF9800, "👢"),
            "group.user.ban" => ("Member Banned", 0xF44336, "🔨"),
            "group.user.unban" => ("Member Unbanned", 0x4CAF50, "✅"),
            "group.user.role.add" => ("Role Added", 0x9C27B0, "➕"),
            "group.user.role.remove" => ("Role Removed", 0xFF5722, "➖"),
            "group.user.join_request" => ("Join Request", 0x7C4DFF, "📥"),
            "group.joinRequest" => ("Join Request", 0x7C4DFF, "📥"),
            
            // Role Events
            "group.role.create" => ("Role Created", 0x00BCD4, "🎭"),
            "group.role.update" => ("Role Updated", 0x9C27B0, "🏷️"),
            "group.role.delete" => ("Role Deleted", 0xF44336, "🗑️"),
            
            // Instance Events
            "group.instance.create" => ("Instance Created", 0x03A9F4, "🌍"),
            "group.instance.delete" => ("Instance Deleted", 0x9E9E9E, "🚫"),
            "group.instance.open" => ("Instance Opened", 0x2196F3, "🌐"),
            "group.instance.close" => ("Instance Closed", 0x9E9E9E, "🔒"),
            "group.instance.warn" => ("Instance Warning Issued", 0xFFC107, "⚠️"),
            
            // Group Events
            "group.update" => ("Group Updated", 0xFFC107, "⚙️"),
            
            // Invite Events
            "group.invite.create" => ("Invite Sent", 0x8BC34A, "💌"),
            "group.user.invite" => ("Invite Sent", 0x8BC34A, "💌"),
            "group.invite.accept" => ("Invite Accepted", 0x4CAF50, "✔️"),
            "group.invite.reject" => ("Invite Rejected", 0xFF5722, "❌"),
            
            // Announcement Events
            "group.announcement.create" => ("Announcement Posted", 0xFF9800, "📢"),
            "group.announcement.delete" => ("Announcement Deleted", 0x757575, "🗑️"),
            
            // Gallery Events
            "group.gallery.create" => ("Gallery Item Added", 0xE91E63, "🖼️"),
            "group.gallery.delete" => ("Gallery Item Removed", 0x9E9E9E, "🗑️"),
            
            // Post Events
            "group.post.create" => ("Post Created", 0x2196F3, "📝"),
            "group.post.delete" => ("Post Deleted", 0x757575, "🗑️"),
            
            _ => ("Event", 0x757575, "📋")
        };

        var desc = new StringBuilder();
        desc.AppendLine($"**Actor:** {actorName}");
        if (!string.IsNullOrEmpty(targetName))
            desc.AppendLine($"**Target:** {targetName}");
        if (!string.IsNullOrEmpty(description))
            desc.AppendLine($"**Details:** {description}");

        Console.WriteLine($"[DISCORD] Sending message: {emoji} {title}");
        var success = await SendMessageAsync($"{emoji} {title}", desc.ToString(), color);
        Console.WriteLine($"[DISCORD] Message send result: {success}");
        return success;
    }
    
    // Helper method to send moderation action notifications with timestamps
    public async Task<bool> SendModerationActionAsync(
        string actionType,
        string targetUserId,
        string targetName,
        string actorName,
        string reason,
        string? description,
        DateTime actionTime,
        DateTime? expiresAt = null,
        int? infractionCount = null)
    {
        if (!IsConfigured)
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
        
        // Add Discord timestamp for action time
        var unixTime = ((DateTimeOffset)actionTime).ToUnixTimeSeconds();
        desc.AppendLine($"**Time:** <t:{unixTime}:F>");
        
        // Add expiration if provided
        if (expiresAt.HasValue)
        {
            var expiryUnixTime = ((DateTimeOffset)expiresAt.Value).ToUnixTimeSeconds();
            desc.AppendLine($"**Expires:** <t:{expiryUnixTime}:R>");
        }
        else if (actionType.ToLower() == "ban")
        {
            desc.AppendLine($"**Duration:** Permanent");
        }
        
        // Add infraction count if provided
        if (infractionCount.HasValue && infractionCount.Value > 0)
        {
            desc.AppendLine($"**Previous Infractions:** {infractionCount.Value}");
        }
        
        return await SendMessageAsync($"{emoji} {title}", desc.ToString(), color);
    }
}
