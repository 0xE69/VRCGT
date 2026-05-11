// VRCGT.Core — VRChat API DTOs, exceptions, and interface
// No ASP.NET Core, EF Core, or infrastructure references here.

namespace VRCGT.Core;

// ── Exceptions ────────────────────────────────────────────────────────────────

/// <summary>
/// Thrown when the VRChat API returns HTTP 429 Too Many Requests
/// and the client has exhausted its retry budget.
/// </summary>
public sealed class VrchatRateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public VrchatRateLimitException(TimeSpan? retryAfter = null)
        : base(retryAfter.HasValue
            ? $"VRChat API rate limit exceeded. Retry after {retryAfter.Value.TotalSeconds:F0}s."
            : "VRChat API rate limit exceeded.")
        => RetryAfter = retryAfter;
}

/// <summary>
/// Thrown when the VRChat API returns a non-recoverable error response.
/// </summary>
public sealed class VrchatApiException : Exception
{
    public int StatusCode { get; }

    public VrchatApiException(int statusCode, string message)
        : base(message) => StatusCode = statusCode;
}

// ── Session / login DTOs ──────────────────────────────────────────────────────

/// <summary>
/// Current in-process VRChat session state.
/// Cookies/tokens are NEVER included here.
/// </summary>
public sealed record VrchatSessionDto(
    bool    IsLoggedIn,
    string? UserId,
    string? DisplayName,
    string? ProfilePicUrl
);

/// <summary>Result of a login or 2FA verification attempt.</summary>
public sealed record VrchatLoginResultDto(
    bool                  Success,
    bool                  RequiresTwoFactor,
    IReadOnlyList<string> TwoFactorTypes,
    string?               UserId,
    string?               DisplayName,
    string?               ProfilePicUrl,
    string?               ErrorMessage
);

// ── VRChat object DTOs ────────────────────────────────────────────────────────

public sealed record VrchatCurrentUserDto(
    string                Id,
    string                DisplayName,
    string?               Bio,
    string?               ProfilePicUrl,
    IReadOnlyList<string> Tags,
    bool                  IsAgeVerified
);

public sealed record VrchatGroupDto(
    string    Id,
    string    Name,
    string?   ShortCode,
    string?   Description,
    int       MemberCount,
    int       OnlineCount,
    string?   IconUrl,
    string?   BannerUrl,
    string?   Privacy,
    string?   OwnerId,
    DateTime? CreatedAt
);

public sealed record VrchatGroupMemberDto(
    string                UserId,
    string                DisplayName,
    string?               JoinedAt,
    IReadOnlyList<string> RoleIds,
    bool                  IsRepresenting
);

public sealed record VrchatGroupRoleDto(
    string                RoleId,
    string                Name,
    string?               Description,
    IReadOnlyList<string> Permissions
);

public sealed record VrchatBanDto(
    string  UserId,
    string  DisplayName,
    string? Reason,
    string? BannedAt,
    string? ExpiresAt
);

public sealed record VrchatJoinRequestDto(
    string  UserId,
    string  DisplayName,
    string? ProfilePicUrl,
    string? CreatedAt,
    bool    IsAgeVerified
);

/// <summary>
/// Lightweight user summary returned by GET /users/{userId}.
/// Carries only the fields needed for badge scanning.
/// </summary>
public sealed record VrchatUserSummaryDto(
    string  UserId,
    string  DisplayName,
    bool    IsAgeVerified
);

public sealed record VrchatAuditLogPageDto(
    IReadOnlyList<VrchatAuditLogEntryDto> Results,
    int                                   TotalCount
);

public sealed record VrchatAuditLogEntryDto(
    string? EventType,
    string? ActorId,
    string? ActorDisplayName,
    string? TargetId,
    string? TargetDisplayName,
    string? CreatedAt,
    string? Description
);

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Clean v2 VRChat API client — no WPF, no App.Services, no MessageBox.
/// All errors are typed exceptions or nullable returns.
/// Implemented by VRCGT.Infrastructure.VrchatApiClient.
/// </summary>
public interface IVrchatApiClient
{
    // ---- Session ----

    /// <summary>Returns current in-process session state (synchronous — reads cached state).</summary>
    VrchatSessionDto GetSession();

    /// <summary>Login with VRChat credentials. May return RequiresTwoFactor = true.</summary>
    Task<VrchatLoginResultDto> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Complete a pending 2FA challenge. authType: "totp" | "otp" | "emailotp".</summary>
    Task<VrchatLoginResultDto> Verify2FAAsync(string code, string authType, CancellationToken ct = default);

    /// <summary>
    /// Restore a previously saved session from stored cookie values.
    /// Validates against the VRChat API before marking the session as active.
    /// </summary>
    Task<VrchatLoginResultDto> RestoreSessionAsync(string authCookie, string? twoFactorCookie, CancellationToken ct = default);

    /// <summary>Sign out of VRChat and clear the local session.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    // ---- Current user ----

    /// <summary>Returns the authenticated VRChat user, or null on error.</summary>
    Task<VrchatCurrentUserDto?> GetCurrentUserAsync(CancellationToken ct = default);

    // ---- Groups ----

    Task<VrchatGroupDto?>             GetGroupAsync(string groupId, CancellationToken ct = default);
    Task<IReadOnlyList<VrchatGroupDto>> GetMyManageableGroupsAsync(CancellationToken ct = default);

    // ---- Users ----

    /// <summary>Returns a user summary (with age-verification status) or null on error.</summary>
    Task<VrchatUserSummaryDto?> GetUserAsync(string userId, CancellationToken ct = default);

    // ---- Members ----

    Task<IReadOnlyList<VrchatGroupMemberDto>> GetGroupMembersAsync(
        string groupId,
        IProgress<(int fetched, int total)>? progress = null,
        CancellationToken ct = default);

    Task<VrchatGroupMemberDto?> GetGroupMemberAsync(string groupId, string userId, CancellationToken ct = default);

    // ---- Roles ----

    Task<IReadOnlyList<VrchatGroupRoleDto>> GetGroupRolesAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Removes a single role from a group member.
    /// Returns true on success, false on non-fatal failure.
    /// </summary>
    Task<bool> RemoveRoleFromGroupMemberAsync(string groupId, string userId, string roleId, CancellationToken ct = default);

    /// <summary>
    /// Assigns a single role to a group member (used by kill-switch restore).
    /// Returns true on success, false on non-fatal failure.
    /// </summary>
    Task<bool> AssignRoleToGroupMemberAsync(string groupId, string userId, string roleId, CancellationToken ct = default);

    // ---- Bans ----

    Task<IReadOnlyList<VrchatBanDto>> GetGroupBansAsync(
        string groupId,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    Task<bool> BanGroupMemberAsync(string groupId, string userId, CancellationToken ct = default);
    Task<bool> UnbanGroupMemberAsync(string groupId, string userId, CancellationToken ct = default);
    Task<bool> KickGroupMemberAsync(string groupId, string userId, CancellationToken ct = default);

    // ---- Audit logs ----

    Task<VrchatAuditLogPageDto> GetAuditLogsAsync(
        string groupId, int count = 100, int offset = 0, CancellationToken ct = default);

    // ---- Join requests ----

    Task<IReadOnlyList<VrchatJoinRequestDto>> GetJoinRequestsAsync(string groupId, CancellationToken ct = default);

    /// <param name="action">"accept" or "reject"</param>
    Task<bool> RespondToJoinRequestAsync(string groupId, string userId, string action, CancellationToken ct = default);

    // ---- Invites ----

    Task<bool> SendGroupInviteAsync(string groupId, string userId, CancellationToken ct = default);
}
