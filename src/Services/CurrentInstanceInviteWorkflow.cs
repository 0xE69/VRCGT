using System.Threading;

namespace VRCGroupTools.Services;

/// <summary>One explicit, confirmed batch. Delegates keep UI and transport out of the safety policy.</summary>
public sealed class CurrentInstanceInviteWorkflow(
    Func<CurrentInstanceInfo?> getDestination,
    Func<string, string, string, CancellationToken, Task<bool>> send,
    Func<CurrentInstanceInfo, int, bool> confirm,
    Func<CancellationToken, Task> pace,
    Func<(string AccountId, long Session)?>? getAuthentication = null)
{
    private int _running;
    private (string AccountId, long Session)? _confirmedAuthentication;

    public static CurrentInstanceInviteWorkflow Create(IVRChatApiService api, IInstanceInviterService instances,
        Func<CurrentInstanceInfo, int, bool> confirm)
    {
        CurrentInstanceInfo? Read() => api.IsLoggedIn ? instances.GetCurrentInstance() : null;
        CurrentInstanceInviteWorkflow? workflow = null;
        workflow = new(Read,
            (user, world, instance, token) => api.InviteUserToInstanceGuardedAsync(user, world, instance, () =>
            {
                var current = Read();
                return !token.IsCancellationRequested && workflow!.AuthenticationUnchanged() && current?.WorldId == world && current.InstanceId == instance;
            }),
            confirm, token => Task.Delay(500, token),
            () => api.IsLoggedIn && !string.IsNullOrWhiteSpace(api.CurrentUserId)
                ? (api.CurrentUserId, api.InvitationSession) : null);
        return workflow;
    }

    public async Task<InstanceInviteReport> RunAsync(IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(Array.Empty<InstanceInviteOutcome>(), "An invite batch is already running.");
        try { return await RunBatchAsync(recipients, cancellationToken); }
        finally { Volatile.Write(ref _running, 0); }
    }

    private CurrentInstanceInfo? ReadDestination()
    {
        try { return getDestination(); }
        catch (Exception) { return null; }
    }

    private (string AccountId, long Session)? ReadAuthentication()
    {
        try { return getAuthentication?.Invoke(); }
        catch (Exception) { return null; }
    }
    private bool AuthenticationUnchanged() => getAuthentication == null ||
        _confirmedAuthentication != null && ReadAuthentication() == _confirmedAuthentication;

    private async Task<InstanceInviteReport> RunBatchAsync(IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        var selected = recipients.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0) return new(Array.Empty<InstanceInviteOutcome>(), "Select at least one friend.");
        var outcomes = selected.Select(user => new InstanceInviteOutcome(user, InstanceInviteStatus.NotSent)).ToArray();
        if (cancellationToken.IsCancellationRequested) return new(outcomes, "Cancelled before confirmation.");
        var destination = ReadDestination();
        if (destination == null) return new(outcomes, "No current instance. Start VRChat and join a world; a fresh completed-session log is required.");
        _confirmedAuthentication = ReadAuthentication();
        if (!AuthenticationUnchanged()) return new(outcomes, "Acting account is unavailable.");
        // Pin before displaying confirmation; the provider's model is mutable.
        var worldId = destination.WorldId;
        var instanceId = destination.InstanceId;
        if (!confirm(new CurrentInstanceInfo { WorldId = worldId, InstanceId = instanceId, FullLocation = destination.FullLocation, DetectedAt = destination.DetectedAt }, selected.Length))
            return new(outcomes, "Confirmation declined.");
        for (int i = 0; i < selected.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) return new(outcomes, "Cancelled; in-flight requests cannot be recalled.");
            if (i > 0)
            {
                try { await pace(cancellationToken); }
                catch (OperationCanceledException) { return new(outcomes, "Cancelled; in-flight requests cannot be recalled."); }
            }
            if (cancellationToken.IsCancellationRequested) return new(outcomes, "Cancelled; in-flight requests cannot be recalled.");
            var current = ReadDestination();
            if (!AuthenticationUnchanged() || current?.WorldId != worldId || current.InstanceId != instanceId) return new(outcomes, "Acting account/session or current instance changed or is unavailable.");
            try
            {
                var sent = await send(selected[i], worldId, instanceId, cancellationToken);
                outcomes[i] = new(selected[i], sent ? InstanceInviteStatus.Sent : InstanceInviteStatus.Failed);
            }
            catch (InstanceInviteNotSentException)
            {
                return new(outcomes, "Stopped before sending: destination changed, session unavailable, or cancellation requested.");
            }
            catch (Exception)
            {
                // A lost response does not prove that the server rejected the invite. Never retry automatically.
                outcomes[i] = new(selected[i], InstanceInviteStatus.Unknown);
            }
        }
        return new(outcomes, "Batch complete.");
    }
}

public sealed class InstanceInviteNotSentException : Exception
{
    public InstanceInviteNotSentException() : base("Invite was not sent because the destination is no longer current.") { }
}

public enum InstanceInviteStatus { NotSent, Sent, Failed, Unknown }
public sealed record InstanceInviteOutcome(string UserId, InstanceInviteStatus Status);
public sealed record InstanceInviteReport(IReadOnlyList<InstanceInviteOutcome> Outcomes, string Message);
