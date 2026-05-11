using Microsoft.AspNetCore.SignalR;

namespace VRCGT.Server.Hubs;

/// <summary>
/// SignalR hub for live audit log updates.
/// Clients subscribe per-group; the server pushes new entries as they arrive.
/// </summary>
public sealed class AuditHub : Hub
{
    /// <summary>
    /// Called by clients to join the SignalR group for a specific VRChat group.
    /// New audit entries will be broadcast to the group "{groupId}".
    /// </summary>
    public Task SubscribeToGroup(string groupId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"audit:{groupId}");

    public Task UnsubscribeFromGroup(string groupId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"audit:{groupId}");
}
