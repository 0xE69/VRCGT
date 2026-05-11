using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Server.Endpoints;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/groups/{groupId}/audit-logs")
            .RequireAuthorization();

        // ── GET /api/groups/{groupId}/audit-logs ──────────────────────────────
        grp.MapGet("", async (
            string            groupId,
            HttpContext       ctx,
            IAuditLogService  auditSvc,
            int               page     = 0,
            int               pageSize = 50,
            string?           search   = null,
            string?           type     = null,
            string?           from     = null,
            string?           to       = null) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);

            DateTime? fromDt = DateTime.TryParse(from, out var fd) ? fd.ToUniversalTime() : null;
            DateTime? toDt   = DateTime.TryParse(to,   out var td) ? td.ToUniversalTime() : null;

            var result = await auditSvc.GetPageAsync(
                groupId, page, pageSize, search, type, fromDt, toDt,
                ctx.RequestAborted);

            return Results.Ok(result);
        });

        // ── POST /api/groups/{groupId}/audit-logs/fetch-history ───────────────
        grp.MapPost("fetch-history", async (
            string                            groupId,
            HttpContext                       ctx,
            IDbContextFactory<VrcgtDbContext> dbFactory,
            IBackgroundJobQueue               queue) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ctx.RequestAborted);

            var job = new BackgroundJob
            {
                JobType        = "fetch-audit-history",
                Label          = $"Fetch audit history — {groupId}",
                Status         = BackgroundJobStatus.Pending,
                CreatedAt      = DateTime.UtcNow,
                ParametersJson = System.Text.Json.JsonSerializer.Serialize(new { groupId }),
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await queue.EnqueueAsync(job.Id, ctx.RequestAborted);

            return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
        });

        // ── GET /api/groups/{groupId}/audit-logs/export ───────────────────────
        grp.MapGet("export", async (
            string           groupId,
            HttpContext       ctx,
            IAuditLogService auditSvc,
            string?          search = null,
            string?          type   = null,
            string?          from   = null,
            string?          to     = null) =>
        {
            DateTime? fromDt = DateTime.TryParse(from, out var fd) ? fd.ToUniversalTime() : null;
            DateTime? toDt   = DateTime.TryParse(to,   out var td) ? td.ToUniversalTime() : null;

            var filename = $"audit-{groupId}-{DateTime.UtcNow:yyyyMMddHHmm}.csv";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            ctx.Response.ContentType = "text/csv; charset=utf-8";

            await using var writer = new StreamWriter(ctx.Response.Body, Encoding.UTF8, leaveOpen: true);

            await foreach (var line in auditSvc.ExportCsvAsync(
                groupId, search, type, fromDt, toDt, ctx.RequestAborted))
            {
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync();
        });

        return app;
    }
}
