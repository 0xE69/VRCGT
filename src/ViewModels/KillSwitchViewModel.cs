using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

/// <summary>
/// Display model for a member with their assigned roles
/// </summary>
public partial class MemberWithRoles : ObservableObject
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ObservableCollection<GroupRoleDisplay> Roles { get; set; } = new();
    
    public string RoleSummary => Roles.Count > 0 
        ? string.Join(", ", Roles.Select(r => r.Name)) 
        : "No roles";
        
    [ObservableProperty]
    private bool _isSelected = true;
}

public partial class RoleSelectionItem : ObservableObject
{
    public string RoleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;
}

/// <summary>
/// Display model for a role snapshot (for restoration)
/// </summary>
public partial class SnapshotDisplay : ObservableObject
{
    public string SnapshotId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TotalUsers { get; set; }
    public int TotalRoles { get; set; }
    public int RestoredCount { get; set; }
    
    public string DisplayText => $"{CreatedAt:yyyy-MM-dd HH:mm:ss} - {TotalUsers} users, {TotalRoles} roles ({RestoredCount} restored)";
}

/// <summary>
/// Display model for a user's roles in a snapshot
/// </summary>
public partial class SnapshotUserRoles : ObservableObject
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ObservableCollection<SnapshotRoleItem> Roles { get; set; } = new();
    
    [ObservableProperty]
    private bool _isSelected = true;
}

public partial class SnapshotRoleItem : ObservableObject
{
    public int EntityId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsRestored { get; set; }
    
    [ObservableProperty]
    private bool _isSelected = true;
}

