using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class DiscordSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDiscordWebhookService _discordService;

    [ObservableProperty]
    private string _webhookUrl = "";

    [ObservableProperty]
    private bool _notifyUserJoins;

    [ObservableProperty]
    private bool _notifyUserLeaves;

    [ObservableProperty]
    private bool _notifyUserKicked;

    [ObservableProperty]
    private bool _notifyUserBanned;

    [ObservableProperty]
    private bool _notifyUserUnbanned;

    [ObservableProperty]
    private bool _notifyInstanceOpened;

    [ObservableProperty]
    private bool _notifyInstanceClosed;

    [ObservableProperty]
    private bool _notifyJoinRequests;

    [ObservableProperty]
    private bool _notifyRoleUpdate;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isWebhookValid;

    public DiscordSettingsViewModel()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _discordService = App.Services.GetRequiredService<IDiscordWebhookService>();

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Settings;
        WebhookUrl = settings.DiscordWebhookUrl ?? "";
        NotifyUserJoins = settings.DiscordNotifyUserJoins;
        NotifyUserLeaves = settings.DiscordNotifyUserLeaves;
        NotifyUserKicked = settings.DiscordNotifyUserKicked;
        NotifyUserBanned = settings.DiscordNotifyUserBanned;
        NotifyUserUnbanned = settings.DiscordNotifyUserUnbanned;
        NotifyInstanceOpened = settings.DiscordNotifyInstanceOpened;
        NotifyInstanceClosed = settings.DiscordNotifyInstanceClosed;
        NotifyJoinRequests = settings.DiscordNotifyJoinRequests;
        NotifyRoleUpdate = settings.DiscordNotifyRoleUpdate;
        
        IsWebhookValid = !string.IsNullOrWhiteSpace(WebhookUrl);
    }

    partial void OnWebhookUrlChanged(string value)
    {
        IsWebhookValid = !string.IsNullOrWhiteSpace(value) && 
                         value.StartsWith("https://discord.com/api/webhooks/");
    }

    [RelayCommand]
    private async Task TestWebhookAsync()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            StatusMessage = "⚠️ Please enter a webhook URL";
            return;
        }

        if (!WebhookUrl.StartsWith("https://discord.com/api/webhooks/"))
        {
            StatusMessage = "⚠️ Invalid webhook URL format";
            return;
        }

        IsTesting = true;
        StatusMessage = "Testing webhook...";

        var success = await _discordService.TestWebhookAsync(WebhookUrl);

        if (success)
        {
            StatusMessage = "✅ Webhook connected successfully!";
            IsWebhookValid = true;
        }
        else
        {
            StatusMessage = "❌ Failed to connect. Check the webhook URL.";
            IsWebhookValid = false;
        }

        IsTesting = false;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = _settingsService.Settings;
        settings.DiscordWebhookUrl = WebhookUrl;
        settings.DiscordNotifyUserJoins = NotifyUserJoins;
        settings.DiscordNotifyUserLeaves = NotifyUserLeaves;
        settings.DiscordNotifyUserKicked = NotifyUserKicked;
        settings.DiscordNotifyUserBanned = NotifyUserBanned;
        settings.DiscordNotifyUserUnbanned = NotifyUserUnbanned;
        settings.DiscordNotifyInstanceOpened = NotifyInstanceOpened;
        settings.DiscordNotifyInstanceClosed = NotifyInstanceClosed;
        settings.DiscordNotifyJoinRequests = NotifyJoinRequests;
        settings.DiscordNotifyRoleUpdate = NotifyRoleUpdate;

        _settingsService.Save();
        StatusMessage = "✅ Settings saved!";
    }

    [RelayCommand]
    private void SelectAll()
    {
        NotifyUserJoins = true;
        NotifyUserLeaves = true;
        NotifyUserKicked = true;
        NotifyUserBanned = true;
        NotifyUserUnbanned = true;
        NotifyInstanceOpened = true;
        NotifyInstanceClosed = true;
        NotifyJoinRequests = true;
        NotifyRoleUpdate = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        NotifyUserJoins = false;
        NotifyUserLeaves = false;
        NotifyUserKicked = false;
        NotifyUserBanned = false;
        NotifyUserUnbanned = false;
        NotifyInstanceOpened = false;
        NotifyInstanceClosed = false;
        NotifyJoinRequests = false;
        NotifyRoleUpdate = false;
    }
}
