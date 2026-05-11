using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Server.Endpoints;

internal static class JobEndpoints
{
    internal static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        // ── List recent jobs ─────────────────────────────────────────────────
        app.MapGet("/api/jobs", async (
            IDbContextFactory<VrcgtDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var jobs = await db.BackgroundJobs
                .OrderByDescending(j => j.CreatedAt)
                .Take(50)
                .ToListAsync(ct);

            return Results.Ok(jobs.Select(ToSummary));
        }).RequireAuthorization();

        // ── Single job detail ────────────────────────────────────────────────
        app.MapGet("/api/jobs/{jobId:long}", async (
            long jobId,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var job = await db.BackgroundJobs
                .Include(j => j.Items.OrderBy(i => i.Id))
                .FirstOrDefaultAsync(j => j.Id == jobId, ct);

            return job is null ? Results.NotFound() : Results.Ok(ToSummary(job));
        }).RequireAuthorization();

        // ── Cancel a running job ─────────────────────────────────────────────
        app.MapPost("/api/jobs/{jobId:long}/cancel", async (
            long jobId,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            JobCancellationSource cancels,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var job = await db.BackgroundJobs.FindAsync([jobId], ct);
            if (job is null) return Results.NotFound();

            if (job.Status != BackgroundJobStatus.Running)
                return Results.BadRequest(new { error = $"Job is not running (status: {job.Status})." });

            cancels.Cancel(jobId);
            job.Status = BackgroundJobStatus.Cancelling;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToSummary(job));
        }).RequireAuthorization();

        // ── Create a test-progress job ───────────────────────────────────────
        app.MapPost("/api/jobs/test", async (
            HttpRequest req,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            IBackgroundJobQueue queue,
            CancellationToken ct) =>
        {
            var stepsStr = req.Query["steps"].FirstOrDefault() ?? "20";
            int steps    = int.TryParse(stepsStr, out var n) && n is > 0 and <= 1000 ? n : 20;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var job = new BackgroundJob
            {
                JobType    = "test-progress",
                Label      = $"Test job ({steps} steps)",
                TotalItems = steps,
                Status     = BackgroundJobStatus.Pending,
                CreatedAt  = DateTime.UtcNow,
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync(ct);

            await queue.EnqueueAsync(job.Id, ct);

            return Results.Accepted($"/api/jobs/{job.Id}", ToSummary(job));
        }).RequireAuthorization();

        return app;
    }

    private static JobSummaryDto ToSummary(BackgroundJob j) => new(
        j.Id, j.JobType, j.Label, j.Status.ToString(),
        j.ProcessedItems, j.TotalItems, j.FailedItems,
        j.CurrentItemLabel, j.CreatedAt, j.StartedAt, j.CompletedAt, j.ErrorMessage);
}
