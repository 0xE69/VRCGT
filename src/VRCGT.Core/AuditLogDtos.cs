namespace VRCGT.Core;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record AuditLogEntryDto(
    long      Id,
    string    GroupId,
    DateTime  Timestamp,
    string    Action,
    string?   ActorId,
    string?   ActorDisplayName,
    string?   TargetId,
    string?   TargetDisplayName,
    string?   Description,
    string    Source,       // "vrchat" | "vrcgt"
    DateTime  FetchedAt
);

public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogEntryDto> Items,
    int      TotalCount,
    int      Page,
    int      PageSize,
    long?    LatestId,
    DateTime? LatestTimestamp
);

// ── Service interface ─────────────────────────────────────────────────────────

public interface IAuditLogService
{
    /// <summary>
    /// Paged, filtered query against the local audit log cache.
    /// Never calls the VRChat API.
    /// </summary>
    Task<AuditLogPageDto> GetPageAsync(
        string    groupId,
        int       page,
        int       pageSize,
        string?   search,
        string?   action,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the timestamp of the newest locally-stored entry for the group.
    /// Returns null when the local cache is empty.
    /// </summary>
    Task<DateTime?> GetHighWatermarkAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Fetches recent VRChat audit events (first page) and stores any entries
    /// newer than the current high-watermark.  Returns the number of new rows.
    /// Called by the background polling service.
    /// </summary>
    Task<int> SyncRecentAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Full history back-fill: walks all audit log pages from VRChat and
    /// inserts entries not already in the local cache.  Designed to run inside
    /// a background job; <paramref name="reportProgress"/> is invoked after
    /// each batch.
    /// </summary>
    Task FetchHistoryAsync(
        string groupId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default);

    /// <summary>
    /// Records a locally-generated VRCGT moderation event in the audit log
    /// (e.g. a kick or ban triggered through the web UI).
    /// </summary>
    Task WriteLocalEventAsync(
        string  groupId,
        string  action,
        string? actorId,
        string? actorDisplayName,
        string? targetId,
        string? targetDisplayName,
        string? description,
        CancellationToken ct = default);

    /// <summary>
    /// Streams audit log rows for the group as CSV lines (header first).
    /// Applies the same search/filter/date range as <see cref="GetPageAsync"/>
    /// but returns all matching rows.
    /// </summary>
    IAsyncEnumerable<string> ExportCsvAsync(
        string    groupId,
        string?   search,
        string?   action,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all entries with Id &gt; <paramref name="afterId"/> for live
    /// update delivery via SignalR.
    /// </summary>
    Task<IReadOnlyList<AuditLogEntryDto>> GetNewSinceIdAsync(
        string groupId,
        long   afterId,
        CancellationToken ct = default);
}
