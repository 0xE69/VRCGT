using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VRCGT.Core;

namespace VRCGT.Server.Endpoints;

/// <summary>
/// CRUD endpoints for local VRCGT user accounts.
/// All routes require the <c>ManageLocalUsers</c> permission.
///
/// GET    /api/local-users/
/// POST   /api/local-users/
/// PATCH  /api/local-users/{id}
/// DELETE /api/local-users/{id}
/// POST   /api/local-users/{id}/reset-password
/// </summary>
public static class LocalUserEndpoints
{
    public static IEndpointRouteBuilder MapLocalUserEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/local-users")
            .RequireAuthorization("perm:ManageLocalUsers");

        // ── GET /api/local-users/ ─────────────────────────────────────────────
        grp.MapGet("/", async (ILocalUserService users) =>
        {
            var list = await users.ListAsync();
            return Results.Ok(list);
        });

        // ── POST /api/local-users/ ────────────────────────────────────────────
        grp.MapPost("/", async (
            CreateUserRequest body,
            ILocalUserService users,
            IAuditLogService  auditLog,
            ClaimsPrincipal   principal) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username) ||
                string.IsNullOrWhiteSpace(body.Password) ||
                string.IsNullOrWhiteSpace(body.DisplayName) ||
                string.IsNullOrWhiteSpace(body.Role))
            {
                return Results.BadRequest(new
                {
                    error = "username, password, displayName, and role are required."
                });
            }

            try
            {
                var user = await users.CreateUserAsync(
                    body.Username, body.Password, body.DisplayName, body.Role);

                await auditLog.WriteLocalEventAsync(
                    "$system",
                    "vrcgt.local_user.create",
                    principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    principal.FindFirstValue(ClaimTypes.GivenName),
                    user.Id.ToString(),
                    user.Username,
                    $"Created local user '{user.Username}' with role '{user.Role}'");

                return Results.Created($"/api/local-users/{user.Id}", user);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── PATCH /api/local-users/{id} ───────────────────────────────────────
        grp.MapMethods("/{id:int}", ["PATCH"], async (
            int               id,
            UpdateUserRequest body,
            ILocalUserService users,
            IAuditLogService  auditLog,
            ClaimsPrincipal   principal) =>
        {
            try
            {
                var user = await users.UpdateUserAsync(
                    id, body.DisplayName, body.Role, body.IsEnabled);

                await auditLog.WriteLocalEventAsync(
                    "$system",
                    "vrcgt.local_user.update",
                    principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    principal.FindFirstValue(ClaimTypes.GivenName),
                    user.Id.ToString(),
                    user.Username,
                    $"Updated local user '{user.Username}'");

                return Results.Ok(user);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"User {id} not found." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ── DELETE /api/local-users/{id} ──────────────────────────────────────
        grp.MapDelete("/{id:int}", async (
            int               id,
            ILocalUserService users,
            IAuditLogService  auditLog,
            ClaimsPrincipal   principal) =>
        {
            var target = await users.GetByIdAsync(id);
            if (target is null)
                return Results.NotFound(new { error = $"User {id} not found." });

            try
            {
                await users.DeleteUserAsync(id);

                await auditLog.WriteLocalEventAsync(
                    "$system",
                    "vrcgt.local_user.delete",
                    principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    principal.FindFirstValue(ClaimTypes.GivenName),
                    id.ToString(),
                    target.Username,
                    $"Deleted local user '{target.Username}' (role: {target.Role})");

                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // ── POST /api/local-users/{id}/reset-password ─────────────────────────
        grp.MapPost("/{id:int}/reset-password", async (
            int                  id,
            ResetPasswordRequest body,
            ILocalUserService    users,
            IAuditLogService     auditLog,
            ClaimsPrincipal      principal) =>
        {
            if (string.IsNullOrWhiteSpace(body.NewPassword))
                return Results.BadRequest(new { error = "newPassword is required." });

            try
            {
                await users.ResetPasswordAsync(id, body.NewPassword);

                var target = await users.GetByIdAsync(id);
                await auditLog.WriteLocalEventAsync(
                    "$system",
                    "vrcgt.local_user.password_reset",
                    principal.FindFirstValue(ClaimTypes.NameIdentifier),
                    principal.FindFirstValue(ClaimTypes.GivenName),
                    id.ToString(),
                    target?.Username,
                    $"Reset password for local user ID {id}");

                return Results.Ok(new { message = "Password reset successfully." });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"User {id} not found." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    // ── Request body records ──────────────────────────────────────────────────

    public sealed record CreateUserRequest(
        string Username,
        string Password,
        string DisplayName,
        string Role);

    public sealed record UpdateUserRequest(
        string? DisplayName,
        string? Role,
        bool?   IsEnabled);

    public sealed record ResetPasswordRequest(string NewPassword);
}
