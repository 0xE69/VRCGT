using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Server.Endpoints;

public static class BadgeScanEndpoints
{
    public static IEndpointRouteBuilder MapBadgeScanEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/groups/{groupId}/badge-scans")
            .RequireAuthorization();

        // ── POST /api/groups/{groupId}/badge-scans ────────────────────────────
        // Start a new badge scan (creates background job).
        grp.MapPost("", async (
            string                            groupId,
            HttpContext                       ctx,
            IBadgeScanService                 scanSvc,
            IBackgroundJobQueue               queue) =>
        {
            var summary = await scanSvc.StartScanAsync(groupId, queue, ctx.RequestAborted);
            return Results.Accepted($"/api/jobs/{summary.JobId}", summary);
        });

        // ── GET /api/groups/{groupId}/badge-scans/latest ──────────────────────
        grp.MapGet("latest", async (
            string            groupId,
            HttpContext       ctx,
            IBadgeScanService scanSvc) =>
        {
            var summary = await scanSvc.GetLatestAsync(groupId, ctx.RequestAborted);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        });

        // ── GET /api/groups/{groupId}/badge-scans/{scanId} ────────────────────
        grp.MapGet("{scanId:long}", async (
            string            groupId,
            long              scanId,
            HttpContext       ctx,
            IBadgeScanService scanSvc) =>
        {
            var detail = await scanSvc.GetScanAsync(scanId, ctx.RequestAborted);
            if (detail is null || detail.GroupId != groupId) return Results.NotFound();
            return Results.Ok(detail);
        });

        // ── POST .../badge-scans/{scanId}/kick-selected ───────────────────────
        grp.MapPost("{scanId:long}/kick-selected", async (
            string                            groupId,
            long                              scanId,
            HttpContext                       ctx,
            IBadgeScanService                 scanSvc,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            IBackgroundJobQueue               queue,
            KickSelectedRequest               body) =>
        {
            // Validate confirmation token
            if (!ConfirmationGuard.IsValid(
                    body.ConfirmToken, ConfirmationGuard.KickSelectedBadge, scanId.ToString()))
            {
                return ConfirmationResults.ConfirmationRequired(
                    ConfirmationGuard.KickSelectedBadge, scanId.ToString());
            }

            if (body.UserIds is null || body.UserIds.Count == 0)
                return Results.BadRequest(new { error = "No userIds provided" });

            await using var db = await dbFactory.CreateDbContextAsync(ctx.RequestAborted);
            var job = new BackgroundJob
            {
                JobType        = "badge-kick-selected",
                Label          = $"Kick {body.UserIds.Count} selected — scan #{scanId}",
                Status         = BackgroundJobStatus.Pending,
                TotalItems     = body.UserIds.Count,
                CreatedAt      = DateTime.UtcNow,
                ParametersJson = System.Text.Json.JsonSerializer.Serialize(
                    new { scanId, userIds = body.UserIds }),
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await queue.EnqueueAsync(job.Id, ctx.RequestAborted);

            return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
        });

        // ── POST .../badge-scans/{scanId}/kick-unverified ─────────────────────
        grp.MapPost("{scanId:long}/kick-unverified", async (
            string                            groupId,
            long                              scanId,
            HttpContext                       ctx,
            IBadgeScanService                 scanSvc,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            IBackgroundJobQueue               queue,
            KickUnverifiedRequest             body) =>
        {
            if (!ConfirmationGuard.IsValid(
                    body.ConfirmToken, ConfirmationGuard.KickAllUnverified, scanId.ToString()))
            {
                return ConfirmationResults.ConfirmationRequired(
                    ConfirmationGuard.KickAllUnverified, scanId.ToString());
            }

            // Count unverified to label the job
            var detail = await scanSvc.GetScanAsync(scanId, ctx.RequestAborted);
            if (detail is null || detail.GroupId != groupId) return Results.NotFound();

            int unverifiedCount = detail.Members.Count(m => m.IsAgeVerified == false && !m.WasKicked);
            if (unverifiedCount == 0)
                return Results.BadRequest(new { error = "No unverified unkicked members in this scan" });

            await using var db = await dbFactory.CreateDbContextAsync(ctx.RequestAborted);
            var job = new BackgroundJob
            {
                JobType        = "badge-kick-unverified",
                Label          = $"Kick {unverifiedCount} unverified — scan #{scanId}",
                Status         = BackgroundJobStatus.Pending,
                TotalItems     = unverifiedCount,
                CreatedAt      = DateTime.UtcNow,
                ParametersJson = System.Text.Json.JsonSerializer.Serialize(new { scanId }),
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await queue.EnqueueAsync(job.Id, ctx.RequestAborted);

            return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
        });

        // ── GET .../badge-scans/{scanId}/export ───────────────────────────────
        grp.MapGet("{scanId:long}/export", async (
            string            groupId,
            long              scanId,
            HttpContext       ctx,
            IBadgeScanService scanSvc) =>
        {
            var detail = await scanSvc.GetScanAsync(scanId, ctx.RequestAborted);
            if (detail is null || detail.GroupId != groupId) return Results.NotFound();

            var filename = $"badge-scan-{groupId}-{detail.CreatedAt:yyyyMMddHHmm}.csv";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            ctx.Response.ContentType = "text/csv; charset=utf-8";

            await using var writer = new System.IO.StreamWriter(ctx.Response.Body, System.Text.Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("UserId,DisplayName,IsAgeVerified,WasKicked,ScannedAt");
            foreach (var m in detail.Members)
            {
                static string Esc(string? v) => v is null ? "" : $"\"{v.Replace("\"", "\"\"")}\"";
                var verified = m.IsAgeVerified switch { true => "true", false => "false", _ => "unknown" };
                await writer.WriteLineAsync(
                    $"{Esc(m.UserId)},{Esc(m.DisplayName)},{verified},{m.WasKicked},{m.ScannedAt:o}");
            }
            await writer.FlushAsync();

            return Results.Empty;
        });

        return app;
    }

    // ── Request body records ──────────────────────────────────────────────────

    public sealed record KickSelectedRequest(string? ConfirmToken, IReadOnlyList<string>? UserIds);
    public sealed record KickUnverifiedRequest(string? ConfirmToken);
}
