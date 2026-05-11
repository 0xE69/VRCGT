namespace VRCGT.Core;

// ── Status enum ───────────────────────────────────────────────────────────────

public enum BadgeScanStatus
{
    Pending   = 0,
    Running   = 1,
    Completed = 2,
    Failed    = 3,
    Cancelled = 4,
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record BadgeScanMemberDto(
    long     Id,
    string   UserId,
    string   DisplayName,
    bool?    IsAgeVerified,   // null = unknown (API error)
    bool     WasKicked,
    DateTime ScannedAt
);

/// <summary>Summary without the full member list — for list/latest endpoints.</summary>
public sealed record BadgeScanSummaryDto(
    long          Id,
    string        GroupId,
    BadgeScanStatus Status,
    DateTime      CreatedAt,
    DateTime?     CompletedAt,
    int           TotalMembers,
    int           ScannedMembers,
    int           VerifiedCount,
    int           UnverifiedCount,
    int           UnknownCount,
    int           KickedCount,
    long?         JobId,
    string?       ErrorMessage
);

/// <summary>Full scan detail with all member results.</summary>
public sealed record BadgeScanDetailDto(
    long          Id,
    string        GroupId,
    BadgeScanStatus Status,
    DateTime      CreatedAt,
    DateTime?     CompletedAt,
    int           TotalMembers,
    int           ScannedMembers,
    int           VerifiedCount,
    int           UnverifiedCount,
    int           UnknownCount,
    int           KickedCount,
    long?         JobId,
    string?       ErrorMessage,
    IReadOnlyList<BadgeScanMemberDto> Members
);

// ── Service interface ─────────────────────────────────────────────────────────

public interface IBadgeScanService
{
    /// <summary>Returns the most recent scan for a group, or null if none exists.</summary>
    Task<BadgeScanSummaryDto?> GetLatestAsync(string groupId, CancellationToken ct = default);

    /// <summary>Returns a specific scan with all member results.</summary>
    Task<BadgeScanDetailDto?> GetScanAsync(long scanId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new BadgeScan record and enqueues the "badge-scan" background job.
    /// Returns the summary including the new JobId.
    /// </summary>
    Task<BadgeScanSummaryDto> StartScanAsync(
        string groupId, IBackgroundJobQueue queue, CancellationToken ct = default);

    /// <summary>
    /// Core scan logic — called by the background job handler.
    /// Fetches group members, calls GetUserAsync per member, persists results.
    /// </summary>
    Task RunScanJobAsync(long scanId, Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default);

    /// <summary>
    /// Kick a specific list of members from the scan results.
    /// Called by the background job handler for "badge-kick-selected".
    /// </summary>
    Task RunKickSelectedJobAsync(long scanId, IReadOnlyList<string> userIds,
        Func<int, int, string, int, Task> reportProgress, CancellationToken ct = default);

    /// <summary>
    /// Kick all unverified members found in the scan.
    /// Called by the background job handler for "badge-kick-unverified".
    /// </summary>
    Task RunKickUnverifiedJobAsync(long scanId,
        Func<int, int, string, int, Task> reportProgress, CancellationToken ct = default);
}
