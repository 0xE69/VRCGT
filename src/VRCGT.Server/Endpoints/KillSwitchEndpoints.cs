using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VRCGT.Core;
using VRCGT.Infrastructure;

namespace VRCGT.Server.Endpoints;

public static class KillSwitchEndpoints
{
    public static IEndpointRouteBuilder MapKillSwitchEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/groups/{groupId}/kill-switch")
            .RequireAuthorization();

        // ── GET /api/groups/{groupId}/kill-switch/preview ─────────────────────
        // Returns the current member/role state from the local cache without
        // calling the VRChat API.
        grp.MapGet("preview", async (
            string             groupId,
            HttpContext        ctx,
            IKillSwitchService killSvc) =>
        {
            var preview = await killSvc.GetPreviewAsync(groupId, ctx.RequestAborted);
            return Results.Ok(preview);
        });

        // ── POST /api/groups/{groupId}/kill-switch/refresh ────────────────────
        // Synchronously re-fetches members from VRChat and returns a fresh preview.
        // This is the gate the operator must pass through when cache is stale.
        grp.MapPost("refresh", async (
            string             groupId,
            HttpContext        ctx,
            IKillSwitchService killSvc) =>
        {
            var preview = await killSvc.RefreshAndPreviewAsync(groupId, ctx.RequestAborted);
            return Results.Ok(preview);
        });

        // ── POST /api/groups/{groupId}/kill-switch/execute ────────────────────
        // Body: { confirmToken, roleIds[], userIds[] }
        // Creates a snapshot then enqueues the remove-roles background job.
        grp.MapPost("execute", async (
            string              groupId,
            HttpContext         ctx,
            IKillSwitchService  killSvc,
            IBackgroundJobQueue queue,
            ExecuteRequest      body) =>
        {
            // Validate typed confirmation token
            if (!ConfirmationGuard.IsValid(
                    body.ConfirmToken, ConfirmationGuard.RemoveRoles, groupId))
            {
                return ConfirmationResults.ConfirmationRequired(
                    ConfirmationGuard.RemoveRoles, groupId);
            }

            if (body.RoleIds is null || body.RoleIds.Count == 0)
                return Results.BadRequest(new { error = "At least one roleId must be specified" });

            var result = await killSvc.StartExecuteAsync(
                groupId,
                body.RoleIds,
                body.UserIds ?? Array.Empty<string>(),
                queue,
                ctx.RequestAborted);

            return Results.Accepted(
                $"/api/jobs/{result.JobId}",
                new { result.SnapshotId, result.JobId });
        }).RequireAuthorization("perm:UseKillSwitch");

        // ── GET /api/groups/{groupId}/kill-switch/snapshots ───────────────────
        // Returns all snapshots for the group, newest first.
        grp.MapGet("snapshots", async (
            string             groupId,
            HttpContext        ctx,
            IKillSwitchService killSvc) =>
        {
            var snaps = await killSvc.GetSnapshotsAsync(groupId, ctx.RequestAborted);
            return Results.Ok(snaps);
        });

        // ── POST /api/groups/{groupId}/kill-switch/snapshots/{snapshotId}/restore
        // Body: { confirmToken }
        // Enqueues a restore job for the given snapshot.
        grp.MapPost("snapshots/{snapshotId:long}/restore", async (
            string              groupId,
            long                snapshotId,
            HttpContext         ctx,
            IKillSwitchService  killSvc,
            IBackgroundJobQueue queue,
            RestoreRequest      body) =>
        {
            if (!ConfirmationGuard.IsValid(
                    body.ConfirmToken, ConfirmationGuard.RestoreBackup, snapshotId.ToString()))
            {
                return ConfirmationResults.ConfirmationRequired(
                    ConfirmationGuard.RestoreBackup, snapshotId.ToString());
            }

            long jobId;
            try
            {
                jobId = await killSvc.StartRestoreAsync(snapshotId, queue, ctx.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }

            return Results.Accepted($"/api/jobs/{jobId}", new { jobId });
        }).RequireAuthorization("perm:UseKillSwitch");

        return app;
    }

    // ── Request body records ──────────────────────────────────────────────────

    public sealed record ExecuteRequest(
        string?               ConfirmToken,
        IReadOnlyList<string>? RoleIds,
        IReadOnlyList<string>? UserIds);

    public sealed record RestoreRequest(string? ConfirmToken);
}
