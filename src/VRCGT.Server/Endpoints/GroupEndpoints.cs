using Microsoft.AspNetCore.Mvc;
using VRCGT.Core;

namespace VRCGT.Server.Endpoints;

public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/groups")
                   .RequireAuthorization();
        // ── GET /api/groups ───────────────────────────────────────────────────
        // Returns all managed groups (from DB cache — no VRChat API call).
        g.MapGet("/", async (IGroupService svc, CancellationToken ct) =>
        {
            var groups = await svc.GetAllAsync(ct);
            return Results.Ok(groups.Select(d => WithFreshness(d, "cache")));
        });

        // ── POST /api/groups ──────────────────────────────────────────────────
        // Body: { "groupId": "grp_..." }
        // Verifies the group in VRChat then persists it.
        g.MapPost("/", async (AddGroupRequest req, IGroupService svc, CancellationToken ct) =>
        {
            try
            {
                var dto = await svc.AddGroupAsync(req.GroupId, ct);
                return Results.Ok(WithFreshness(dto, "live"));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = "already_managed", message = ex.Message },
                    statusCode: 409);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = "group_not_found", message = ex.Message },
                    statusCode: 404);
            }
            catch (VrchatRateLimitException ex)
            {
                return Results.Json(
                    new { error = "rate_limited", retryAfterSeconds = ex.RetryAfter?.TotalSeconds },
                    statusCode: 429);
            }
        });

        // ── GET /api/groups/{groupId} ─────────────────────────────────────────
        // Returns the cached snapshot for one group.
        g.MapGet("/{groupId}", async (string groupId, IGroupService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetByGroupIdAsync(groupId, ct);
            if (dto is null)
                return Results.Json(new { error = "not_found" }, statusCode: 404);
            return Results.Ok(WithFreshness(dto, "cache"));
        });

        // ── POST /api/groups/{groupId}/refresh ────────────────────────────────
        // Re-fetches from VRChat, updates LastRefreshedAt.
        g.MapPost("/{groupId}/refresh", async (string groupId, IGroupService svc, CancellationToken ct) =>
        {
            try
            {
                var dto = await svc.RefreshGroupAsync(groupId, ct);
                return Results.Ok(WithFreshness(dto, "live"));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = "not_found", message = ex.Message },
                    statusCode: 404);
            }
            catch (VrchatRateLimitException ex)
            {
                return Results.Json(
                    new { error = "rate_limited", retryAfterSeconds = ex.RetryAfter?.TotalSeconds },
                    statusCode: 429);
            }
        });

        // ── DELETE /api/groups/{groupId} ──────────────────────────────────────
        // Body: { "confirmToken": "DELETE_GROUP:{groupId}" }
        g.MapDelete("/{groupId}", async (
            string              groupId,
            [FromBody] DeleteGroupRequest? req,
            IGroupService       svc,
            CancellationToken   ct) =>
        {
            if (!ConfirmationGuard.IsValid(req?.ConfirmToken, ConfirmationGuard.DeleteGroup, groupId))
                return ConfirmationResults.ConfirmationRequired(ConfirmationGuard.DeleteGroup, groupId);

            var removed = await svc.RemoveGroupAsync(groupId, ct);
            return removed
                ? Results.Ok()
                : Results.Json(new { error = "not_found" }, statusCode: 404);
        });

        return app;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object WithFreshness(ManagedGroupDto dto, string source)
    {
        double? ageSeconds = dto.LastRefreshedAt.HasValue
            ? Math.Round((DateTime.UtcNow - dto.LastRefreshedAt.Value).TotalSeconds, 1)
            : null;

        return new
        {
            groupId         = dto.GroupId,
            name            = dto.Name,
            shortCode       = dto.ShortCode,
            description     = dto.Description,
            iconUrl         = dto.IconUrl,
            bannerUrl       = dto.BannerUrl,
            isActive        = dto.IsActive,
            memberCount     = dto.MemberCount,
            onlineCount     = dto.OnlineCount,
            addedAt         = dto.AddedAt,
            lastRefreshedAt = dto.LastRefreshedAt,
            source,
            cacheAgeSeconds = ageSeconds,
        };
    }
}

internal sealed record AddGroupRequest(string GroupId);
internal sealed record DeleteGroupRequest(string? ConfirmToken);
