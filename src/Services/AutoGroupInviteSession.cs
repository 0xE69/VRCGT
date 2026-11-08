namespace VRCGroupTools.Services;

/// <summary>One explicitly confirmed current-instance → group session. Never restored from settings.</summary>
public sealed record AutoGroupInviteContext(string GroupId, string AccountId, long LoginSession, string ClientSession, string WorldId, string InstanceId);

public sealed class AutoGroupInviteSession : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan InviteInterval = TimeSpan.FromSeconds(13);
    private readonly AutoGroupInviteContext _confirmed;
    private readonly Func<AutoGroupInviteContext?> _context;
    private readonly Func<Task<List<InstanceUser>>> _discover;
    private readonly Func<InstanceUser, Task<bool>> _eligible;
    private readonly Func<InstanceUser, bool> _stillEligible;
    private readonly Func<string, Func<bool>, CancellationToken, Task<GroupInviteAttempt>> _send;
    private readonly Action<string> _status;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _cancel = new();
    private int _polling;
    private Task _sending = Task.CompletedTask;
    private DateTimeOffset _nextSend;
    public bool IsRunning => !_cancel.IsCancellationRequested;
    public Task PendingSend => _sending;

    public AutoGroupInviteSession(AutoGroupInviteContext confirmed, Func<AutoGroupInviteContext?> context,
        Func<Task<List<InstanceUser>>> discover, Func<InstanceUser, Task<bool>> eligible,
        Func<InstanceUser, bool> stillEligible,
        Func<string, Func<bool>, CancellationToken, Task<GroupInviteAttempt>> send, Action<string> status,
        Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _confirmed = confirmed; _context = context; _discover = discover; _eligible = eligible;
        _stillEligible = stillEligible; _send = send; _status = status;
        _now = now ?? (() => DateTimeOffset.UtcNow); _delay = delay ?? Task.Delay;
    }
    private bool Valid()
    {
        if (!IsRunning) return false;
        try { if (_context() == _confirmed) return true; } catch { }
        Stop("Auto Invite stopped: account, selected group, client session or instance changed.");
        return false;
    }
    public async Task RunAsync()
    {
        var due = _now() + PollInterval;
        try
        {
            while (IsRunning)
            {
                var wait = due - _now();
                if (wait > TimeSpan.Zero) await _delay(wait, _cancel.Token);
                if (!IsRunning) break;
                await TickAsync();
                due += PollInterval;
                // Missed ticks are dropped, never replayed as a burst.
                if (due <= _now()) due = _now() + PollInterval;
            }
        }
        catch (OperationCanceledException) when (!IsRunning) { }
        catch { Stop("Auto Invite stopped: local polling failed. Refresh and explicitly enable again."); }
    }
    public async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            if (!Valid()) return;
            var users = await _discover();
            if (!Valid() || !_sending.IsCompleted || _now() < _nextSend) return;
            _sending = PrepareOneAsync(users);
        }
        catch { Stop("Auto Invite stopped: eligibility/history unavailable. No automatic retry."); }
        finally { Volatile.Write(ref _polling, 0); }
    }
    private async Task PrepareOneAsync(List<InstanceUser> users)
    {
        try
        {
            foreach (var user in users)
            {
                if (!Valid()) return;
                if (!await _eligible(user)) continue;
                if (!Valid()) return;
                if (!_stillEligible(user)) continue;
                _nextSend = _now() + InviteInterval;
                await SendOneAsync(user);
                break; // Discovery continues while details or paced dispatch await.
            }
        }
        catch { Stop("Auto Invite stopped: eligibility lookup failed. No automatic retry."); }
    }
    private async Task SendOneAsync(InstanceUser user)
    {
        try
        {
            var result = await _send(user.UserId, () => Valid() && _stillEligible(user) && Valid(), _cancel.Token);
            _nextSend = _now() + InviteInterval; // a slow/backed-off request must not allow a completion-time burst
            _status($"{DateTime.Now:HH:mm:ss} {user.DisplayName}: {result.Message}");
            if (result.State is GroupInviteState.Failed or GroupInviteState.Unknown or GroupInviteState.InFlight)
                Stop("Auto Invite stopped after rejected/uncertain request. Check local history; failed invites require deliberate manual retry.");
        }
        catch { Stop("Auto Invite stopped: request/history error. Check local history; no automatic retry."); }
    }
    public void Stop(string reason = "Auto Invite off. Pending requests may already have reached VRChat; see local history.")
    {
        if (!IsRunning) return;
        _cancel.Cancel(); _status(reason);
    }
    public void Dispose() => Stop();
}
