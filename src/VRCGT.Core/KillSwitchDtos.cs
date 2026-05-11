namespace VRCGT.Core;

// ── Cache staleness threshold ─────────────────────────────────────────────────

/// <summary>
/// Member cache older than this is considered stale and blocks execution.
/// The user must explicitly refresh before the kill switch can run.
/// </summary>
public static class KillSwitchPolicy
{
    /// <summary>Age in minutes before the member cache is considered stale.</summary>
    public const int CacheStalenessMinutes = 30;
}

// ── Preview DTOs ──────────────────────────────────────────────────────────────

/// <summary>
/// A single member's contribution to the kill switch preview.
/// Shows which roles they hold (from cache) so the operator can decide.
/// </summary>
public sealed record KillSwitchMemberPreviewDto(
    string                UserId,
    string                DisplayName,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> RoleNames);

/// <summary>
/// Returned by GET /kill-switch/preview.
/// Summarises the current member/role state from the local cache
/// so the operator can review before executing.
/// </summary>
public sealed record KillSwitchPreviewDto(
    /// <summary>Total members in the local cache for the group.</summary>
    int             MemberCount,
    /// <summary>Sum of role assignments across all members.</summary>
    int             RoleAssignmentCount,
    /// <summary>When the cache was last populated (UTC). Null when no cache exists.</summary>
    DateTime?       CacheTimestamp,
    /// <summary>True when the cache is older than <see cref="KillSwitchPolicy.CacheStalenessMinutes"/>.</summary>
    bool            IsStale,
    /// <summary>Members with at least one non-owner role.</summary>
    IReadOnlyList<KillSwitchMemberPreviewDto> AffectedMembers,
    /// <summary>All roles currently configured for the group (from VRChat API).</summary>
    IReadOnlyList<VrchatGroupRoleDto>         AvailableRoles);

// ── Snapshot DTOs ─────────────────────────────────────────────────────────────

public sealed record KillSwitchSnapshotDto(
    long      Id,
    string    GroupId,
    DateTime  CreatedAt,
    int       MemberCount,
    int       RoleAssignmentCount,
    int       RestoredCount,
    DateTime? RestoredAt,
    string?   Label);

// ── Execute result ────────────────────────────────────────────────────────────

public sealed record KillSwitchExecuteResultDto(
    long SnapshotId,
    long JobId);

// ── Service interface ─────────────────────────────────────────────────────────

public interface IKillSwitchService
{
    /// <summary>
    /// Returns a preview of the current member/role state from the local cache.
    /// Never calls the VRChat API.
    /// </summary>
    Task<KillSwitchPreviewDto> GetPreviewAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Synchronously refreshes the member cache from the VRChat API, then
    /// returns an up-to-date preview.  May be slow — call from a background context.
    /// </summary>
    Task<KillSwitchPreviewDto> RefreshAndPreviewAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Creates a role snapshot, enqueues the remove-roles background job, and
    /// returns the snapshot ID + job ID.
    /// </summary>
    Task<KillSwitchExecuteResultDto> StartExecuteAsync(
        string                groupId,
        IReadOnlyList<string> roleIds,
        IReadOnlyList<string> userIds,
        IBackgroundJobQueue   queue,
        CancellationToken     ct = default);

    /// <summary>
    /// The background job body for role removal. Called by the job worker.
    /// </summary>
    Task RunExecuteJobAsync(
        long                             snapshotId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken                ct = default);

    /// <summary>
    /// Enqueues a restore job for a previously created snapshot.
    /// </summary>
    Task<long> StartRestoreAsync(
        long                snapshotId,
        IBackgroundJobQueue queue,
        CancellationToken   ct = default);

    /// <summary>
    /// The background job body for snapshot restore. Called by the job worker.
    /// </summary>
    Task RunRestoreJobAsync(
        long                             snapshotId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken                ct = default);

    /// <summary>
    /// Lists all snapshots for a group, newest first.
    /// </summary>
    Task<IReadOnlyList<KillSwitchSnapshotDto>> GetSnapshotsAsync(
        string groupId, CancellationToken ct = default);
}
