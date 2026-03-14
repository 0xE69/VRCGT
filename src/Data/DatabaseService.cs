using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VRCGroupTools.Data.Models;

namespace VRCGroupTools.Data;

public interface IDatabaseService
{
    Task InitializeAsync();

    // Audit Logs
    Task<List<AuditLogEntity>> GetAuditLogsAsync(string groupId, int limit = 1000, int offset = 0);
    Task<List<AuditLogEntity>> SearchAuditLogsAsync(string groupId, string? searchQuery = null, string? eventType = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<int> SaveAuditLogsAsync(IEnumerable<AuditLogEntity> logs);
    Task<DateTime?> GetLastAuditLogTimestampAsync(string groupId);
    Task<int> GetAuditLogCountAsync(string groupId);
    Task<List<AuditLogEntity>> GetUnsentDiscordLogsAsync(string groupId, int limit = 100);
    Task MarkLogAsSentToDiscordAsync(string auditLogId);
    Task MarkLogsAsSentToDiscordAsync(IEnumerable<string> auditLogIds);

    // Pending Invites (used to infer invite acceptance on join)
    Task UpsertPendingInviteAsync(string groupId, string? targetUserId, string? targetName, string inviteLogId, DateTime invitedAtUtc);
    Task<(bool Found, DateTime? InvitedAtUtc)> TryConsumePendingInviteAsync(string groupId, string? targetUserId, string? targetName);
    Task<int> CleanupExpiredPendingInvitesAsync(DateTime cutoffUtc);

    // Group Members
    Task<List<GroupMemberEntity>> GetGroupMembersAsync(string groupId);
    Task<GroupMemberEntity?> GetGroupMemberAsync(string groupId, string userId);
    Task SaveGroupMemberAsync(GroupMemberEntity member);
    Task SaveGroupMembersAsync(IEnumerable<GroupMemberEntity> members);

    // Users
    Task<UserEntity?> GetUserAsync(string userId);
    Task SaveUserAsync(UserEntity user);

    // Secure Storage (credentials, session)
    Task SaveSecureAsync(string key, string value, DateTime? expiresAt = null);
    Task<string?> GetSecureAsync(string key);
    Task DeleteSecureAsync(string key);
    Task ClearAllSecureAsync();

    // App Settings
    Task SaveSettingAsync(string key, string value);
    Task<string?> GetSettingAsync(string key);
    Task<T?> GetSettingAsync<T>(string key) where T : class;
    Task SaveSettingAsync<T>(string key, T value) where T : class;
}

public class DatabaseService : IDatabaseService
{
    private readonly string _appDataPath;
    private bool _initialized;

    public DatabaseService()
    {
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRCGroupTools"
        );
        Directory.CreateDirectory(_appDataPath);
        Console.WriteLine($"[DATABASE] Data folder: {_appDataPath}");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            using var context = new AppDbContext();
            await context.Database.EnsureCreatedAsync();

            // Add missing columns for existing databases
            await MigrateSchemaAsync(context);

