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
            "group.user.join" => settings.DiscordNotifyUserJoins,
            "group.user.leave" => settings.DiscordNotifyUserLeaves,
            "group.user.kick" => settings.DiscordNotifyUserKicked,
            "group.user.ban" => settings.DiscordNotifyUserBanned,
            "group.user.unban" => settings.DiscordNotifyUserUnbanned,
            "group.instance.open" => settings.DiscordNotifyInstanceOpened,
            "group.instance.close" => settings.DiscordNotifyInstanceClosed,
            "group.user.join_request" => settings.DiscordNotifyJoinRequests,
            "group.user.role.update" or "group.role.update" => settings.DiscordNotifyRoleUpdate,
            _ => false
        };

        if (!shouldSend)
            return;

        var (title, color, emoji) = eventType switch
        {
            "group.user.join" => ("Member Joined", 0x4CAF50, "👋"),
            "group.user.leave" => ("Member Left", 0x9E9E9E, "🚪"),
            "group.user.kick" => ("Member Kicked", 0xFF9800, "👢"),
            "group.user.ban" => ("Member Banned", 0xF44336, "🔨"),
            "group.user.unban" => ("Member Unbanned", 0x4CAF50, "✅"),
            "group.instance.open" => ("Instance Opened", 0x2196F3, "🌐"),
            "group.instance.close" => ("Instance Closed", 0x9E9E9E, "🔒"),
            "group.user.join_request" => ("Join Request", 0x7C4DFF, "📥"),
            "group.user.role.update" or "group.role.update" => ("Role Updated", 0x9C27B0, "🏷️"),
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
