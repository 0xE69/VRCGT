using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace VRCGroupTools.Services;

public interface IDiscordWebhookService
{
    Task<bool> SendMessageAsync(string title, string description, int color, string? thumbnailUrl = null);
    Task<bool> TestWebhookAsync(string webhookUrl);
    bool IsConfigured { get; }
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
        if (!IsConfigured)
            return false;

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
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCORD] Failed to send message: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TestWebhookAsync(string webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return false;

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
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Helper method to send audit log events
    public async Task SendAuditEventAsync(string eventType, string actorName, string? targetName, string? description)
    {
        var settings = _settingsService.Settings;
        
        // Check if this event type is enabled
        bool shouldSend = eventType switch
        {
            // User Events
            "group.user.join" => settings.DiscordNotifyUserJoins,
            "group.user.leave" => settings.DiscordNotifyUserLeaves,
            "group.user.kick" => settings.DiscordNotifyUserKicked,
            "group.user.ban" => settings.DiscordNotifyUserBanned,
            "group.user.unban" => settings.DiscordNotifyUserUnbanned,
            "group.user.role.add" => settings.DiscordNotifyUserRoleAdd,
            "group.user.role.remove" => settings.DiscordNotifyUserRoleRemove,
            "group.user.join_request" => settings.DiscordNotifyJoinRequests,
            
            // Role Events
            "group.role.create" => settings.DiscordNotifyRoleCreate,
            "group.role.update" => settings.DiscordNotifyRoleUpdate,
            "group.role.delete" => settings.DiscordNotifyRoleDelete,
            
            // Instance Events
            "group.instance.create" => settings.DiscordNotifyInstanceCreate,
            "group.instance.delete" => settings.DiscordNotifyInstanceDelete,
            "group.instance.open" => settings.DiscordNotifyInstanceOpened,
            "group.instance.close" => settings.DiscordNotifyInstanceClosed,
            
            // Group Events
            "group.update" => settings.DiscordNotifyGroupUpdate,
            
            // Invite Events
            "group.invite.create" => settings.DiscordNotifyInviteCreate,
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

        if (!shouldSend)
            return;

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
            
            // Role Events
            "group.role.create" => ("Role Created", 0x00BCD4, "🎭"),
            "group.role.update" => ("Role Updated", 0x9C27B0, "🏷️"),
            "group.role.delete" => ("Role Deleted", 0xF44336, "🗑️"),
            
            // Instance Events
            "group.instance.create" => ("Instance Created", 0x03A9F4, "🌍"),
            "group.instance.delete" => ("Instance Deleted", 0x9E9E9E, "🚫"),
            "group.instance.open" => ("Instance Opened", 0x2196F3, "🌐"),
            "group.instance.close" => ("Instance Closed", 0x9E9E9E, "🔒"),
            
            // Group Events
            "group.update" => ("Group Updated", 0xFFC107, "⚙️"),
            
            // Invite Events
            "group.invite.create" => ("Invite Sent", 0x8BC34A, "💌"),
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

        await SendMessageAsync($"{emoji} {title}", desc.ToString(), color);
    }
}
