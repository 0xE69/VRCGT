namespace VRCGroupTools.Services;

/// <summary>Server-returned identity/location only. Local log text is never evidence.</summary>
public sealed record UserPresence(string UserId, string Location);

/// <summary>One explicitly confirmed session. Each candidate is read once, never on unchanged polls.</summary>
public sealed class AutomaticPresenceCorroborator
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(20);
    private readonly AutoGroupInviteContext _context;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (DateTimeOffset Started, UserPresence? Evidence)> _checked = new(StringComparer.Ordinal);
    private DateTimeOffset _nextRead;
    public AutomaticPresenceCorroborator(AutoGroupInviteContext context, Func<DateTimeOffset> now)
    { _context = context; _now = now; }

    public async Task<bool> ResolveAsync(AutoGroupInviteContext context, string userId, IVRChatApiService api, Func<bool> valid)
    {
        if (context != _context || !valid()) return false;
        if (_checked.ContainsKey(userId)) return Confirms(context, userId);
        var started = _now();
        if (started < _nextRead) return false;
        _nextRead = started + AutoGroupInviteSession.InviteInterval;
        _checked[userId] = (started, null); // reserve; failed/hidden/expired reads are not retried automatically
        UserPresence? evidence = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            evidence = await api.GetUserPresenceAsync(userId, valid, timeout.Token);
        }
        catch { /* Unknown. Do not log response bodies/private locations or retry. */ }
        if (valid()) _checked[userId] = (started, evidence);
        return valid() && Confirms(context, userId);
    }
    public bool Confirms(AutoGroupInviteContext context, string userId)
    {
        if (context != _context || !_checked.TryGetValue(userId, out var result)) return false;
        var age = _now() - result.Started; // includes API pacing/network time, not just time since response
        return age >= TimeSpan.Zero && age <= MaximumAge && result.Evidence?.UserId == userId &&
            string.Equals(result.Evidence.Location, context.WorldId + ":" + context.InstanceId, StringComparison.Ordinal);
    }
    public string Describe(AutoGroupInviteContext context, string userId) => Confirms(context, userId)
        ? "presence API-corroborated (bounded freshness)" : _checked.ContainsKey(userId)
        ? "presence unknown/hidden/mismatched/expired — automatic invite skipped; review manually"
        : "presence pending — logs alone cannot authorize an automatic invite";
}