            Console.WriteLine("[DATABASE] Database initialized successfully");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DATABASE] Error initializing database: {ex.Message}");
            throw;
        }
    }

    private async Task MigrateSchemaAsync(AppDbContext context)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(AuditLogs)";

            var hasDiscordSentAt = false;
            var hasInstanceId = false;
            var hasWorldName = false;

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString(1); // Column name is at index 1
                    if (columnName == "DiscordSentAt")
                        hasDiscordSentAt = true;
                    else if (columnName == "InstanceId")
                        hasInstanceId = true;
                    else if (columnName == "WorldName")
                        hasWorldName = true;
                }
            }

            Console.WriteLine($"[DATABASE] Schema check - DiscordSentAt: {hasDiscordSentAt}, InstanceId: {hasInstanceId}, WorldName: {hasWorldName}");

            // Add DiscordSentAt column if it doesn't exist
            if (!hasDiscordSentAt)
            {
                Console.WriteLine("[DATABASE] Adding DiscordSentAt column to AuditLogs table...");
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE AuditLogs ADD COLUMN DiscordSentAt TEXT NULL";
                await alterCommand.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] DiscordSentAt column added successfully");
            }

            // Add InstanceId column if it doesn't exist
            if (!hasInstanceId)
            {
                Console.WriteLine("[DATABASE] Adding InstanceId column to AuditLogs table...");
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE AuditLogs ADD COLUMN InstanceId TEXT NULL";
                await alterCommand.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] InstanceId column added successfully");
            }

            // Add WorldName column if it doesn't exist
            if (!hasWorldName)
            {
                Console.WriteLine("[DATABASE] Adding WorldName column to AuditLogs table...");
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE AuditLogs ADD COLUMN WorldName TEXT NULL";
                await alterCommand.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] WorldName column added successfully");
            }

            // Ensure MemberBackups table exists (older databases won't have it)
            using var tableCheck = connection.CreateCommand();
            tableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='MemberBackups'";
            var tableName = await tableCheck.ExecuteScalarAsync();
            if (tableName == null)
            {
                Console.WriteLine("[DATABASE] Creating MemberBackups table...");
                using var createTable = connection.CreateCommand();
                createTable.CommandText = @"
CREATE TABLE IF NOT EXISTS MemberBackups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BackupId TEXT NOT NULL,
    GroupId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    DisplayName TEXT,
    ProfilePicUrl TEXT,
    RoleIds TEXT,
    RoleNames TEXT,
    JoinedAt TEXT,
    BackupCreatedAt TEXT NOT NULL,
    BackupDescription TEXT,
    WasReInvited INTEGER NOT NULL,
    ReInvitedAt TEXT,
    IsCurrentMember INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_MemberBackups_BackupId ON MemberBackups(BackupId);
CREATE INDEX IF NOT EXISTS IX_MemberBackups_GroupId ON MemberBackups(GroupId);
CREATE INDEX IF NOT EXISTS IX_MemberBackups_BackupId_UserId ON MemberBackups(BackupId, UserId);
CREATE INDEX IF NOT EXISTS IX_MemberBackups_BackupCreatedAt ON MemberBackups(BackupCreatedAt);
CREATE INDEX IF NOT EXISTS IX_MemberBackups_WasReInvited ON MemberBackups(WasReInvited);
";
                await createTable.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] MemberBackups table created successfully");
            }

            // Ensure PendingGroupInvites table exists (used to infer invite acceptance)
            using var pendingInviteTableCheck = connection.CreateCommand();
            pendingInviteTableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PendingGroupInvites'";
            var pendingInviteTableName = await pendingInviteTableCheck.ExecuteScalarAsync();

            if (pendingInviteTableName == null)
            {
                Console.WriteLine("[DATABASE] Creating PendingGroupInvites table...");
                using var createPendingInvites = connection.CreateCommand();
                createPendingInvites.CommandText = @"
CREATE TABLE IF NOT EXISTS PendingGroupInvites (
    GroupId TEXT NOT NULL,
    TargetKey TEXT NOT NULL,
    TargetUserId TEXT,
    TargetName TEXT,
    InviteLogId TEXT NOT NULL,
    InvitedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    PRIMARY KEY (GroupId, TargetKey)
);
CREATE INDEX IF NOT EXISTS IX_PendingGroupInvites_GroupId ON PendingGroupInvites(GroupId);
CREATE INDEX IF NOT EXISTS IX_PendingGroupInvites_InvitedAtUtc ON PendingGroupInvites(InvitedAtUtc);
";
                await createPendingInvites.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] PendingGroupInvites table created successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DATABASE] CRITICAL - Schema migration error: {ex.Message}");
            Console.WriteLine($"[DATABASE] Stack trace: {ex.StackTrace}");
            // Rethrow to prevent app from continuing with incompatible schema
            throw new InvalidOperationException("Failed to migrate database schema. Please check the error log.", ex);
        }
    }

    #region Audit Logs

    public async Task<List<AuditLogEntity>> GetAuditLogsAsync(string groupId, int limit = 1000, int offset = 0)
    {
        using var context = new AppDbContext();
        return await context.AuditLogs
            .Where(l => l.GroupId == groupId)
            .OrderByDescending(l => l.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntity>> SearchAuditLogsAsync(
        string groupId,
        string? searchQuery = null,
        string? eventType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        using var context = new AppDbContext();

        var query = context.AuditLogs.Where(l => l.GroupId == groupId);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.ToLower();
            query = query.Where(l =>
                l.Description.ToLower().Contains(search) ||
                (l.ActorName != null && l.ActorName.ToLower().Contains(search)) ||
                (l.TargetName != null && l.TargetName.ToLower().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(eventType) && eventType != "All")
        {
            query = query.Where(l => l.EventType.Contains(eventType));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(l => l.CreatedAt <= endOfDay);
        }

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(10000) // Safety limit
            .ToListAsync();
    }

    public async Task<int> SaveAuditLogsAsync(IEnumerable<AuditLogEntity> logs)
    {
        // Ensure database is initialized with latest schema
        await InitializeAsync();

        using var context = new AppDbContext();
        var saved = 0;

        foreach (var log in logs)
        {
            // Check if already exists
            var exists = await context.AuditLogs
                .AnyAsync(l => l.AuditLogId == log.AuditLogId);

            if (!exists)
            {
                log.InsertedAt = DateTime.UtcNow;
                context.AuditLogs.Add(log);
                saved++;
            }
        }

        if (saved > 0)
        {
            await context.SaveChangesAsync();
            Console.WriteLine($"[DATABASE] Saved {saved} new audit logs");
        }

        return saved;
    }

    public async Task<DateTime?> GetLastAuditLogTimestampAsync(string groupId)
    {
        using var context = new AppDbContext();
        return await context.AuditLogs
            .Where(l => l.GroupId == groupId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<int> GetAuditLogCountAsync(string groupId)
    {
        using var context = new AppDbContext();
        return await context.AuditLogs.CountAsync(l => l.GroupId == groupId);
    }

    public async Task<List<AuditLogEntity>> GetUnsentDiscordLogsAsync(string groupId, int limit = 100)
    {
        using var context = new AppDbContext();
        return await context.AuditLogs
            .Where(l => l.GroupId == groupId && l.DiscordSentAt == null)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task MarkLogAsSentToDiscordAsync(string auditLogId)
    {
        using var context = new AppDbContext();
        var log = await context.AuditLogs.FirstOrDefaultAsync(l => l.AuditLogId == auditLogId);
        if (log != null)
        {
            log.DiscordSentAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }

    public async Task MarkLogsAsSentToDiscordAsync(IEnumerable<string> auditLogIds)
    {
        using var context = new AppDbContext();
        var logs = await context.AuditLogs
            .Where(l => auditLogIds.Contains(l.AuditLogId))
            .ToListAsync();

        foreach (var log in logs)
        {
            log.DiscordSentAt = DateTime.UtcNow;
        }

        if (logs.Any())
        {
            await context.SaveChangesAsync();
            Console.WriteLine($"[DATABASE] Marked {logs.Count} logs as sent to Discord");
        }
    }

    #endregion

    #region Pending Invites (Invite Accepted Inference)

    private static string ComputeTargetKey(string? targetUserId, string? targetName)
    {
        if (!string.IsNullOrWhiteSpace(targetUserId))
            return targetUserId.Trim();

        var name = (targetName ?? "").Trim().ToLowerInvariant();
        return "name:" + name;
    }

    public async Task UpsertPendingInviteAsync(string groupId, string? targetUserId, string? targetName, string inviteLogId, DateTime invitedAtUtc)
    {
        await InitializeAsync();

        using var context = new AppDbContext();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var targetKey = ComputeTargetKey(targetUserId, targetName);
        var invitedAtStr = invitedAtUtc.ToUniversalTime().ToString("o");
        var nowStr = DateTime.UtcNow.ToString("o");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PendingGroupInvites
    (GroupId, TargetKey, TargetUserId, TargetName, InviteLogId, InvitedAtUtc, UpdatedAtUtc)
VALUES
    ($groupId, $targetKey, $targetUserId, $targetName, $inviteLogId, $invitedAtUtc, $updatedAtUtc)
ON CONFLICT(GroupId, TargetKey) DO UPDATE SET
    TargetUserId = excluded.TargetUserId,
    TargetName   = excluded.TargetName,
    InviteLogId  = excluded.InviteLogId,
    InvitedAtUtc = excluded.InvitedAtUtc,
    UpdatedAtUtc = excluded.UpdatedAtUtc;
";

        var p1 = cmd.CreateParameter(); p1.ParameterName = "$groupId"; p1.Value = groupId;
        var p2 = cmd.CreateParameter(); p2.ParameterName = "$targetKey"; p2.Value = targetKey;
        var p3 = cmd.CreateParameter(); p3.ParameterName = "$targetUserId"; p3.Value = (object?)targetUserId ?? DBNull.Value;
        var p4 = cmd.CreateParameter(); p4.ParameterName = "$targetName"; p4.Value = (object?)targetName ?? DBNull.Value;
        var p5 = cmd.CreateParameter(); p5.ParameterName = "$inviteLogId"; p5.Value = inviteLogId;
        var p6 = cmd.CreateParameter(); p6.ParameterName = "$invitedAtUtc"; p6.Value = invitedAtStr;
        var p7 = cmd.CreateParameter(); p7.ParameterName = "$updatedAtUtc"; p7.Value = nowStr;

        cmd.Parameters.Add(p1);
        cmd.Parameters.Add(p2);
        cmd.Parameters.Add(p3);
        cmd.Parameters.Add(p4);
        cmd.Parameters.Add(p5);
        cmd.Parameters.Add(p6);
        cmd.Parameters.Add(p7);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(bool Found, DateTime? InvitedAtUtc)> TryConsumePendingInviteAsync(string groupId, string? targetUserId, string? targetName)
    {
        await InitializeAsync();

        using var context = new AppDbContext();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Try userId key first, then name key fallback
        var candidateKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(targetUserId))
            candidateKeys.Add(ComputeTargetKey(targetUserId, null));
        if (!string.IsNullOrWhiteSpace(targetName))
            candidateKeys.Add(ComputeTargetKey(null, targetName));

        if (candidateKeys.Count == 0)
            return (false, null);

        using var tx = connection.BeginTransaction();

        foreach (var key in candidateKeys.Distinct())
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText = @"
SELECT InvitedAtUtc
FROM PendingGroupInvites
WHERE GroupId = $groupId AND TargetKey = $targetKey
LIMIT 1;
";
            var s1 = selectCmd.CreateParameter(); s1.ParameterName = "$groupId"; s1.Value = groupId;
            var s2 = selectCmd.CreateParameter(); s2.ParameterName = "$targetKey"; s2.Value = key;
            selectCmd.Parameters.Add(s1);
            selectCmd.Parameters.Add(s2);

            var invitedStrObj = await selectCmd.ExecuteScalarAsync();
            if (invitedStrObj != null && invitedStrObj != DBNull.Value)
            {
                var invitedStr = invitedStrObj.ToString() ?? "";
                DateTime invitedUtc;
                if (DateTimeOffset.TryParse(invitedStr, out var dto))
                    invitedUtc = dto.UtcDateTime;
                else
                    invitedUtc = DateTime.UtcNow;

                using var deleteCmd = connection.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = @"
DELETE FROM PendingGroupInvites
WHERE GroupId = $groupId AND TargetKey = $targetKey;
";
                var d1 = deleteCmd.CreateParameter(); d1.ParameterName = "$groupId"; d1.Value = groupId;
                var d2 = deleteCmd.CreateParameter(); d2.ParameterName = "$targetKey"; d2.Value = key;
                deleteCmd.Parameters.Add(d1);
                deleteCmd.Parameters.Add(d2);

                await deleteCmd.ExecuteNonQueryAsync();

                tx.Commit();
                return (true, invitedUtc);
            }
        }

        tx.Commit();
        return (false, null);
    }

    public async Task<int> CleanupExpiredPendingInvitesAsync(DateTime cutoffUtc)
    {
        await InitializeAsync();

        using var context = new AppDbContext();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var cutoffStr = cutoffUtc.ToUniversalTime().ToString("o");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
DELETE FROM PendingGroupInvites
WHERE InvitedAtUtc < $cutoffUtc;
";
        var p = cmd.CreateParameter();
        p.ParameterName = "$cutoffUtc";
        p.Value = cutoffStr;
        cmd.Parameters.Add(p);

        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected > 0)
            Console.WriteLine($"[DATABASE] Cleaned up {affected} expired PendingGroupInvites");

        return affected;
    }

    #endregion

    #region Group Members

    public async Task<List<GroupMemberEntity>> GetGroupMembersAsync(string groupId)
    {
        using var context = new AppDbContext();
        return await context.GroupMembers
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    public async Task<GroupMemberEntity?> GetGroupMemberAsync(string groupId, string userId)
    {
        using var context = new AppDbContext();
        return await context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);
    }

    public async Task SaveGroupMemberAsync(GroupMemberEntity member)
    {
        using var context = new AppDbContext();

        var existing = await context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == member.GroupId && m.UserId == member.UserId);

        if (existing != null)
        {
            // Update
            existing.DisplayName = member.DisplayName;
            existing.ThumbnailUrl = member.ThumbnailUrl;
            existing.RoleId = member.RoleId;
            existing.RoleName = member.RoleName;
            existing.HasBadge = member.HasBadge;
            existing.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            member.LastUpdated = DateTime.UtcNow;
            context.GroupMembers.Add(member);
        }

        await context.SaveChangesAsync();
    }

    public async Task SaveGroupMembersAsync(IEnumerable<GroupMemberEntity> members)
    {
        using var context = new AppDbContext();

        foreach (var member in members)
        {
            var existing = await context.GroupMembers
                .FirstOrDefaultAsync(m => m.GroupId == member.GroupId && m.UserId == member.UserId);

            if (existing != null)
            {
                existing.DisplayName = member.DisplayName;
                existing.ThumbnailUrl = member.ThumbnailUrl;
                existing.RoleId = member.RoleId;
                existing.RoleName = member.RoleName;
                existing.HasBadge = member.HasBadge;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                member.LastUpdated = DateTime.UtcNow;
                context.GroupMembers.Add(member);
            }
        }

        await context.SaveChangesAsync();
    }

    #endregion

    #region Users

    public async Task<UserEntity?> GetUserAsync(string userId)
    {
        using var context = new AppDbContext();
        return await context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task SaveUserAsync(UserEntity user)
    {
        using var context = new AppDbContext();

        var existing = await context.Users.FirstOrDefaultAsync(u => u.UserId == user.UserId);

        if (existing != null)
        {
            existing.DisplayName = user.DisplayName;
            existing.ThumbnailUrl = user.ThumbnailUrl;
            existing.Status = user.Status;
            existing.StatusDescription = user.StatusDescription;
            existing.Bio = user.Bio;
            existing.IsPlus = user.IsPlus;
            existing.RawData = user.RawData;
            existing.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            user.LastUpdated = DateTime.UtcNow;
            context.Users.Add(user);
        }

        await context.SaveChangesAsync();
    }

    #endregion

    #region Secure Storage

    public async Task SaveSecureAsync(string key, string value, DateTime? expiresAt = null)
    {
        using var context = new AppDbContext();

        // Encrypt using DPAPI
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

        var existing = await context.CachedSessions.FirstOrDefaultAsync(s => s.Key == key);

        if (existing != null)
        {
            existing.EncryptedValue = encryptedBytes;
            existing.ExpiresAt = expiresAt;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            context.CachedSessions.Add(new CachedSessionEntity
            {
                Key = key,
                EncryptedValue = encryptedBytes,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        Console.WriteLine($"[DATABASE] Saved secure data for key: {key}");
    }

    public async Task<string?> GetSecureAsync(string key)
    {
        using var context = new AppDbContext();

        var session = await context.CachedSessions.FirstOrDefaultAsync(s => s.Key == key);

        if (session == null)
            return null;

        // Check expiration
        if (session.ExpiresAt.HasValue && session.ExpiresAt.Value < DateTime.UtcNow)
        {
            context.CachedSessions.Remove(session);
            await context.SaveChangesAsync();
            return null;
        }

        try
        {
            var decryptedBytes = ProtectedData.Unprotect(session.EncryptedValue, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DATABASE] Error decrypting secure data: {ex.Message}");
            return null;
        }
    }

    public async Task DeleteSecureAsync(string key)
    {
        using var context = new AppDbContext();

        var session = await context.CachedSessions.FirstOrDefaultAsync(s => s.Key == key);
        if (session != null)
        {
            context.CachedSessions.Remove(session);
            await context.SaveChangesAsync();
        }
    }

    public async Task ClearAllSecureAsync()
    {
        using var context = new AppDbContext();
        context.CachedSessions.RemoveRange(context.CachedSessions);
        await context.SaveChangesAsync();
        Console.WriteLine("[DATABASE] Cleared all secure data");
    }

    #endregion

    #region App Settings

    public async Task SaveSettingAsync(string key, string value)
    {
        using var context = new AppDbContext();

        var existing = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);

        if (existing != null)
        {
            existing.Value = value;
            existing.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            context.AppSettings.Add(new AppSettingEntity
            {
                Key = key,
                Value = value,
                LastUpdated = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        using var context = new AppDbContext();
        var setting = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task<T?> GetSettingAsync<T>(string key) where T : class
    {
        var value = await GetSettingAsync(key);
        if (string.IsNullOrEmpty(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveSettingAsync<T>(string key, T value) where T : class
    {
        var json = JsonSerializer.Serialize(value);
        await SaveSettingAsync(key, json);
    }

    #endregion
}