public partial class KillSwitchViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;
    private readonly ICacheService _cacheService;

    [ObservableProperty] private ObservableCollection<MemberWithRoles> _membersWithRoles = new();
    [ObservableProperty] private ObservableCollection<GroupRoleDisplay> _allRoles = new();
    [ObservableProperty] private ObservableCollection<RoleSelectionItem> _roleSelections = new();
    [ObservableProperty] private ObservableCollection<SnapshotDisplay> _snapshots = new();
    [ObservableProperty] private ObservableCollection<SnapshotUserRoles> _snapshotUsers = new();
    
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRemoving;
    [ObservableProperty] private bool _isRestoring;
    
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressText = string.Empty;
    
    [ObservableProperty] private bool _showConfirmPanel;
    [ObservableProperty] private bool _showRestorePanel;
    [ObservableProperty] private SnapshotDisplay? _selectedSnapshot;
    
    [ObservableProperty] private int _totalMembersWithRoles;
    [ObservableProperty] private int _totalRoleAssignments;
    [ObservableProperty] private int _selectedMembersCount;
    [ObservableProperty] private int _selectedRoleAssignmentsToRemove;
    [ObservableProperty] private int _selectedRolesCount;

    public KillSwitchViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
    }

    public IEnumerable<MemberWithRoles> SelectedMembers => MembersWithRoles.Where(m => m.IsSelected);

    [RelayCommand]
    private async Task LoadMembersFromCacheAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading cached roles and members...";
        MembersWithRoles.Clear();
        AllRoles.Clear();
        RoleSelections.Clear();

        var cachedRoles = await _cacheService.LoadAsync<List<GroupRole>>($"group_roles_{groupId}") ?? new List<GroupRole>();
        foreach (var role in cachedRoles)
        {
            var roleName = role.Name ?? "Unknown";
            AllRoles.Add(new GroupRoleDisplay { RoleId = role.RoleId, Name = roleName });
            var roleSelection = new RoleSelectionItem { RoleId = role.RoleId, Name = roleName, IsSelected = true };
            roleSelection.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RoleSelectionItem.IsSelected))
                {
                    UpdateSelectionStats();
                }
            };
            RoleSelections.Add(roleSelection);
        }

        var cachedMembers = await _cacheService.LoadAsync<List<GroupMember>>($"group_members_{groupId}");
        if (cachedMembers == null || cachedMembers.Count == 0)
        {
            Status = "No cached members found. Use Refresh to fetch from API.";
            IsBusy = false;
            return;
        }

        int totalAssignments = 0;
        foreach (var member in cachedMembers)
        {
            if (member.RoleIds == null || member.RoleIds.Count == 0)
                continue;

            var memberRoles = new ObservableCollection<GroupRoleDisplay>();
            foreach (var roleId in member.RoleIds)
            {
                var role = AllRoles.FirstOrDefault(r => r.RoleId == roleId);
                if (role != null)
                {
                    if (role.Name?.Equals("Member", StringComparison.OrdinalIgnoreCase) == true)
                        continue;
                    memberRoles.Add(new GroupRoleDisplay { RoleId = role.RoleId, Name = role.Name ?? "Unknown" });
                }
            }

            if (memberRoles.Count > 0)
            {
                var newMember = new MemberWithRoles
                {
                    UserId = member.UserId ?? string.Empty,
                    DisplayName = member.DisplayName ?? "Unknown",
                    Roles = memberRoles
                };
                newMember.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MemberWithRoles.IsSelected))
                    {
                        UpdateSelectionStats();
                    }
                };
                MembersWithRoles.Add(newMember);
                totalAssignments += memberRoles.Count;
            }
        }

        TotalMembersWithRoles = MembersWithRoles.Count;
        TotalRoleAssignments = totalAssignments;
        UpdateSelectionStats();
        Status = MembersWithRoles.Count == 0
            ? "No members with special roles found in cache."
            : $"Loaded {MembersWithRoles.Count} members with {totalAssignments} role assignments from cache.";

        IsBusy = false;
    }

    [RelayCommand]
    private async Task LoadMembersWithRolesAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading group roles...";
        MembersWithRoles.Clear();
        AllRoles.Clear();
        RoleSelections.Clear();

        try
        {
            // Load all roles for the group
            var roles = await _apiService.GetGroupRolesAsync(groupId);
            foreach (var role in roles)
            {
                var roleName = role.Name ?? "Unknown";
                AllRoles.Add(new GroupRoleDisplay { RoleId = role.RoleId, Name = roleName });
                var roleSelection = new RoleSelectionItem { RoleId = role.RoleId, Name = roleName, IsSelected = true };
                roleSelection.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RoleSelectionItem.IsSelected))
                    {
                        UpdateSelectionStats();
                    }
                };
                RoleSelections.Add(roleSelection);
            }

            await _cacheService.SaveAsync($"group_roles_{groupId}", roles);
            
            Status = "Loading members...";
            
            // Load all members
            var members = await _apiService.GetGroupMembersAsync(groupId, (count, _) =>
            {
                Status = $"Loaded {count} members...";
            });

            await _cacheService.SaveAsync($"group_members_{groupId}", members);

            // Filter to only members with roles (excluding the default "member" role if it's the only one)
            int totalAssignments = 0;
            foreach (var member in members)
            {
                if (member.RoleIds == null || member.RoleIds.Count == 0)
                    continue;

                // Get role details for this member
                var memberRoles = new ObservableCollection<GroupRoleDisplay>();
                foreach (var roleId in member.RoleIds)
                {
                    var role = AllRoles.FirstOrDefault(r => r.RoleId == roleId);
                    if (role != null)
                    {
                        // Skip the default member role (usually the last one or named "Member")
                        if (role.Name?.Equals("Member", StringComparison.OrdinalIgnoreCase) == true)
                            continue;
                            
                        memberRoles.Add(new GroupRoleDisplay { RoleId = role.RoleId, Name = role.Name ?? "Unknown" });
                    }
                }

                if (memberRoles.Count > 0)
                {
                    MembersWithRoles.Add(new MemberWithRoles
                    {
                        UserId = member.UserId ?? string.Empty,
                        DisplayName = member.DisplayName ?? "Unknown",
                        Roles = memberRoles
                    });
                    MembersWithRoles.Last().PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(MemberWithRoles.IsSelected))
                        {
                            UpdateSelectionStats();
                        }
                    };
                    totalAssignments += memberRoles.Count;
                }
            }

            TotalMembersWithRoles = MembersWithRoles.Count;
            TotalRoleAssignments = totalAssignments;
            UpdateSelectionStats();
            Status = MembersWithRoles.Count == 0 
                ? "No members with special roles found." 
                : $"Found {MembersWithRoles.Count} members with {totalAssignments} role assignments.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadSnapshotsAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        Snapshots.Clear();
        
        try
        {
            using var db = new AppDbContext();
            
            // Get distinct snapshots for this group
            var snapshotGroups = await db.RoleSnapshots
                .Where(s => s.GroupId == groupId)
                .GroupBy(s => s.SnapshotId)
                .Select(g => new
                {
                    SnapshotId = g.Key,
                    CreatedAt = g.Min(s => s.CreatedAt),
                    TotalUsers = g.Select(s => s.UserId).Distinct().Count(),
                    TotalRoles = g.Count(),
                    RestoredCount = g.Count(s => s.IsRestored)
                })
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            foreach (var snapshot in snapshotGroups)
            {
                Snapshots.Add(new SnapshotDisplay
                {
                    SnapshotId = snapshot.SnapshotId,
                    CreatedAt = snapshot.CreatedAt,
                    TotalUsers = snapshot.TotalUsers,
                    TotalRoles = snapshot.TotalRoles,
                    RestoredCount = snapshot.RestoredCount
                });
            }

            Status = Snapshots.Count == 0 
                ? "No snapshots found. Use the Kill Switch to create one." 
                : $"Found {Snapshots.Count} role snapshots.";
        }
        catch (Exception ex)
        {
            Status = $"Error loading snapshots: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowConfirm()
    {
        if (MembersWithRoles.Count == 0)
        {
            Status = "No members with roles to remove. Load members first.";
            return;
        }
        
        ShowConfirmPanel = true;
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        ShowConfirmPanel = false;
    }

    [RelayCommand]
    private async Task ExecuteKillSwitchAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        var selectedMembers = SelectedMembers.ToList();
        if (selectedMembers.Count == 0)
        {
            Status = "No members selected.";
            return;
        }

        var selectedRoleIds = RoleSelections.Where(r => r.IsSelected).Select(r => r.RoleId).ToHashSet();
        var hasRoleFilter = selectedRoleIds.Count > 0;

        ShowConfirmPanel = false;
        IsRemoving = true;
        IsBusy = true;

        // Generate a unique snapshot ID
        var snapshotId = $"snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 50);
        
        try
        {
            // First, save all current role assignments to the database
            Status = "Creating role snapshot...";
            using (var db = new AppDbContext())
            {
                foreach (var member in selectedMembers)
                {
                    foreach (var role in member.Roles)
                    {
                        db.RoleSnapshots.Add(new RoleSnapshotEntity
                        {
                            SnapshotId = snapshotId,
                            GroupId = groupId,
                            UserId = member.UserId,
                            DisplayName = member.DisplayName,
                            RoleId = role.RoleId,
                            RoleName = role.Name ?? "Unknown",
                            CreatedAt = DateTime.UtcNow,
                            IsRestored = false
                        });
                    }
                }
                await db.SaveChangesAsync();
            }
            
            Status = "Snapshot saved. Removing roles...";
            
            // Now remove all roles
            int totalRoles = selectedMembers.Sum(m => hasRoleFilter
                ? m.Roles.Count(r => selectedRoleIds.Contains(r.RoleId))
                : m.Roles.Count);
            ProgressTotal = totalRoles;
            ProgressCurrent = 0;
            
            int successCount = 0;
            int failCount = 0;
            
            foreach (var member in selectedMembers)
            {
                var rolesToRemove = hasRoleFilter
                    ? member.Roles.Where(r => selectedRoleIds.Contains(r.RoleId)).ToList()
                    : member.Roles.ToList();

                foreach (var role in rolesToRemove)
                {
                    ProgressText = $"Removing {role.Name} from {member.DisplayName}...";
                    
                    var success = await _apiService.RemoveGroupRoleAsync(groupId, member.UserId, role.RoleId);
                    
                    if (success)
                    {
                        successCount++;
                        member.Roles.Remove(role);
                    }
                    else
                    {
                        failCount++;
                    }
                    
                    ProgressCurrent++;
                    Status = $"Progress: {ProgressCurrent}/{ProgressTotal} ({successCount} success, {failCount} failed)";
                    
                    // Small delay to avoid rate limiting
                    await Task.Delay(100);
                }
            }

            // Remove members with no more roles from the list
            var emptyMembers = MembersWithRoles.Where(m => m.Roles.Count == 0).ToList();
            foreach (var member in emptyMembers)
            {
                MembersWithRoles.Remove(member);
            }

            TotalMembersWithRoles = MembersWithRoles.Count;
            TotalRoleAssignments = MembersWithRoles.Sum(m => m.Roles.Count);
            UpdateSelectionStats();
            
            Status = $"Kill Switch complete! Removed {successCount} roles. {failCount} failed. Snapshot saved for restoration.";
            ProgressText = string.Empty;
            
            // Refresh snapshots list
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error during kill switch: {ex.Message}";
        }
        finally
        {
            IsRemoving = false;
            IsBusy = false;
            ProgressCurrent = 0;
            ProgressTotal = 0;
        }
    }

    [RelayCommand]
    private void SelectAllRoles()
    {
        foreach (var role in RoleSelections)
        {
            role.IsSelected = true;
        }
        UpdateSelectionStats();
    }

    [RelayCommand]
    private void DeselectAllRoles()
    {
        foreach (var role in RoleSelections)
        {
            role.IsSelected = false;
        }
        UpdateSelectionStats();
    }

    private void UpdateSelectionStats()
    {
        SelectedMembersCount = MembersWithRoles.Count(m => m.IsSelected);

        var selectedRoleIds = RoleSelections.Where(r => r.IsSelected).Select(r => r.RoleId).ToHashSet();
        SelectedRolesCount = selectedRoleIds.Count;

        var hasRoleFilter = selectedRoleIds.Count > 0;
        SelectedRoleAssignmentsToRemove = MembersWithRoles
            .Where(m => m.IsSelected)
            .Sum(m => hasRoleFilter
                ? m.Roles.Count(r => selectedRoleIds.Contains(r.RoleId))
                : m.Roles.Count);
    }

    [RelayCommand]
    private async Task OpenRestorePanelAsync(SnapshotDisplay? snapshot)
    {
        if (snapshot == null) return;
        
        SelectedSnapshot = snapshot;
        SnapshotUsers.Clear();
        
        try
        {
            using var db = new AppDbContext();
            
            var snapshotData = await db.RoleSnapshots
                .Where(s => s.SnapshotId == snapshot.SnapshotId)
                .ToListAsync();

            // Group by user
            var userGroups = snapshotData.GroupBy(s => s.UserId);
            
            foreach (var group in userGroups)
            {
                var firstEntry = group.First();
                var userRoles = new SnapshotUserRoles
                {
                    UserId = group.Key,
                    DisplayName = firstEntry.DisplayName,
                    Roles = new ObservableCollection<SnapshotRoleItem>(
                        group.Select(r => new SnapshotRoleItem
                        {
                            EntityId = r.Id,
                            RoleId = r.RoleId,
                            RoleName = r.RoleName,
                            IsRestored = r.IsRestored,
                            IsSelected = !r.IsRestored // Pre-select non-restored roles
                        }))
                };
                SnapshotUsers.Add(userRoles);
            }
            
            ShowRestorePanel = true;
        }
        catch (Exception ex)
        {
            Status = $"Error loading snapshot: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseRestorePanel()
    {
        ShowRestorePanel = false;
        SelectedSnapshot = null;
        SnapshotUsers.Clear();
    }

    [RelayCommand]
    private async Task RestoreRolesAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId) || SelectedSnapshot == null)
        {
            Status = "Invalid state for restoration.";
            return;
        }

        // Get all selected roles to restore
        var rolesToRestore = SnapshotUsers
            .Where(u => u.IsSelected)
            .SelectMany(u => u.Roles.Where(r => r.IsSelected && !r.IsRestored))
            .ToList();

        if (rolesToRestore.Count == 0)
        {
            Status = "No roles selected for restoration.";
            return;
        }

        IsRestoring = true;
        IsBusy = true;
        
        ProgressTotal = rolesToRestore.Count;
        ProgressCurrent = 0;
        
        int successCount = 0;
        int failCount = 0;

        try
        {
            using var db = new AppDbContext();
            
            foreach (var roleItem in rolesToRestore)
            {
                // Find the user for this role
                var user = SnapshotUsers.FirstOrDefault(u => u.Roles.Contains(roleItem));
                if (user == null) continue;
                
                ProgressText = $"Restoring {roleItem.RoleName} to {user.DisplayName}...";
                
                var success = await _apiService.AssignGroupRoleAsync(groupId, user.UserId, roleItem.RoleId);
                
                if (success)
                {
                    successCount++;
                    roleItem.IsRestored = true;
                    
                    // Update database
                    var entity = await db.RoleSnapshots.FindAsync(roleItem.EntityId);
                    if (entity != null)
                    {
                        entity.IsRestored = true;
                        entity.RestoredAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    failCount++;
                }
                
                ProgressCurrent++;
                Status = $"Progress: {ProgressCurrent}/{ProgressTotal} ({successCount} success, {failCount} failed)";
                
                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }
            
            await db.SaveChangesAsync();
            
            Status = $"Restoration complete! Restored {successCount} roles. {failCount} failed.";
            ProgressText = string.Empty;
            
            // Refresh the snapshot list to show updated counts
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error during restoration: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
            IsBusy = false;
            ProgressCurrent = 0;
            ProgressTotal = 0;
        }
    }

    [RelayCommand]
    private async Task DeleteSnapshotAsync(SnapshotDisplay? snapshot)
    {
        if (snapshot == null) return;
        
        try
        {
            using var db = new AppDbContext();
            
            var toDelete = await db.RoleSnapshots
                .Where(s => s.SnapshotId == snapshot.SnapshotId)
                .ToListAsync();
                
            db.RoleSnapshots.RemoveRange(toDelete);
            await db.SaveChangesAsync();
            
            Snapshots.Remove(snapshot);
            Status = $"Snapshot deleted.";
        }
        catch (Exception ex)
        {
            Status = $"Error deleting snapshot: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAllMembers()
    {
        foreach (var member in MembersWithRoles)
        {
            member.IsSelected = true;
        }
        UpdateSelectionStats();
    }

    [RelayCommand]
    private void DeselectAllMembers()
    {
        foreach (var member in MembersWithRoles)
        {
            member.IsSelected = false;
        }
        UpdateSelectionStats();
    }
}
