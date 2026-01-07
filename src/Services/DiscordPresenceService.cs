using DiscordRPC;
using DiscordRPC.Message;
using VRCGroupTools.Services;

namespace VRCGroupTools.Services;

public interface IDiscordPresenceService : IDisposable
{
    void UpdateGroupPresence(string groupName, string groupId, int memberCount, int onlineCount);
    void ClearPresence();
}

public class DiscordPresenceService : IDiscordPresenceService
{
    private readonly ISettingsService _settingsService;
    private readonly object _sync = new();
    private DiscordRpcClient? _client;
    private bool _started;

    private const string RepoUrl = "https://github.com/0xE69/VRCGT";

    public DiscordPresenceService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void UpdateGroupPresence(string groupName, string groupId, int memberCount, int onlineCount)
    {
        try
        {
            if (!EnsureClient())
            {
                return;
            }

            var presence = new RichPresence
            {
                Details = string.IsNullOrWhiteSpace(groupName) ? "Browsing groups" : $"Group: {groupName}",
                State = $"Members {memberCount} | Online {onlineCount}",
                Timestamps = Timestamps.Now,
                Buttons = BuildButtons(groupId)
            };

            _client!.SetPresence(presence);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("DISCORD-RPC", $"Presence update failed: {ex.Message}");
        }
    }

    public void ClearPresence()
    {
        try
        {
            if (_client != null && _client.IsInitialized)
            {
                _client.ClearPresence();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn("DISCORD-RPC", $"Clear presence failed: {ex.Message}");
        }
    }

    private bool EnsureClient()
    {
        lock (_sync)
        {
            var settings = _settingsService.Settings;
            if (!settings.DiscordPresenceEnabled)
            {
                return false;
            }

            var appId = settings.DiscordPresenceAppId?.Trim();
            if (string.IsNullOrWhiteSpace(appId))
            {
                LoggingService.Warn("DISCORD-RPC", "Discord presence enabled but no Application ID is set in settings.");
                return false;
            }

            if (_client != null && _started)
            {
                return true;
            }

            _client?.Dispose();
            _client = new DiscordRpcClient(appId)
            {
                SkipIdenticalPresence = true
            };

            _client.OnError += OnError;
            _client.OnConnectionFailed += OnConnectionFailed;
            _client.Initialize();
            _started = _client.IsInitialized;

            if (_started)
            {
                LoggingService.Info("DISCORD-RPC", "Discord Rich Presence connected.");
            }
            else
            {
                LoggingService.Warn("DISCORD-RPC", "Discord Rich Presence failed to initialize.");
            }

            return _started;
        }
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedMessage args)
    {
        LoggingService.Warn("DISCORD-RPC", $"Connection failed: {args.Type} pipe {args.FailedPipe}");
    }

    private void OnError(object? sender, ErrorMessage args)
    {
        LoggingService.Warn("DISCORD-RPC", $"Error: {args.Code} {args.Message}");
    }

    private Button[] BuildButtons(string groupId)
    {
        var settings = _settingsService.Settings;
        var buttons = new List<Button>();

        if (settings.DiscordPresenceShowRepoButton)
        {
            buttons.Add(new Button
            {
                Label = "GitHub: VRCGT",
                Url = RepoUrl
            });
        }

        if (!string.IsNullOrWhiteSpace(groupId))
        {
            buttons.Add(new Button
            {
                Label = "Open Group",
                Url = $"https://vrchat.com/home/group/{groupId}"
            });
        }

        return buttons.ToArray();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try
            {
                _client?.ClearPresence();
                _client?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _client = null;
                _started = false;
            }
        }
    }
}
