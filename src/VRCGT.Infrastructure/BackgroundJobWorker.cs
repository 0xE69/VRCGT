using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Long-running hosted service that dequeues job IDs and executes them one at a time.
///
/// Design notes:
///   - Jobs execute sequentially (single worker loop).  Multiple workers can be
///     added in a later phase by partitioning the queue by group ID.
///   - On startup, any jobs left in Running/Cancelling state from a previous
///     server run are marked Failed (interrupted).
///   - Pending jobs survive a restart because they only enter Running after being
///     dequeued, but items in the channel (held in memory) are lost.  For durable
///     re-queue, a startup scan re-enqueues Pending jobs automatically.
/// </summary>
public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue               _queue;
    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly JobHandlerRegistry                _handlers;
    private readonly JobCancellationSource             _cancels;
    private readonly IJobProgressSink                  _sink;
    private readonly ILogger<BackgroundJobWorker>      _log;
    private readonly IServiceProvider                  _sp;

    public BackgroundJobWorker(
        IBackgroundJobQueue               queue,
        IDbContextFactory<VrcgtDbContext> dbFactory,
        JobHandlerRegistry                handlers,
        JobCancellationSource             cancels,
        IJobProgressSink                  sink,
        ILogger<BackgroundJobWorker>      log,
        IServiceProvider                  sp)
    {
        _queue     = queue;
        _dbFactory = dbFactory;
        _handlers  = handlers;
        _cancels   = cancels;
        _sink      = sink;
        _log       = log;
        _sp        = sp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        await RequeuePendingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            long jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunAsync(jobId, stoppingToken);
        }
    }

    // ── Recovery ─────────────────────────────────────────────────────────────

    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var interrupted = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.Running ||
                        j.Status == BackgroundJobStatus.Cancelling)
            .ToListAsync(ct);

        if (interrupted.Count == 0) return;

        foreach (var j in interrupted)
        {
            j.Status      = BackgroundJobStatus.Failed;
            j.ErrorMessage = "Server restarted while job was running.";
            j.CompletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        _log.LogWarning("Marked {Count} interrupted jobs as Failed on startup", interrupted.Count);
    }

    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var pending = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .ToListAsync(ct);

        foreach (var id in pending)
            await _queue.EnqueueAsync(id, ct);

        if (pending.Count > 0)
            _log.LogInformation("Re-enqueued {Count} pending jobs from previous session", pending.Count);
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    private async Task RunAsync(long jobId, CancellationToken workerToken)
    {
        await using var db  = await _dbFactory.CreateDbContextAsync(workerToken);
        var job = await db.BackgroundJobs.FindAsync([jobId], workerToken);
        if (job is null)
        {
            _log.LogWarning("Job {Id} not found in DB — skipping", jobId);
            return;
        }

        var handler = _handlers.GetHandler(job.JobType);
        if (handler is null)
        {
            job.Status      = BackgroundJobStatus.Failed;
            job.ErrorMessage = $"No handler registered for job type '{job.JobType}'.";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(workerToken);
            await BroadcastAsync(job, workerToken);
            return;
        }

        job.Status    = BackgroundJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(workerToken);
        await BroadcastAsync(job, workerToken);

        // Per-job cancellation token — linked to the worker shutdown token
        using var jobCts = _cancels.Register(jobId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, jobCts.Token);

        try
        {
            var ctx = new JobExecutionContext
            {
                JobId          = job.Id,
                JobType        = job.JobType,
                TotalItems     = job.TotalItems,
                ParametersJson = job.ParametersJson,
                Services       = _sp,
                Token          = linked.Token,
                ReportProgressAsync = async (processed, total, currentItem, failures) =>
                {
                    // Persist to DB
                    await using var pdb = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
                    var pj = await pdb.BackgroundJobs.FindAsync([jobId]);
                    if (pj is null) return;
                    pj.ProcessedItems   = processed;
                    pj.TotalItems       = total;
                    pj.FailedItems      = failures;
                    pj.CurrentItemLabel = currentItem;
                    await pdb.SaveChangesAsync(CancellationToken.None);

                    // Broadcast
                    await _sink.BroadcastAsync(new JobProgressDto(
                        JobId:        jobId,
                        JobType:      pj.JobType,
                        Label:        pj.Label,
                        Status:       pj.Status.ToString(),
                        Processed:    processed,
                        Total:        total,
                        SuccessCount: processed - failures,
                        FailureCount: failures,
                        CurrentItem:  currentItem,
                        StartedAt:    pj.StartedAt,
                        CompletedAt:  null,
                        ErrorMessage: null
                    ), CancellationToken.None);
                }
            };

            await handler(ctx);

            job.Status      = BackgroundJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            job.Status      = BackgroundJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            _log.LogInformation("Job {Id} was cancelled", jobId);
        }
        catch (OperationCanceledException)
        {
            // Worker is shutting down — leave job as Pending so it restarts next time
            job.Status = BackgroundJobStatus.Pending;
            _log.LogInformation("Job {Id} interrupted by server shutdown — will re-run on next start", jobId);
        }
        catch (Exception ex)
        {
            job.Status       = BackgroundJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt  = DateTime.UtcNow;
            _log.LogError(ex, "Job {Id} ({Type}) failed", jobId, job.JobType);
        }
        finally
        {
            _cancels.Unregister(jobId);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await BroadcastAsync(job, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task BroadcastAsync(BackgroundJob job, CancellationToken ct) =>
        _sink.BroadcastAsync(new JobProgressDto(
            JobId:        job.Id,
            JobType:      job.JobType,
            Label:        job.Label,
            Status:       job.Status.ToString(),
            Processed:    job.ProcessedItems,
            Total:        job.TotalItems,
            SuccessCount: job.ProcessedItems - job.FailedItems,
            FailureCount: job.FailedItems,
            CurrentItem:  job.CurrentItemLabel,
            StartedAt:    job.StartedAt,
            CompletedAt:  job.CompletedAt,
            ErrorMessage: job.ErrorMessage
        ), ct);
}
