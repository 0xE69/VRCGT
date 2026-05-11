// VRCGT.Core — Group management DTOs and service interface.
// No ASP.NET Core, EF Core, or infrastructure references here.

namespace VRCGT.Core;

/// <summary>
/// Cached snapshot of a VRChat group managed by VRCGT.
/// Returned by IGroupService — never contains tokens or cookies.
/// </summary>
public sealed record ManagedGroupDto(
    string    GroupId,
    string    Name,
    string?   ShortCode,
    string?   Description,
    string?   IconUrl,
    string?   BannerUrl,
    bool      IsActive,
    int       MemberCount,
    int       OnlineCount,
    DateTime  AddedAt,
    DateTime? LastRefreshedAt
);

/// <summary>
/// Manages the set of VRChat groups that VRCGT is configured to administer.
/// Implemented by VRCGT.Infrastructure.GroupService.
/// </summary>
public interface IGroupService
{
    /// <summary>Returns all managed groups ordered by IsActive desc, SortOrder, then AddedAt.</summary>
    Task<IReadOnlyList<ManagedGroupDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single managed group by VRChat group ID, or null if not tracked.</summary>
    Task<ManagedGroupDto?> GetByGroupIdAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Adds a group to the managed set.
    /// Verifies the group exists in VRChat before saving.
    /// </summary>
    /// <exception cref="InvalidOperationException">Group is already managed.</exception>
    /// <exception cref="KeyNotFoundException">Group not found in VRChat.</exception>
    Task<ManagedGroupDto> AddGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Re-fetches fresh data from the VRChat API and updates the cached snapshot.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Group is not managed, or not found in VRChat.</exception>
    Task<ManagedGroupDto> RefreshGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>Removes the group from the managed set. Returns false if not found.</summary>
    Task<bool> RemoveGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Sets one group as the active/primary group in the UI (clears IsActive on all others).
    /// </summary>
    Task SetActiveGroupAsync(string groupId, CancellationToken ct = default);
}
