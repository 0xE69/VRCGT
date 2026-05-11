using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Stage 14 — Kill Switch service.
///
/// Execution flow:
///   1.  Operator calls GetPreviewAsync() to see member/role state from cache.
///   2.  If cache is stale, operator calls RefreshAndPreviewAsync() first.
///   3.  Operator selects roles + users, confirms with typed phrase "REMOVE ROLES".
///   4.  StartExecuteAsync() creates a KillSwitchSnapshot (JSON blob) then enqueues
///       a "kill-switch-execute" background job.
///   5.  RunExecuteJobAsync() iterates affected members, removes each selected role
///       via VRChat API, and writes a local audit log entry per removal.
///   6.  At any time the operator can view snapshots via GetSnapshotsAsync() and
///       trigger StartRestoreAsync() → RunRestoreJobAsync() to re-apply roles.
///
/// Rate-limiting:  500ms delay per role-removal API call.
/// </summary>
public sealed class KillSwitchService : IKillSwitchService
{
    private static readonly TimeSpan RemoveDelay  = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IVrchatApiClient                  _vrc;
    private readonly IAuditLogService                  _audit;
    private readonly ILogger<KillSwitchService>        _log;

    public KillSwitchService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IVrchatApiClient                  vrc,
        IAuditLogService                  audit,
        ILogger<KillSwitchService>        log)
    {
        _dbFactory = dbFactory;
        _vrc       = vrc;
        _audit     = audit;
        _log       = log;
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    public async Task<KillSwitchPreviewDto> GetPreviewAsync(
        string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync(ct);

        DateTime? cacheTs = members.Count > 0
            ? members.Max(m => m.CachedAt)
            : null;

        bool isStale = cacheTs is null ||
            (DateTime.UtcNow - cacheTs.Value).TotalMinutes > KillSwitchPolicy.CacheStalenessMinutes;

        // Best-effort role name lookup from the VRChat API (cache-only approach
        // would require storing roles separately, which we don't have yet)
        IReadOnlyList<VrchatGroupRoleDto> availableRoles;
        try { availableRoles = await _vrc.GetGroupRolesAsync(groupId, ct); }
        catch { availableRoles = Array.Empty<VrchatGroupRoleDto>(); }

        var roleNameMap = availableRoles.ToDictionary(r => r.RoleId, r => r.Name);

        var affected = new List<KillSwitchMemberPreviewDto>();
        int totalAssignments = 0;

        foreach (var m in members)
        {
            var roleIds = SplitPipe(m.RoleIds);
            if (roleIds.Count == 0) continue;

            var roleNames = roleIds
                .Select(id => roleNameMap.TryGetValue(id, out var n) ? n : id)
                .ToList();

            totalAssignments += roleIds.Count;
            affected.Add(new KillSwitchMemberPreviewDto(
                m.UserId, m.DisplayName, roleIds, roleNames));
        }

        return new KillSwitchPreviewDto(
            MemberCount:          members.Count,
            RoleAssignmentCount:  totalAssignments,
            CacheTimestamp:       cacheTs,
            IsStale:              isStale,
            AffectedMembers:      affected,
            AvailableRoles:       availableRoles);
    }

    public async Task<KillSwitchPreviewDto> RefreshAndPreviewAsync(
        string groupId, CancellationToken ct = default)
    {
        _log.LogInformation("[KillSwitch] Refreshing member cache for {group}", groupId);

        // Fetch from VRChat API and persist (delegates to the same logic as MemberService.RefreshAsync)
        var vrcMembers = await _vrc.GetGroupMembersAsync(groupId, ct: ct);

        IReadOnlyList<VrchatGroupRoleDto> roles;
        try { roles = await _vrc.GetGroupRolesAsync(groupId, ct); }
        catch { roles = Array.Empty<VrchatGroupRoleDto>(); }

        var roleNameMap  = roles.ToDictionary(r => r.RoleId, r => r.Name);
        var refreshedAt  = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .ExecuteDeleteAsync(ct);

        var entities = vrcMembers.Select(m =>
        {
            var rIds   = m.RoleIds.ToList();
            var rNames = rIds.Select(id => roleNameMap.TryGetValue(id, out var n) ? n : id).ToList();
            return new GroupMember
            {
                GroupId        = groupId,
                UserId         = m.UserId,
                DisplayName    = m.DisplayName,
                JoinedAt       = m.JoinedAt,
                RoleIds        = rIds.Count   > 0 ? string.Join('|', rIds)   : null,
                RoleNames      = rNames.Count > 0 ? string.Join('|', rNames) : null,
                IsRepresenting = m.IsRepresenting,
                CachedAt       = refreshedAt,
            };
        }).ToList();

        const int batchSize = 500;
        for (int i = 0; i < entities.Count; i += batchSize)
        {
            db.GroupMembers.AddRange(entities.Skip(i).Take(batchSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        _log.LogInformation("[KillSwitch] Refreshed {n} members for {group}", entities.Count, groupId);

        // Build preview from the freshly cached data
        var affected = new List<KillSwitchMemberPreviewDto>();
        int totalAssignments = 0;

        foreach (var e in entities)
        {
            var rIds = SplitPipe(e.RoleIds);
            if (rIds.Count == 0) continue;

            var rNames = rIds.Select(id => roleNameMap.TryGetValue(id, out var n) ? n : id).ToList();
            totalAssignments += rIds.Count;
            affected.Add(new KillSwitchMemberPreviewDto(e.UserId, e.DisplayName, rIds, rNames));
        }

        return new KillSwitchPreviewDto(
            MemberCount:          entities.Count,
            RoleAssignmentCount:  totalAssignments,
            CacheTimestamp:       refreshedAt,
            IsStale:              false,
            AffectedMembers:      affected,
            AvailableRoles:       roles);
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    public async Task<KillSwitchExecuteResultDto> StartExecuteAsync(
        string                groupId,
        IReadOnlyList<string> roleIds,
        IReadOnlyList<string> userIds,
        IBackgroundJobQueue   queue,
        CancellationToken     ct = default)
    {
        // 1. Build snapshot from current cache
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.GroupMembers.Where(m => m.GroupId == groupId);
        if (userIds.Count > 0)
        {
            var userSet = userIds.ToHashSet();
            query = query.Where(m => userSet.Contains(m.UserId));
        }

        var members = await query.ToListAsync(ct);

        // Filter to only members who have at least one of the selected roles
        var roleSet = roleIds.ToHashSet();
        var snapshotEntries = members
            .Select(m =>
            {
                var rIds = SplitPipe(m.RoleIds).Where(roleSet.Contains).ToList();
                return (m.UserId, m.DisplayName, RoleIds: rIds);
            })
            .Where(x => x.RoleIds.Count > 0)
            .ToList();

        int totalAssignments = snapshotEntries.Sum(x => x.RoleIds.Count);

        var snapshotJson = JsonSerializer.Serialize(
            snapshotEntries.Select(x => new
            {
                x.UserId,
                x.DisplayName,
                x.RoleIds,
            }));

        var snapshot = new KillSwitchSnapshot
        {
            GroupId              = groupId,
            CreatedAt            = DateTime.UtcNow,
            MemberCount          = snapshotEntries.Count,
            RoleAssignmentCount  = totalAssignments,
            SnapshotJson         = snapshotJson,
        };
        db.KillSwitchSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        // 2. Enqueue the background job
        var job = new BackgroundJob
        {
            JobType        = "kill-switch-execute",
            Label          = $"Kill switch — remove roles in {groupId}",
            Status         = BackgroundJobStatus.Pending,
            TotalItems     = totalAssignments,
            CreatedAt      = DateTime.UtcNow,
            ParametersJson = JsonSerializer.Serialize(new
            {
                snapshotId = snapshot.Id,
                groupId,
                roleIds,
                userIds,
            }),
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);

        return new KillSwitchExecuteResultDto(snapshot.Id, job.Id);
    }

    public async Task RunExecuteJobAsync(
        long                             snapshotId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken                ct = default)
    {
        // Load parameters from the snapshot + job ParametersJson via snapshotId
        string groupId;
        List<string> roleIds;
        List<SnapshotEntry> entries;

        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var snap = await db.KillSwitchSnapshots.FindAsync(new object[] { snapshotId }, ct)
                ?? throw new InvalidOperationException($"KillSwitchSnapshot {snapshotId} not found");

            groupId  = snap.GroupId;
            entries  = JsonSerializer.Deserialize<List<SnapshotEntry>>(snap.SnapshotJson)
                       ?? new List<SnapshotEntry>();
            roleIds  = entries.SelectMany(e => e.RoleIds).Distinct().ToList();
        }

        int total   = entries.Sum(e => e.RoleIds.Count);
        int done    = 0;
        int failed  = 0;

        _log.LogInformation(
            "[KillSwitch] Starting execute job for snapshot {sid}: {n} role removals across {m} members",
            snapshotId, total, entries.Count);

        await reportProgress(0, total, $"Starting — {total} role removals…", 0);

        // Get current user session for audit log actor info
        var session = _vrc.GetSession();
        string? actorId   = session.UserId;
        string? actorName = session.DisplayName;

        foreach (var entry in entries)
        {
            foreach (var roleId in entry.RoleIds)
            {
                ct.ThrowIfCancellationRequested();

                bool ok = false;
                try
                {
                    ok = await _vrc.RemoveRoleFromGroupMemberAsync(groupId, entry.UserId, roleId, ct);
                }
                catch (VrchatRateLimitException)
                {
                    _log.LogWarning("[KillSwitch] Rate limited — pausing 30s");
                    await reportProgress(done, total, "Rate limited — waiting 30s…", failed);
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    try { ok = await _vrc.RemoveRoleFromGroupMemberAsync(groupId, entry.UserId, roleId, ct); }
                    catch { }
                }

                if (ok)
                {
                    // Write local audit log entry
                    await _audit.WriteLocalEventAsync(
                        groupId,
                        action:             "kill_switch.remove_role",
                        actorId:            actorId,
                        actorDisplayName:   actorName,
                        targetId:           entry.UserId,
                        targetDisplayName:  entry.DisplayName,
                        description:        $"Kill switch removed role {roleId}",
                        ct:                 ct);
                }
                else
                {
                    failed++;
                }

                done++;
                await reportProgress(done, total,
                    $"Removed {done}/{total} roles — {failed} failed", failed);

                await Task.Delay(RemoveDelay, ct);
            }
        }

        _log.LogInformation(
            "[KillSwitch] Execute complete: {done} removed, {failed} failed (snapshot {sid})",
            done, failed, snapshotId);
    }

    // ── Snapshots ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<KillSwitchSnapshotDto>> GetSnapshotsAsync(
        string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snaps = await db.KillSwitchSnapshots
            .Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return snaps.Select(ToDto).ToList();
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    public async Task<long> StartRestoreAsync(
        long                snapshotId,
        IBackgroundJobQueue queue,
        CancellationToken   ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snap = await db.KillSwitchSnapshots.FindAsync(new object[] { snapshotId }, ct)
            ?? throw new InvalidOperationException($"KillSwitchSnapshot {snapshotId} not found");

        var job = new BackgroundJob
        {
            JobType        = "kill-switch-restore",
            Label          = $"Kill switch restore — snapshot #{snapshotId}",
            Status         = BackgroundJobStatus.Pending,
            TotalItems     = snap.RoleAssignmentCount,
            CreatedAt      = DateTime.UtcNow,
            ParametersJson = JsonSerializer.Serialize(new { snapshotId }),
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);

        return job.Id;
    }

    public async Task RunRestoreJobAsync(
        long                             snapshotId,
        Func<int, int, string, int, Task> reportProgress,
        CancellationToken                ct = default)
    {
        string groupId;
        List<SnapshotEntry> entries;

        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var snap = await db.KillSwitchSnapshots.FindAsync(new object[] { snapshotId }, ct)
                ?? throw new InvalidOperationException($"KillSwitchSnapshot {snapshotId} not found");

            groupId = snap.GroupId;
            entries = JsonSerializer.Deserialize<List<SnapshotEntry>>(snap.SnapshotJson)
                      ?? new List<SnapshotEntry>();
        }

        int total  = entries.Sum(e => e.RoleIds.Count);
        int done   = 0;
        int failed = 0;

        _log.LogInformation(
            "[KillSwitch] Starting restore job for snapshot {sid}: {n} role re-assignments",
            snapshotId, total);

        await reportProgress(0, total, $"Restoring {total} role assignments…", 0);

        var session = _vrc.GetSession();
        string? actorId   = session.UserId;
        string? actorName = session.DisplayName;

        foreach (var entry in entries)
        {
            foreach (var roleId in entry.RoleIds)
            {
                ct.ThrowIfCancellationRequested();

                // VRChat: PUT /groups/{groupId}/members/{userId}/roles/{roleId}
                bool ok = false;
                try
                {
                    ok = await _vrc.AssignRoleToGroupMemberAsync(groupId, entry.UserId, roleId, ct);
                }
                catch (VrchatRateLimitException)
                {
                    _log.LogWarning("[KillSwitch] Rate limited during restore — pausing 30s");
                    await reportProgress(done, total, "Rate limited — waiting 30s…", failed);
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    try { ok = await _vrc.AssignRoleToGroupMemberAsync(groupId, entry.UserId, roleId, ct); }
                    catch { }
                }

                if (ok)
                {
                    await _audit.WriteLocalEventAsync(
                        groupId,
                        action:            "kill_switch.restore_role",
                        actorId:           actorId,
                        actorDisplayName:  actorName,
                        targetId:          entry.UserId,
                        targetDisplayName: entry.DisplayName,
                        description:       $"Kill switch restored role {roleId}",
                        ct:                ct);
                    done++;
                }
                else
                {
                    failed++;
                    done++;
                }

                await reportProgress(done, total,
                    $"Restored {done}/{total} roles — {failed} failed", failed);

                await Task.Delay(RestoreDelay, ct);
            }
        }

        // Update snapshot's restored counter
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var snap = await db.KillSwitchSnapshots.FindAsync(new object[] { snapshotId }, ct);
            if (snap is not null)
            {
                snap.RestoredCount += done - failed;
                snap.RestoredAt     = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        _log.LogInformation(
            "[KillSwitch] Restore complete: {done} roles re-applied, {failed} failed (snapshot {sid})",
            done - failed, failed, snapshotId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<string> SplitPipe(string? s) =>
        string.IsNullOrEmpty(s)
            ? Array.Empty<string>()
            : s.Split('|', StringSplitOptions.RemoveEmptyEntries);

    private static KillSwitchSnapshotDto ToDto(KillSwitchSnapshot s) => new(
        s.Id, s.GroupId, s.CreatedAt, s.MemberCount,
        s.RoleAssignmentCount, s.RestoredCount, s.RestoredAt, s.Label);

    // JSON de-serialisation helper for snapshot entries
    private sealed class SnapshotEntry
    {
        public string       UserId      { get; set; } = string.Empty;
        public string       DisplayName { get; set; } = string.Empty;
        public List<string> RoleIds     { get; set; } = new();
    }
}
