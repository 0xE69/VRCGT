using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VRCGT.Core;

namespace VRCGT.Server.Endpoints;

/// <summary>
/// Server access / bind-mode settings endpoints.
///
/// GET   /api/system/access        — returns current and saved bind mode (any authenticated user)
/// PATCH /api/system/access        — changes bind mode (requires ManageSecurity permission)
///
/// Changing bind mode persists a sidecar file.  The new setting takes effect
/// after the next server restart.  The response includes <c>requiresRestart: true</c>
/// when the saved mode differs from the current runtime mode.
/// </summary>
public static class SystemAccessEndpoints
{
    public static IEndpointRouteBuilder MapSystemAccessEndpoints(this IEndpointRouteBuilder app)
    {
        // ── GET /api/system/access ─────────────────────────────────────────────
        // Any authenticated user can read the current settings.
        app.MapGet("/api/system/access", async (
            IAccessSettingsService accessSvc,
            HttpContext            ctx) =>
        {
            var settings = await accessSvc.GetAsync(ctx.RequestAborted);
            return Results.Ok(settings);
        }).RequireAuthorization();

        // ── PATCH /api/system/access ───────────────────────────────────────────
        // Requires ManageSecurity permission + typed confirmation when enabling LAN.
        app.MapMethods("/api/system/access", ["PATCH"], async (
            UpdateAccessRequest    body,
            IAccessSettingsService accessSvc,
            IAuditLogService       auditLog,
            ClaimsPrincipal        principal,
            HttpContext            ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.BindMode))
                return Results.BadRequest(new { error = "bindMode is required." });

            if (!Enum.TryParse<BindMode>(body.BindMode, ignoreCase: true, out var newMode))
                return Results.BadRequest(new
                {
                    error = $"Invalid bindMode '{body.BindMode}'. Valid values: LocalOnly, Lan."
                });

            // ── Confirmation guard for enabling LAN ──────────────────────────
            if (newMode == BindMode.Lan)
            {
                if (!ConfirmationGuard.IsValid(
                        body.ConfirmToken, ConfirmationGuard.EnableLanAccess, "lan"))
                {
                    return ConfirmationResults.ConfirmationRequired(
                        ConfirmationGuard.EnableLanAccess, "lan");
                }
            }

            var settings = await accessSvc.UpdateAsync(newMode, ctx.RequestAborted);

            await auditLog.WriteLocalEventAsync(
                "$system",
                "vrcgt.access.bind_mode_changed",
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                principal.FindFirstValue(ClaimTypes.GivenName),
                null, null,
                $"Server bind mode changed to '{newMode}' (restart required: {settings.RequiresRestart})");

            return Results.Ok(settings);

        }).RequireAuthorization("perm:ManageSecurity");

        return app;
    }

    public sealed record UpdateAccessRequest(string? BindMode, string? ConfirmToken);
}
