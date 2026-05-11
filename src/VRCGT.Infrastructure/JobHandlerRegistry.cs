using System.Collections.Concurrent;

namespace VRCGT.Infrastructure;

// ── Execution context ────────────────────────────────────────────────────────

/// <summary>
/// Passed to every job handler. Provides metadata about the job and a
/// callback to report incremental progress (persists to DB + broadcasts
/// over SignalR).
/// </summary>
public sealed class JobExecutionContext
{
    public required long    JobId          { get; init; }
    public required string  JobType        { get; init; }
    public required int     TotalItems     { get; init; }
    public required string? ParametersJson { get; init; }
    public required IServiceProvider     Services { get; init; }
    public required CancellationToken    Token    { get; init; }

    /// <summary>
    /// Report incremental progress.
    /// Parameters: (processed, total, currentItemLabel, failureCount)
    /// </summary>
    public required Func<int, int, string?, int, Task> ReportProgressAsync { get; init; }
}

// ── Handler registry ─────────────────────────────────────────────────────────

/// <summary>
/// Singleton registry of job type → async handler functions.
/// Job types are registered at app startup via Register().
/// </summary>
public sealed class JobHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Func<JobExecutionContext, Task>> _handlers = new();

    public void Register(string jobType, Func<JobExecutionContext, Task> handler)
        => _handlers[jobType] = handler;

    public Func<JobExecutionContext, Task>? GetHandler(string jobType)
        => _handlers.TryGetValue(jobType, out var h) ? h : null;
}

// ── Per-job cancellation source ──────────────────────────────────────────────

/// <summary>
/// Singleton that maps running job IDs to their CancellationTokenSources so
/// that the cancel endpoint can abort a specific job.
/// </summary>
public sealed class JobCancellationSource
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _map = new();

    public CancellationTokenSource Register(long jobId)
    {
        var cts = new CancellationTokenSource();
        _map[jobId] = cts;
        return cts;
    }

    public bool Cancel(long jobId)
    {
        if (!_map.TryGetValue(jobId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void Unregister(long jobId) => _map.TryRemove(jobId, out _);

    public bool IsRunning(long jobId) => _map.ContainsKey(jobId);
}
