using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;

namespace VRCGT.Server.Middleware;

/// <summary>
/// Replaces the default authorization result handler so that 401 and 403
/// responses always return JSON instead of HTML redirect responses.
/// For 403 results on permission-policy endpoints the response body includes
/// which permission was required, giving callers a useful diagnostic.
/// </summary>
public sealed class PermissionDeniedHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate           next,
        HttpContext               context,
        AuthorizationPolicy      policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "unauthenticated" });
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode  = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var requiredPerms = policy.Requirements
                .OfType<ClaimsAuthorizationRequirement>()
                .Where(r => r.ClaimType == "vrcgt.perm")
                .SelectMany(r => r.AllowedValues ?? [])
                .ToList();

            await context.Response.WriteAsJsonAsync(new
            {
                error = "forbidden",
                requiredPermissions = requiredPerms,
                message = requiredPerms.Count > 0
                    ? $"This action requires the '{requiredPerms[0]}' permission."
                    : "Access denied."
            });
            return;
        }

        await next(context);
    }
}
