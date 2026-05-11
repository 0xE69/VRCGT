using VRCGT.Core;

namespace VRCGT.Server.Endpoints;
public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/groups/{groupId}/members")
                   .RequireAuthorization();

        // ── GET /api/groups/{groupId}/members ─────────────────────────────────
        // Query: page (0-based), pageSize (1-200), search, role
        // Served entirely from the local cache. Never calls VRChat API.
        g.MapGet("/", async (
            string groupId,
            int?   page,
            int?   pageSize,
            string? search,
            string? role,
            IMemberService svc,
            CancellationToken ct) =>
        {
            var result = await svc.GetPageAsync(
                groupId,
                page     ?? 0,
                pageSize ?? 50,
                search,
                role,
                ct);
            return Results.Ok(result);
        });

        // ── GET /api/groups/{groupId}/members/{userId} ────────────────────────
        // Returns a single cached member.
        g.MapGet("/{userId}", async (
            string groupId,
            string userId,
            IMemberService svc,
            CancellationToken ct) =>
        {
            var dto = await svc.GetMemberAsync(groupId, userId, ct);
            if (dto is null)
                return Results.Json(new { error = "not_found" }, statusCode: 404);

            var ageSeconds = Math.Round(
                (DateTime.UtcNow - dto.CachedAt).TotalSeconds, 1);

            return Results.Ok(new
            {
                member         = dto,
                source         = "cache",
                cacheAgeSeconds = ageSeconds,
            });
        });

        // ── POST /api/groups/{groupId}/members/refresh ────────────────────────
        // Body: { "confirmToken": "REFRESH_MEMBERS:{groupId}" }
        // Fetches ALL members from the VRChat API and replaces the cache.
        // Returns { count, refreshedAt, source: "live" }.
        // For large groups this may take 10–30 s — the client should show a
        // progress indicator.  The endpoint streams no progress events at this
        // stage; a SignalR/SSE upgrade is Phase 3.
        g.MapPost("/refresh", async (
            string                    groupId,
            MemberRefreshRequest?     req,
            IMemberService            svc,
            CancellationToken         ct) =>
        {
            if (!ConfirmationGuard.IsValid(req?.ConfirmToken, ConfirmationGuard.RefreshMembers, groupId))
                return ConfirmationResults.ConfirmationRequired(ConfirmationGuard.RefreshMembers, groupId);

            try
            {
                var (count, at) = await svc.RefreshAsync(groupId, progress: null, ct);
                return Results.Ok(new
                {
                    count,
                    refreshedAt = at,
                    source      = "live",
                });
            }
            catch (VrchatRateLimitException ex)
            {
                return Results.Json(
                    new { error = "rate_limited", retryAfterSeconds = ex.RetryAfter?.TotalSeconds },
                    statusCode: 429);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = "not_found", message = ex.Message },
                    statusCode: 404);
            }
        });

        return app;
    }
}

internal sealed record MemberRefreshRequest(string? ConfirmToken);
