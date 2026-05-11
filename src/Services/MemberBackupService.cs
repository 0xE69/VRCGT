using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;

namespace VRCGroupTools.Services;

public interface IMemberBackupService
{
    Task<BackupResult> CreateBackupAsync(string groupId, string? description = null, Action<int, int>? progressCallback = null);
    Task<List<BackupInfo>> GetBackupsAsync(string groupId);
    Task<List<MemberBackupEntity>> GetBackupMembersAsync(string backupId);
    Task<RestoreResult> RestoreMembersAsync(string backupId, bool onlyMissing = true, Action<int, int>? progressCallback = null);
    Task<bool> DeleteBackupAsync(string backupId);
    Task<BackupComparisonResult> CompareBackupWithCurrentAsync(string backupId);
}

public class BackupResult
{
    public bool Success { get; set; }
    public string? BackupId { get; set; }
    public int MemberCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RestoreResult
{
    public bool Success { get; set; }
    public int TotalMembers { get; set; }
    public int InvitesSent { get; set; }
    public int AlreadyMembers { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class BackupInfo
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int MemberCount { get; set; }
    public string? Description { get; set; }
}

public class BackupComparisonResult
{
    public int BackupMemberCount { get; set; }
    public int CurrentMemberCount { get; set; }
    public List<MemberBackupEntity> MissingMembers { get; set; } = new();
    public List<string> NewMembers { get; set; } = new();
}

public class MemberBackupService : IMemberBackupService
{
    private readonly IVRChatApiService _apiService;
    private readonly ICacheService _cacheService;

    public MemberBackupService(IVRChatApiService apiService, ICacheService cacheService)
    {
        _apiService = apiService;
        _cacheService = cacheService;
    }

    public async Task<BackupResult> CreateBackupAsync(string groupId, string? description = null, Action<int, int>? progressCallback = null)
    {
        try
        {
            LoggingService.Info("BACKUP", $"Starting member backup for group: {groupId}");

            var backupId = Guid.NewGuid().ToString();
            var members = await _apiService.GetGroupMembersAsync(groupId, progressCallback);

            if (members == null || members.Count == 0)
            {
                LoggingService.Warn("BACKUP", "No members found to backup");
                return new BackupResult
                {
                    Success = false,
                    ErrorMessage = "No members found in the group"
                };
            }

            LoggingService.Info("BACKUP", $"Retrieved {members.Count} members from API");

            // Get role information
            var roles = await _apiService.GetGroupRolesAsync(groupId);
            var roleDict = roles?.ToDictionary(r => r.RoleId, r => r.Name) ?? new Dictionary<string, string>();

            using var context = new AppDbContext();
            var backupEntities = new List<MemberBackupEntity>();

            foreach (var member in members)
            {
                var roleNames = member.RoleIds
                    .Select(roleId => roleDict.TryGetValue(roleId, out var name) ? name : roleId)
                    .ToList();

                DateTime? joinedAt = null;
                if (!string.IsNullOrWhiteSpace(member.JoinedAt) && DateTime.TryParse(member.JoinedAt, out var parsedJoinedAt))
                {
                    joinedAt = parsedJoinedAt;
                }

                var backup = new MemberBackupEntity
                {
                    BackupId = backupId,
                    GroupId = groupId,
                    UserId = member.UserId,
                    DisplayName = member.DisplayName,
                    ProfilePicUrl = null,
                    RoleIds = string.Join(",", member.RoleIds),
                    RoleNames = string.Join(", ", roleNames),
                    JoinedAt = joinedAt,
                    BackupCreatedAt = DateTime.UtcNow,
                    BackupDescription = description,
                    IsCurrentMember = true,
                    WasReInvited = false
                };

                backupEntities.Add(backup);
            }

            await context.MemberBackups.AddRangeAsync(backupEntities);
            await context.SaveChangesAsync();

            LoggingService.Info("BACKUP", $"✓ Backup created successfully: {backupId} ({members.Count} members)");

            return new BackupResult
            {
                Success = true,
                BackupId = backupId,
                MemberCount = members.Count
            };
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to create backup");
            return new BackupResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<List<BackupInfo>> GetBackupsAsync(string groupId)
    {
        try
        {
            using var context = new AppDbContext();

            var backups = await context.MemberBackups
                .Where(b => b.GroupId == groupId)
                .GroupBy(b => b.BackupId)
                .Select(g => new BackupInfo
                {
                    BackupId = g.Key,
                    CreatedAt = g.First().BackupCreatedAt,
                    MemberCount = g.Count(),
                    Description = g.First().BackupDescription
                })
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return backups;
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to get backups");
            return new List<BackupInfo>();
        }
    }

    public async Task<List<MemberBackupEntity>> GetBackupMembersAsync(string backupId)
    {
        try
        {
            using var context = new AppDbContext();

            var members = await context.MemberBackups
                .Where(b => b.BackupId == backupId)
                .OrderBy(b => b.DisplayName)
                .ToListAsync();

            return members;
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to get backup members");
            return new List<MemberBackupEntity>();
        }
    }

    public async Task<RestoreResult> RestoreMembersAsync(string backupId, bool onlyMissing = true, Action<int, int>? progressCallback = null)
    {
        var result = new RestoreResult { Success = true };

        try
        {
            LoggingService.Info("BACKUP", $"Starting restore from backup: {backupId}");

            using var context = new AppDbContext();
            var backupMembers = await GetBackupMembersAsync(backupId);

            if (backupMembers.Count == 0)
            {
                result.Success = false;
                result.Errors.Add("Backup not found or empty");
                return result;
            }

            var groupId = backupMembers.First().GroupId;
            result.TotalMembers = backupMembers.Count;

            // Get current members if we're only restoring missing ones
            HashSet<string>? currentMemberIds = null;
            if (onlyMissing)
            {
                LoggingService.Info("BACKUP", "Fetching current members to compare...");
                var currentMembers = await _apiService.GetGroupMembersAsync(groupId);
                currentMemberIds = currentMembers?.Select(m => m.UserId).ToHashSet() ?? new HashSet<string>();
                LoggingService.Info("BACKUP", $"Current group has {currentMemberIds.Count} members");
            }

            int processed = 0;
            foreach (var member in backupMembers)
            {
                processed++;
                progressCallback?.Invoke(processed, result.TotalMembers);

                // Skip if member is already in the group
                if (currentMemberIds != null && currentMemberIds.Contains(member.UserId))
                {
                    result.AlreadyMembers++;
                    LoggingService.Debug("BACKUP", $"Skipping {member.DisplayName} - already a member");
                    continue;
                }

                try
                {
                    LoggingService.Info("BACKUP", $"Sending invite to {member.DisplayName} ({member.UserId})");
                    var success = await _apiService.SendGroupInviteAsync(groupId, member.UserId);

                    if (success)
                    {
                        result.InvitesSent++;

                        // Mark as re-invited in database
                        var dbMember = await context.MemberBackups
                            .FirstOrDefaultAsync(m => m.BackupId == backupId && m.UserId == member.UserId);
                        if (dbMember != null)
                        {
                            dbMember.WasReInvited = true;
                            dbMember.ReInvitedAt = DateTime.UtcNow;
                        }

                        LoggingService.Info("BACKUP", $"✓ Invited {member.DisplayName}");
                    }
                    else
                    {
                        result.Failed++;
                        result.Errors.Add($"Failed to invite {member.DisplayName}");
                        LoggingService.Warn("BACKUP", $"✗ Failed to invite {member.DisplayName}");
                    }

                    // Rate limiting - be gentle with the API
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{member.DisplayName}: {ex.Message}");
                    LoggingService.Error("BACKUP", ex, $"Error inviting {member.DisplayName}");
                }
            }

            await context.SaveChangesAsync();

            LoggingService.Info("BACKUP", $"Restore complete: {result.InvitesSent} sent, {result.AlreadyMembers} already members, {result.Failed} failed");
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to restore members");
            result.Success = false;
            result.Errors.Add($"Restore failed: {ex.Message}");
        }

        return result;
    }

    public async Task<bool> DeleteBackupAsync(string backupId)
    {
        try
        {
            using var context = new AppDbContext();

            var backupMembers = await context.MemberBackups
                .Where(b => b.BackupId == backupId)
                .ToListAsync();

            if (backupMembers.Count == 0)
            {
                return false;
            }

            context.MemberBackups.RemoveRange(backupMembers);
            await context.SaveChangesAsync();

            LoggingService.Info("BACKUP", $"Deleted backup: {backupId} ({backupMembers.Count} members)");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to delete backup");
            return false;
        }
    }

    public async Task<BackupComparisonResult> CompareBackupWithCurrentAsync(string backupId)
    {
        var result = new BackupComparisonResult();

        try
        {
            var backupMembers = await GetBackupMembersAsync(backupId);
            if (backupMembers.Count == 0)
            {
                return result;
            }

            result.BackupMemberCount = backupMembers.Count;
            var groupId = backupMembers.First().GroupId;

            var currentMembers = await _apiService.GetGroupMembersAsync(groupId);
            if (currentMembers != null)
            {
                result.CurrentMemberCount = currentMembers.Count;

                var backupUserIds = backupMembers.Select(m => m.UserId).ToHashSet();
                var currentUserIds = currentMembers.Select(m => m.UserId).ToHashSet();

                // Find members in backup but not in current group
                result.MissingMembers = backupMembers
                    .Where(m => !currentUserIds.Contains(m.UserId))
                    .ToList();

                // Find members in current group but not in backup
                result.NewMembers = currentMembers
                    .Where(m => !backupUserIds.Contains(m.UserId))
                    .Select(m => m.DisplayName)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("BACKUP", ex, "Failed to compare backup with current");
        }

        return result;
    }
}
