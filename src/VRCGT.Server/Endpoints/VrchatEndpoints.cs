using VRCGT.Core;
using VRCGT.Infrastructure;

namespace VRCGT.Server.Endpoints;

public static class VrchatEndpoints
{
    public static IEndpointRouteBuilder MapVrchatEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vrchat")
                   .RequireAuthorization(); // must be a logged-in local VRCGT user

        // ── GET /api/vrchat/session ───────────────────────────────────────────
        // Returns current in-process VRChat session state.
        // Cookies are NEVER included in the response.
        g.MapGet("/session", (IVrchatApiClient vrc) =>
            Results.Ok(vrc.GetSession()));

        // ── POST /api/vrchat/login ────────────────────────────────────────────
        // Initiates a VRChat login.
        // On 2FA requirement:  200 { requiresTwoFactor: true, twoFactorTypes: [...] }
        // On success:          200 { requiresTwoFactor: false, userId, displayName }
        // On failure:          401 { error }
        g.MapPost("/login", async (VrchatLoginRequest req, IVrchatApiClient vrc, CancellationToken ct) =>
        {
            VrchatLoginResultDto result;
            try
            {
                result = await vrc.LoginAsync(req.Username, req.Password, ct);
            }
            catch (VrchatRateLimitException ex)
            {
                return Results.Json(
                    new { error = "rate_limited", retryAfterSeconds = ex.RetryAfter?.TotalSeconds },
                    statusCode: 429);
            }

            if (result.RequiresTwoFactor)
            {
                return Results.Ok(new
                {
                    requiresTwoFactor = true,
                    twoFactorTypes    = result.TwoFactorTypes,
                });
            }

            if (!result.Success)
            {
                return Results.Json(
                    new { error = "login_failed", message = result.ErrorMessage },
                    statusCode: 401);
            }

            // Login success — never include token/cookie in response
            return Results.Ok(new
            {
                requiresTwoFactor = false,
                userId            = result.UserId,
                displayName       = result.DisplayName,
            });
        });

        // ── POST /api/vrchat/2fa ──────────────────────────────────────────────
        // Completes a pending 2FA challenge.
        // On success:  200 { userId, displayName }
        // On failure:  401 { error }
        g.MapPost("/2fa", async (Vrchat2FARequest req, IVrchatApiClient vrc, CancellationToken ct) =>
        {
            VrchatLoginResultDto result;
            try
            {
                result = await vrc.Verify2FAAsync(req.Code, req.Type, ct);
            }
            catch (VrchatRateLimitException ex)
            {
                return Results.Json(
                    new { error = "rate_limited", retryAfterSeconds = ex.RetryAfter?.TotalSeconds },
                    statusCode: 429);
            }

            if (!result.Success)
            {
                return Results.Json(
                    new { error = "invalid_code", message = result.ErrorMessage },
                    statusCode: 401);
            }

            return Results.Ok(new
            {
                userId      = result.UserId,
                displayName = result.DisplayName,
            });
        });

        // ── POST /api/vrchat/logout ───────────────────────────────────────────
        // Signs out of VRChat. Always returns 200.
        g.MapPost("/logout", async (IVrchatApiClient vrc, CancellationToken ct) =>
        {
            await vrc.LogoutAsync(ct);
            return Results.Ok();
        });

        return app;
    }
}

// ── Request records ───────────────────────────────────────────────────────────

internal sealed record VrchatLoginRequest(string Username, string Password);

internal sealed record Vrchat2FARequest(
    string Code,
    /// <summary>"totp" | "otp" | "emailotp"</summary>
    string Type);
