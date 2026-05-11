using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Local cache of VRChat group members.
/// Read operations are served entirely from SQLite.
/// RefreshAsync() is the only operation that calls the VRChat API.
/// </summary>
public sealed class MemberService : IMemberService
{
    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IVrchatApiClient                  _vrc;
    private readonly ILogger<MemberService>            _log;

    // Tracks per-group refresh timestamps in memory so we don't need a
    // dedicated DB table.  Persisted value is derived from MAX(CachedAt).
    public MemberService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IVrchatApiClient                  vrc,
        ILogger<MemberService>            log)
    {
        _dbFactory = dbFactory;
        _vrc       = vrc;
        _log       = log;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<MemberPageDto> GetPageAsync(
        string  groupId,
        int     page     = 0,
        int     pageSize = 50,
        string? search   = null,
        string? roleId   = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.GroupMembers
            .Where(m => m.GroupId == groupId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m =>
                m.DisplayName.ToLower().Contains(term) ||
                m.UserId.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(roleId))
        {
            // RoleIds column is pipe-delimited; SQLite LIKE is the simplest filter
            var pattern = $"%{roleId}%";
            query = query.Where(m => m.RoleIds != null && EF.Functions.Like(m.RoleIds, pattern));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(m => m.DisplayName)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Derive last-refreshed from MAX(CachedAt) in this group
        DateTime? lastRefreshedAt = await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .MaxAsync(m => (DateTime?)m.CachedAt, ct);

        double? ageSeconds = lastRefreshedAt.HasValue
            ? Math.Round((DateTime.UtcNow - lastRefreshedAt.Value).TotalSeconds, 1)
            : null;

        return new MemberPageDto(
            Items:          rows.Select(ToDto).ToList(),
            TotalCount:     total,
            Page:           page,
            PageSize:       pageSize,
            Source:         "cache",
            LastRefreshedAt: lastRefreshedAt,
            CacheAgeSeconds: ageSeconds);
    }

    public async Task<GroupMemberDto?> GetMemberAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<DateTime?> GetLastRefreshedAtAsync(
        string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .MaxAsync(m => (DateTime?)m.CachedAt, ct);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task<(int count, DateTime refreshedAt)> RefreshAsync(
        string groupId,
        IProgress<(int fetched, int total)>? progress = null,
        CancellationToken ct = default)
    {
        _log.LogInformation("Starting member cache refresh for group {GroupId}", groupId);

        // Fetch from VRChat API (paginated internally by VrchatApiClient)
        var vrcMembers = await _vrc.GetGroupMembersAsync(groupId, progress, ct);

        // Optionally fetch roles to resolve names (best-effort)
        Dictionary<string, string> roleNameMap;
        try
        {
            var roles = await _vrc.GetGroupRolesAsync(groupId, ct);
            roleNameMap = roles.ToDictionary(r => r.RoleId, r => r.Name);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not fetch roles for group {GroupId}; role names will be empty", groupId);
            roleNameMap = new();
        }

        var refreshedAt = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Delete existing cache for this group then bulk-insert fresh data.
        // ExecuteDeleteAsync is EF 7+ and avoids loading all rows into memory.
        await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .ExecuteDeleteAsync(ct);

        var entities = vrcMembers.Select(m =>
        {
            var roleIds   = m.RoleIds.ToList();
            var roleNames = roleIds
                .Select(id => roleNameMap.TryGetValue(id, out var n) ? n : id)
                .ToList();
            return new GroupMember
            {
                GroupId        = groupId,
                UserId         = m.UserId,
                DisplayName    = m.DisplayName,
                JoinedAt       = m.JoinedAt,
                RoleIds        = roleIds.Count > 0   ? string.Join('|', roleIds)   : null,
                RoleNames      = roleNames.Count > 0 ? string.Join('|', roleNames) : null,
                IsRepresenting = m.IsRepresenting,
                CachedAt       = refreshedAt,
            };
        }).ToList();

        // Bulk insert in batches of 500 to avoid SQLite parameter limits
        const int batchSize = 500;
        for (int i = 0; i < entities.Count; i += batchSize)
        {
            db.GroupMembers.AddRange(entities.Skip(i).Take(batchSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear(); // prevent memory build-up for large groups
        }

        _log.LogInformation(
            "Member cache refreshed for group {GroupId}: {Count} members at {At}",
            groupId, entities.Count, refreshedAt);

        return (entities.Count, refreshedAt);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static GroupMemberDto ToDto(GroupMember e)
    {
        static IReadOnlyList<string> Split(string? s) =>
            string.IsNullOrEmpty(s)
                ? Array.Empty<string>()
                : s.Split('|', StringSplitOptions.RemoveEmptyEntries);

        return new GroupMemberDto(
            e.GroupId,
            e.UserId,
            e.DisplayName,
            e.JoinedAt,
            Split(e.RoleIds),
            Split(e.RoleNames),
            e.AvatarUrl,
            e.IsRepresenting,
            e.CachedAt);
    }
}
