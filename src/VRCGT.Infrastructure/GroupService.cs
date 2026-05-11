using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Manages the set of VRChat groups tracked by VRCGT.
/// Uses <see cref="IDbContextFactory{VrcgtDbContext}"/> so each operation
/// creates a short-lived context — safe for scoped DI lifetime.
/// </summary>
public sealed class GroupService : IGroupService
{
    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IVrchatApiClient                  _vrc;
    private readonly ILogger<GroupService>             _log;

    public GroupService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IVrchatApiClient                  vrc,
        ILogger<GroupService>             log)
    {
        _dbFactory = dbFactory;
        _vrc       = vrc;
        _log       = log;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ManagedGroupDto>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ManagedGroups
            .OrderByDescending(g => g.IsActive)
            .ThenBy(g => g.SortOrder)
            .ThenBy(g => g.AddedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ManagedGroupDto?> GetByGroupIdAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.ManagedGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId, ct);
        return entity is null ? null : ToDto(entity);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<ManagedGroupDto> AddGroupAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (await db.ManagedGroups.AnyAsync(g => g.GroupId == groupId, ct))
            throw new InvalidOperationException($"Group '{groupId}' is already managed.");

        // Verify group exists in VRChat before persisting
        var vrcGroup = await _vrc.GetGroupAsync(groupId, ct)
            ?? throw new KeyNotFoundException($"Group '{groupId}' was not found in VRChat.");

        var isFirst = !await db.ManagedGroups.AnyAsync(ct);

        var entity = new ManagedGroup
        {
            GroupId         = vrcGroup.Id,
            Name            = vrcGroup.Name,
            ShortCode       = vrcGroup.ShortCode,
            Description     = vrcGroup.Description,
            IconUrl         = vrcGroup.IconUrl,
            BannerUrl       = vrcGroup.BannerUrl,
            MemberCount     = vrcGroup.MemberCount,
            OnlineCount     = vrcGroup.OnlineCount,
            IsActive        = isFirst,   // first group added is auto-selected
            SortOrder       = 0,
            AddedAt         = DateTime.UtcNow,
            LastRefreshedAt = DateTime.UtcNow,
        };

        db.ManagedGroups.Add(entity);
        await db.SaveChangesAsync(ct);

        _log.LogInformation("Added managed group {GroupId} ({Name})", vrcGroup.Id, vrcGroup.Name);
        return ToDto(entity);
    }

    public async Task<ManagedGroupDto> RefreshGroupAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = await db.ManagedGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException($"Group '{groupId}' is not in the managed set.");

        var vrcGroup = await _vrc.GetGroupAsync(groupId, ct)
            ?? throw new KeyNotFoundException($"Group '{groupId}' was not found in VRChat.");

        entity.Name            = vrcGroup.Name;
        entity.ShortCode       = vrcGroup.ShortCode;
        entity.Description     = vrcGroup.Description;
        entity.IconUrl         = vrcGroup.IconUrl;
        entity.BannerUrl       = vrcGroup.BannerUrl;
        entity.MemberCount     = vrcGroup.MemberCount;
        entity.OnlineCount     = vrcGroup.OnlineCount;
        entity.LastRefreshedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        _log.LogInformation("Refreshed managed group {GroupId}", groupId);
        return ToDto(entity);
    }

    public async Task<bool> RemoveGroupAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = await db.ManagedGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId, ct);
        if (entity is null) return false;

        var wasActive = entity.IsActive;
        db.ManagedGroups.Remove(entity);
        await db.SaveChangesAsync(ct);

        // Promote the first remaining group to active if we removed the active one
        if (wasActive)
        {
            var next = await db.ManagedGroups
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.AddedAt)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsActive = true;
                await db.SaveChangesAsync(ct);
            }
        }

        return true;
    }

    public async Task SetActiveGroupAsync(string groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var all = await db.ManagedGroups.ToListAsync(ct);
        foreach (var g in all)
            g.IsActive = g.GroupId == groupId;
        await db.SaveChangesAsync(ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static ManagedGroupDto ToDto(ManagedGroup e) => new(
        e.GroupId, e.Name, e.ShortCode, e.Description,
        e.IconUrl, e.BannerUrl, e.IsActive,
        e.MemberCount, e.OnlineCount, e.AddedAt, e.LastRefreshedAt);
}
