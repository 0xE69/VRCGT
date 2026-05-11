using Microsoft.AspNetCore.SignalR;
using VRCGT.Core;
using VRCGT.Server.Hubs;

namespace VRCGT.Server.Services;

/// <summary>
/// Implements IJobProgressSink by forwarding every broadcast to all connected
/// SignalR clients on the JobsHub.  Registered as a singleton in DI.
/// </summary>
public sealed class HubJobProgressSink : IJobProgressSink
{
    private readonly IHubContext<JobsHub> _hub;

    public HubJobProgressSink(IHubContext<JobsHub> hub) => _hub = hub;

    public Task BroadcastAsync(JobProgressDto progress, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("JobProgressUpdate", progress, ct);
}
