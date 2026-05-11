using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using VRCGT.Core;

namespace VRCGT.Server.Endpoints;

/// <summary>
/// Local authentication endpoints.
///
/// POST /api/auth/login    — authenticate with username + password; sets HttpOnly cookie
/// POST /api/auth/logout   — clears the auth cookie
/// GET  /api/auth/me       — returns the current authenticated user (requires auth)
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // ----------------------------------------------------------------
        // POST /api/auth/login
        // Body: { "username": "...", "password": "..." }
        // On success: sets a secure HttpOnly cookie and returns the user DTO.
        // On failure: 401 with a generic error (no information leakage).
        // ----------------------------------------------------------------
        group.MapPost("/login", async (
            LoginRequest       req,
            ILocalUserService  users,
            HttpContext        ctx) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "username and password are required." });

            var user = await users.AuthenticateAsync(req.Username, req.Password);
            if (user is null)
                // Intentionally vague — do not leak whether the username exists.
                return Results.Unauthorized();

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name,           user.Username),
                new(ClaimTypes.GivenName,      user.DisplayName),
                new(ClaimTypes.Role,           user.Role)
            };

            foreach (var perm in PermissionMap.For(user.Role))
                claims.Add(new Claim("vrcgt.perm", perm.ToString()));

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    // Cookie lives for 30 days; on browser restart the session continues
                    // because IsPersistent = true.  The server validates on every request
                    // by checking the DB user is still enabled.
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
                });

            return Results.Ok(user);
        });

        // ----------------------------------------------------------------
        // POST /api/auth/logout
        // Clears the auth cookie.  Always returns 200 even if not logged in.
        // ----------------------------------------------------------------
        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "Logged out." });
        });

        // ----------------------------------------------------------------
        // GET /api/auth/me
        // Returns the current user.  Requires authentication.
        // ----------------------------------------------------------------
        group.MapGet("/me", async (
            ClaimsPrincipal    principal,
            ILocalUserService  users) =>
        {
            var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (idClaim is null || !int.TryParse(idClaim, out var userId))
                return Results.Unauthorized();

            var user = await users.GetByIdAsync(userId);
            if (user is null || !user.IsEnabled)
                return Results.Unauthorized();

            return Results.Ok(user);
        }).RequireAuthorization();

        return app;
    }

    private sealed record LoginRequest(string Username, string Password);
}
