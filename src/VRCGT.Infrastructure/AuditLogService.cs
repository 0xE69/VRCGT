using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Implements <see cref="IAuditLogService"/>.
/// Uses IDbContextFactory so it can be safely called from background threads.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IVrchatApiClient                  _vrc;
    private readonly ILogger<AuditLogService>          _log;

    public AuditLogService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IVrchatApiClient                  vrc,
        ILogger<AuditLogService>          log)
    {
        _dbFactory = dbFactory;
        _vrc       = vrc;
        _log       = log;
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public async Task<AuditLogPageDto> GetPageAsync(
        string groupId, int page, int pageSize,
        string? search, string? action,
        DateTime? from, DateTime? to,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var q = db.AuditLog
            .Where(e => e.GroupId == groupId);

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(e => e.Action == action);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e =>
                (e.ActorDisplayName != null && EF.Functions.Like(e.ActorDisplayName.ToLower(), $"%{s}%")) ||
                (e.TargetDisplayName != null && EF.Functions.Like(e.TargetDisplayName.ToLower(), $"%{s}%")) ||
                (e.ActorId != null && EF.Functions.Like(e.ActorId.ToLower(), $"%{s}%")) ||
                (e.TargetId != null && EF.Functions.Like(e.TargetId.ToLower(), $"%{s}%")) ||
                (e.Description != null && EF.Functions.Like(e.Description.ToLower(), $"%{s}%")) ||
                EF.Functions.Like(e.Action.ToLower(), $"%{s}%"));
        }

        if (from.HasValue)
            q = q.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            q = q.Where(e => e.Timestamp <= to.Value);

        var totalCount = await q.CountAsync(ct);

        // Newest first — leverage (GroupId, Timestamp) index
        var items = await q
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(e => ToDto(e))
            .ToListAsync(ct);

        // Cheap high-watermark without a secondary query
        long?    latestId        = null;
        DateTime? latestTimestamp = null;
        if (items.Count > 0 && page == 0)
        {
            latestId        = items[0].Id;
            latestTimestamp = items[0].Timestamp;
        }

        return new AuditLogPageDto(items, totalCount, page, pageSize, latestId, latestTimestamp);
    }

    public async Task<DateTime?> GetHighWatermarkAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var max = await db.AuditLog
            .Where(e => e.GroupId == groupId)
            .MaxAsync(e => (DateTime?)e.Timestamp, ct);
        return max;
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> GetNewSinceIdAsync(
        string groupId, long afterId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLog
            .Where(e => e.GroupId == groupId && e.Id > afterId)
            .OrderBy(e => e.Id)
            .Take(500)
            .Select(e => ToDto(e))
            .ToListAsync(ct);
    }

    // ── Sync (polling) ────────────────────────────────────────────────────────

    public async Task<int> SyncRecentAsync(string groupId, CancellationToken ct = default)
    {
        var hwm = await GetHighWatermarkAsync(groupId, ct);

        // Fetch first page from VRChat (newest first)
        var page = await _vrc.GetAuditLogsAsync(groupId, 100, 0, ct);
        if (page.Results.Count == 0) return 0;

        var toInsert = FilterNewEntries(page.Results, groupId, hwm);
        if (toInsert.Count == 0) return 0;

        // If all 100 are new, the gap may be larger — fetch additional pages
        if (toInsert.Count == page.Results.Count && page.TotalCount > 100)
        {
            var page2 = await _vrc.GetAuditLogsAsync(groupId, 100, 100, ct);
            toInsert.AddRange(FilterNewEntries(page2.Results, groupId, hwm));
        }

        return await InsertAsync(toInsert, ct);
    }

    // ── Full history back-fill ────────────────────────────────────────────────

    public async Task FetchHistoryAsync(
        string groupId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken ct = default)
    {
        const int batchSize = 100;
        var firstPage = await _vrc.GetAuditLogsAsync(groupId, batchSize, 0, ct);
        int total = firstPage.TotalCount;
        int fetched = 0;
        int inserted = 0;

        _log.LogInformation("Audit history fetch for {group}: {total} total entries", groupId, total);

        // Insert first page
        var batch = ToEntities(firstPage.Results, groupId);
        inserted += await InsertAsync(batch, ct);
        fetched  += firstPage.Results.Count;
        await reportProgress(fetched, total, $"Fetched {fetched} / {total}", 0);

        // Walk remaining pages
        while (fetched < total)
        {
            ct.ThrowIfCancellationRequested();
            var next = await _vrc.GetAuditLogsAsync(groupId, batchSize, fetched, ct);
            if (next.Results.Count == 0) break;

            batch    = ToEntities(next.Results, groupId);
            inserted += await InsertAsync(batch, ct);
            fetched  += next.Results.Count;

            await reportProgress(fetched, total, $"Fetched {fetched} / {total}", 0);

            // Small courtesy delay — respect VRChat rate limits
            await Task.Delay(300, ct);
        }

        _log.LogInformation("Audit history fetch for {group} complete: {inserted} new rows", groupId, inserted);
    }

    // ── Local VRCGT event writing ─────────────────────────────────────────────

    public async Task WriteLocalEventAsync(
        string groupId, string action,
        string? actorId, string? actorDisplayName,
        string? targetId, string? targetDisplayName,
        string? description,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.AuditLog.Add(new AuditLogEntry
        {
            GroupId            = groupId,
            Timestamp          = DateTime.UtcNow,
            Action             = action,
            ActorId            = actorId,
            ActorDisplayName   = actorDisplayName,
            TargetId           = targetId,
            TargetDisplayName  = targetDisplayName,
            Description        = description,
            Source             = "vrcgt",
            FetchedAt          = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    // ── CSV export ────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> ExportCsvAsync(
        string groupId, string? search, string? action,
        DateTime? from, DateTime? to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "Id,Timestamp,Action,Source,ActorId,ActorDisplayName,TargetId,TargetDisplayName,Description";

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var q = db.AuditLog.Where(e => e.GroupId == groupId);

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(e => e.Action == action);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e =>
                (e.ActorDisplayName != null && EF.Functions.Like(e.ActorDisplayName.ToLower(), $"%{s}%")) ||
                (e.TargetDisplayName != null && EF.Functions.Like(e.TargetDisplayName.ToLower(), $"%{s}%")) ||
                (e.Description != null && EF.Functions.Like(e.Description.ToLower(), $"%{s}%")) ||
                EF.Functions.Like(e.Action.ToLower(), $"%{s}%"));
        }

        if (from.HasValue) q = q.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)   q = q.Where(e => e.Timestamp <= to.Value);

        var stream = q.OrderByDescending(e => e.Timestamp).AsAsyncEnumerable();
        await foreach (var e in stream.WithCancellation(ct))
            yield return CsvRow(e);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AuditLogEntryDto ToDto(AuditLogEntry e) => new(
        e.Id, e.GroupId, e.Timestamp, e.Action,
        e.ActorId, e.ActorDisplayName,
        e.TargetId, e.TargetDisplayName,
        e.Description, e.Source, e.FetchedAt);

    private static List<AuditLogEntry> ToEntities(
        IReadOnlyList<VrchatAuditLogEntryDto> src, string groupId)
    {
        var list = new List<AuditLogEntry>(src.Count);
        foreach (var s in src)
        {
            if (!DateTime.TryParse(s.CreatedAt, out var ts)) ts = DateTime.UtcNow;
            list.Add(new AuditLogEntry
            {
                GroupId           = groupId,
                Timestamp         = ts.ToUniversalTime(),
                Action            = s.EventType ?? "unknown",
                ActorId           = s.ActorId,
                ActorDisplayName  = s.ActorDisplayName,
                TargetId          = s.TargetId,
                TargetDisplayName = s.TargetDisplayName,
                Description       = s.Description,
                Source            = "vrchat",
                FetchedAt         = DateTime.UtcNow,
            });
        }
        return list;
    }

    private List<AuditLogEntry> FilterNewEntries(
        IReadOnlyList<VrchatAuditLogEntryDto> src, string groupId, DateTime? hwm)
    {
        var list = ToEntities(src, groupId);
        if (hwm.HasValue)
            list = list.Where(e => e.Timestamp > hwm.Value).ToList();
        return list;
    }

    /// <summary>
    /// Bulk-insert entries, skipping duplicates via the unique index.
    /// Returns the number of rows actually inserted.
    /// </summary>
    private async Task<int> InsertAsync(List<AuditLogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        int inserted = 0;

        foreach (var entry in entries)
        {
            // Check for existing entry using the natural dedup key
            bool exists = await db.AuditLog.AnyAsync(e =>
                e.GroupId  == entry.GroupId  &&
                e.Timestamp == entry.Timestamp &&
                e.Action    == entry.Action    &&
                e.ActorId   == entry.ActorId, ct);

            if (!exists)
            {
                db.AuditLog.Add(entry);
                inserted++;
            }
        }

        if (inserted > 0)
            await db.SaveChangesAsync(ct);

        return inserted;
    }

    private static string CsvRow(AuditLogEntry e)
    {
        static string Esc(string? v) =>
            v == null ? "" : "\"" + v.Replace("\"", "\"\"") + "\"";

        return string.Join(",",
            e.Id,
            e.Timestamp.ToString("o"),
            Esc(e.Action),
            Esc(e.Source),
            Esc(e.ActorId),
            Esc(e.ActorDisplayName),
            Esc(e.TargetId),
            Esc(e.TargetDisplayName),
            Esc(e.Description));
    }
}
