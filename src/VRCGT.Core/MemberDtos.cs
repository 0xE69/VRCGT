// VRCGT.Core — Member cache DTOs and service interface.

namespace VRCGT.Core;

/// <summary>Cached snapshot of a single group member for API responses.</summary>
public sealed record GroupMemberDto(
    string                GroupId,
    string                UserId,
    string                DisplayName,
    string?               JoinedAt,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> RoleNames,
    string?               AvatarUrl,
    bool                  IsRepresenting,
    DateTime              CachedAt
);

/// <summary>Paginated member list response with freshness metadata.</summary>
public sealed record MemberPageDto(
    IReadOnlyList<GroupMemberDto> Items,
    int                          TotalCount,
    int                          Page,
    int                          PageSize,
    /// <summary>"live" after a fresh pull, "cache" from the database.</summary>
    string                       Source,
    DateTime?                    LastRefreshedAt,
    double?                      CacheAgeSeconds
);

/// <summary>
/// Manages the local cache of group members.
/// Implemented by VRCGT.Infrastructure.MemberService.
/// </summary>
public interface IMemberService
{
    /// <summary>
    /// Returns a paginated, optionally filtered slice from the local cache.
    /// Does NOT call the VRChat API.
    /// </summary>
    Task<MemberPageDto> GetPageAsync(
        string  groupId,
        int     page      = 0,
        int     pageSize  = 50,
        string? search    = null,
        string? roleId    = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single cached member, or null if not in the cache.
    /// </summary>
    Task<GroupMemberDto?> GetMemberAsync(string groupId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Fetches ALL members from the VRChat API, replaces the local cache for this group,
    /// and returns the new total count + refresh timestamp.
    /// This may take seconds for large groups — callers should run it in a background context.
    /// </summary>
    Task<(int count, DateTime refreshedAt)> RefreshAsync(
        string groupId,
        IProgress<(int fetched, int total)>? progress = null,
        CancellationToken ct = default);

    /// <summary>Returns when the member cache for this group was last fully refreshed, or null.</summary>
    Task<DateTime?> GetLastRefreshedAtAsync(string groupId, CancellationToken ct = default);
}
