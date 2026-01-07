using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            Console.WriteLine("[DATABASE] Database initialized successfully");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DATABASE] Error initializing database: {ex.Message}");
            throw;
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
