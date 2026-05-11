using Microsoft.AspNetCore.SignalR;
using VRCGT.Core;
using VRCGT.Server.Hubs;

namespace VRCGT.Server.Services;

/// <summary>
/// Broadcasts new audit log entries to connected SignalR clients.
/// </summary>
public interface IAuditSink
{
    Task BroadcastAsync(string groupId, IReadOnlyList<AuditLogEntryDto> entries, CancellationToken ct = default);
}

public sealed class HubAuditSink : IAuditSink
{
    private readonly IHubContext<AuditHub> _hub;

    public HubAuditSink(IHubContext<AuditHub> hub) => _hub = hub;

    public Task BroadcastAsync(string groupId, IReadOnlyList<AuditLogEntryDto> entries, CancellationToken ct = default) =>
        _hub.Clients.Group($"audit:{groupId}")
            .SendAsync("NewAuditLogEntries", entries, ct);
}
