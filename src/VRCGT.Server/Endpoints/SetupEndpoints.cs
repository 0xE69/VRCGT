using VRCGT.Core;

namespace VRCGT.Server.Endpoints;

/// <summary>
/// First-run setup endpoints.
///
/// GET  /api/setup/status   — returns whether setup is required (no auth needed)
/// POST /api/setup/owner    — creates the first Owner account (only when no Owner exists)
/// </summary>
public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup");

        // ----------------------------------------------------------------
        // GET /api/setup/status
        // Returns setupRequired:true if no Owner account exists.
        // Always accessible — this is how the UI decides whether to show
        // the setup screen or the login screen.
        // ----------------------------------------------------------------
        group.MapGet("/status", async (ILocalUserService users) =>
        {
            var required = await users.IsSetupRequiredAsync();
            return Results.Ok(new { setupRequired = required });
        });

        // ----------------------------------------------------------------
        // POST /api/setup/owner
        // Body: { "username": "...", "password": "...", "displayName": "..." }
        // Creates the first Owner account. Rejected if one already exists.
        // ----------------------------------------------------------------
        group.MapPost("/owner", async (CreateOwnerRequest req, ILocalUserService users) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Password) ||
                string.IsNullOrWhiteSpace(req.DisplayName))
            {
                return Results.BadRequest(new { error = "username, password, and displayName are required." });
            }

            try
            {
                var user = await users.CreateOwnerAsync(req.Username, req.Password, req.DisplayName);
                return Results.Ok(new
                {
                    message = "Owner account created. You can now log in.",
                    user
                });
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

        return app;
    }

    private sealed record CreateOwnerRequest(string Username, string Password, string DisplayName);
}
