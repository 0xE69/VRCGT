using VRCGT.Core;

namespace VRCGT.Server.Middleware;

/// <summary>
/// Intercepts all API requests (paths starting with /api/) when setup has not
/// been completed yet, and allows only the /api/setup/* and /health endpoints
/// through.  All other API routes return 503 with a JSON body indicating that
/// setup is required.
///
/// Non-API requests (static files, SPA fallback) are not intercepted — the
/// browser needs to be able to load the setup page without being blocked.
/// </summary>
public sealed class FirstRunGateMiddleware
{
    private readonly RequestDelegate _next;

    public FirstRunGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ILocalUserService users)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Only gate API paths (and not SignalR hub paths)
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // SignalR hub upgrades — always pass through (auth is handled by the hub)
        if (path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // These paths are always permitted regardless of setup state
        if (path.StartsWith("/api/setup/",  StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v2/",     StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/system/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // Check setup state (result is tiny and quick — single COUNT query)
        var setupRequired = await users.IsSetupRequiredAsync(ctx.RequestAborted);
        if (setupRequired)
        {
            ctx.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                error        = "setup_required",
                message      = "VRCGT setup has not been completed. " +
                               "POST /api/setup/owner to create the first Owner account.",
                setupEndpoint = "/api/setup/owner"
            });
            return;
        }

        await _next(ctx);
    }
}
