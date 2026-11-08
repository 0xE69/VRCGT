using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public partial class VRChatApiService
{
    private readonly SemaphoreSlim _presenceDispatchGate = new(1, 1);
    private long? _lastPresenceDispatch;

    /// <summary>Read-only API corroboration. No log/profile claims, missing identity or fallback location.</summary>
    public async Task<UserPresence?> GetUserPresenceAsync(string userId, Func<bool> canRead, CancellationToken cancellationToken)
    {
        if (!ApprovalPublisher.Id(userId, "usr") || !IsLoggedIn || !canRead()) return null;
        var account = CurrentUserId;
        var generation = InvitationSession;
        bool Valid() => IsLoggedIn && CurrentUserId == account && InvitationSession == generation && canRead();
        try
        {
            Task<HttpResponseMessage> pending;
            await _presenceDispatchGate.WaitAsync(cancellationToken);
            try
            {
                if (!Valid()) return null;
                await RateLimitAsync().WaitAsync(cancellationToken);
                // Admission time is not dispatch time: the shared limiter can delay a previous read.
                // Serialize only through HTTP initiation, never through the response/network await.
                while (_lastPresenceDispatch is long previous)
                {
                    var remaining = AutoGroupInviteSession.InviteInterval - Stopwatch.GetElapsedTime(previous);
                    if (remaining <= TimeSpan.Zero) break;
                    if (!Valid()) return null;
                    await Task.Delay(remaining, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!Valid()) return null;
                try { pending = _httpClient.GetAsync($"users/{userId}?apiKey={ApiKey}", cancellationToken); }
                finally { _lastPresenceDispatch = Stopwatch.GetTimestamp(); }
            }
            finally { _presenceDispatchGate.Release(); }
            using var response = await pending;
            if (!response.IsSuccessStatusCode || !Valid()) return null;
            var data = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            // User.location is optional/privacy-limited; User.state=active means web-only, not in VRChat.
            // Do not infer identity from request URL or accept status/bio/last-location/travel destination instead.
            if (data["id"]?.Type != JTokenType.String || data["id"]?.Value<string>() != userId ||
                data["state"]?.Type != JTokenType.String || data["state"]?.Value<string>() != "online" ||
                data["location"]?.Type != JTokenType.String) return null;
            if (new[] { "travelingToLocation", "travelingToInstance", "travelingToWorld" }
                .Any(field => data[field] != null && data[field]!.Type != JTokenType.Null &&
                    (data[field]!.Type != JTokenType.String || !string.IsNullOrEmpty(data[field]!.Value<string>())))) return null;
            var location = data["location"]!.Value<string>();
            return Valid() && !string.IsNullOrWhiteSpace(location) ? new(userId, location) : null;
        }
        catch { return null; } // Unknown, no automatic retry; never log private locations/auth/response bodies.
    }
}
