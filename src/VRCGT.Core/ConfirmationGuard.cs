namespace VRCGT.Core;

/// <summary>
/// Lightweight server-side confirmation guard for high-risk endpoints.
///
/// Prevents bulk-destructive actions from being triggered by:
///   - Accidental API calls from scripts
///   - Automated tools without the confirmation UI
///   - CSRF-style requests (combined with SameSite=Strict cookies)
///
/// Token format:  "{ACTION}:{RESOURCE_ID}"
/// Example:       "DELETE_GROUP:grp_abc123"
///
/// The client (frontend) generates the token from the confirmed action+resource
/// and includes it in the request body as "confirmToken".
/// The server validates that the token is structurally correct and matches
/// the resource being acted upon.
///
/// This is NOT a cryptographic check — it is a semantic guard that ensures
/// the confirmation UI ran and produced a valid token before the action.
/// The local-only architecture (127.0.0.1, SameSite=Strict) provides the
/// actual security boundary.
/// </summary>
public static class ConfirmationGuard
{
    // ── Known action constants ────────────────────────────────────────────────

    public const string DeleteGroup        = "DELETE_GROUP";
    public const string RefreshMembers     = "REFRESH_MEMBERS";
    public const string KickMember         = "KICK_MEMBER";
    public const string BanMember          = "BAN_MEMBER";
    public const string UnbanMember        = "UNBAN_MEMBER";
    public const string RemoveRoles        = "REMOVE_ROLES";
    public const string KickAllUnverified  = "KICK_ALL_UNVERIFIED";
    public const string KickSelectedBadge  = "KICK_SELECTED_BADGE";
    public const string DeleteBackup       = "DELETE_BACKUP";
    public const string RestoreBackup      = "RESTORE_BACKUP";
    public const string CloseInstance      = "CLOSE_INSTANCE";
    public const string DeletePost         = "DELETE_POST";
    public const string DeleteEvent        = "DELETE_EVENT";
    public const string EnableLanAccess    = "ENABLE_LAN_ACCESS";
    public const string DisableLanAccess   = "DISABLE_LAN_ACCESS";

    // ── Token helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the expected confirmation token for an action + resource combination.
    /// </summary>
    public static string BuildToken(string action, string resourceId)
        => $"{action}:{resourceId}";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="token"/> matches the expected
    /// token for the given action and resource ID.  Both parts are case-sensitive.
    /// </summary>
    public static bool IsValid(string? token, string action, string resourceId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var expected = BuildToken(action, resourceId);
        return string.Equals(token, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Throws <see cref="ConfirmationRequiredException"/> when the token is
    /// missing or does not match.
    /// </summary>
    public static void Require(string? token, string action, string resourceId)
    {
        if (!IsValid(token, action, resourceId))
            throw new ConfirmationRequiredException(action, resourceId);
    }
}

/// <summary>
/// Thrown by <see cref="ConfirmationGuard.Require"/> when a confirmation
/// token is absent or invalid.
/// </summary>
public sealed class ConfirmationRequiredException : Exception
{
    public string Action     { get; }
    public string ResourceId { get; }

    public ConfirmationRequiredException(string action, string resourceId)
        : base($"Confirmation required for {action}:{resourceId}.")
    {
        Action     = action;
        ResourceId = resourceId;
    }
}
