// VRCGT.Core — Background job DTOs and interfaces.
// No ASP.NET Core or EF Core references.

namespace VRCGT.Core;

/// <summary>
/// Summary snapshot of a background job — returned by REST endpoints.
/// </summary>
public sealed record JobSummaryDto(
    long      Id,
    string    JobType,
    string?   Label,
    string    Status,
    int       ProcessedItems,
    int       TotalItems,
    int       FailedItems,
    string?   CurrentItemLabel,
    DateTime  CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string?   ErrorMessage
);

/// <summary>
/// Live progress event broadcast via SignalR.
/// </summary>
public sealed record JobProgressDto(
    long      JobId,
    string    JobType,
    string?   Label,
    string    Status,
    int       Processed,
    int       Total,
    int       SuccessCount,
    int       FailureCount,
    string?   CurrentItem,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string?   ErrorMessage
);

/// <summary>
/// In-process, single-machine job queue backed by a Channel.
/// Implemented by VRCGT.Infrastructure.BackgroundJobQueue.
/// </summary>
public interface IBackgroundJobQueue
{
    /// <summary>Push a persisted job ID onto the queue for execution.</summary>
    ValueTask EnqueueAsync(long jobId, CancellationToken ct = default);

    /// <summary>Wait for and pop the next job ID. Blocks until one is available.</summary>
    ValueTask<long> DequeueAsync(CancellationToken ct = default);
}

/// <summary>
/// Receives job progress broadcasts. Implemented by the SignalR sink in VRCGT.Server.
/// Decouples the worker (Infrastructure) from the hub (Server).
/// </summary>
public interface IJobProgressSink
{
    Task BroadcastAsync(JobProgressDto progress, CancellationToken ct = default);
}
