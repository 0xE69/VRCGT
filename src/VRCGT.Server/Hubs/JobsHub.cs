using Microsoft.AspNetCore.SignalR;

namespace VRCGT.Server.Hubs;

/// <summary>
/// Minimal hub — clients connect and passively receive "JobProgressUpdate"
/// messages from the server.  No inbound calls are needed.
/// </summary>
public sealed class JobsHub : Hub { }
