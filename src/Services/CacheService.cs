using System.Text.Json;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;

namespace VRCGroupTools.Services;

public interface ICacheService
{
    Task InitializeAsync();
    
    // Generic cache operations
    Task SaveAsync<T>(string key, T data);
    Task<T?> LoadAsync<T>(string key);
    Task<bool> ExistsAsync(string key);
    Task DeleteAsync(string key);
    Task ClearAllAsync();
    
    // Secure storage for credentials
    Task SaveSecureAsync(string key, string data);
    Task<string?> LoadSecureAsync(string key);
    Task ClearSecureAsync();
    
    // Audit log specific
    Task SaveAuditLogsAsync(string groupId, List<AuditLogEntry> logs);
    Task<List<AuditLogEntry>> LoadAuditLogsAsync(string groupId);
    Task<List<AuditLogEntry>> SearchAuditLogsAsync(string groupId, string? searchQuery = null, string? eventType = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<int> AppendAuditLogsAsync(string groupId, List<AuditLogEntry> newLogs);
    Task<DateTime?> GetLastAuditLogTimestampAsync(string groupId);
    Task<int> GetAuditLogCountAsync(string groupId);
    
    // Group Members
    Task SaveGroupMembersAsync(string groupId, List<GroupMemberInfo> members);
    Task<List<GroupMemberInfo>> LoadGroupMembersAsync(string groupId);
    
    // Users
    Task SaveUserAsync(UserInfo user);
    Task<UserInfo?> LoadUserAsync(string userId);
}

/// <summary>
/// Simple model for audit log entries used throughout the app
/// </summary>
public class AuditLogEntry
{
    public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? EventColor { get; set; }
    public string? RawData { get; set; }
}

/// <summary>
/// Simple model for group members
/// </summary>
public class GroupMemberInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? RoleId { get; set; }
    public string? RoleName { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool HasBadge { get; set; }
}

/// <summary>
/// Simple model for user info
/// </summary>
public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Status { get; set; }
    public string? StatusDescription { get; set; }
    public string? Bio { get; set; }
    public bool IsPlus { get; set; }
    public string? RawData { get; set; }
}

/// <summary>
/// Cached credentials for Remember Me
/// </summary>
public class CachedCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Cached session for auto-login
/// </summary>
public class CachedSession
{
    public string AuthCookie { get; set; } = string.Empty;
    public string? TwoFactorAuth { get; set; }
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class CacheService : ICacheService
{
    private readonly IDatabaseService _db;

    public CacheService(IDatabaseService databaseService)
    {
        _db = databaseService;
        Console.WriteLine("[CACHE] CacheService initialized with DatabaseService (SQLite)");
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
    }

    #region Generic Cache Operations

    public async Task SaveAsync<T>(string key, T data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await _db.SaveSettingAsync(key, json);
        Console.WriteLine($"[CACHE] Saved: {key}");
    }

    public async Task<T?> LoadAsync<T>(string key)
    {
        var json = await _db.GetSettingAsync(key);
        if (string.IsNullOrEmpty(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CACHE] Error loading {key}: {ex.Message}");
            return default;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var value = await _db.GetSettingAsync(key);
        return !string.IsNullOrEmpty(value);
    }

    public async Task DeleteAsync(string key)
    {
        // Save empty value to effectively delete
        await _db.SaveSettingAsync(key, string.Empty);
        Console.WriteLine($"[CACHE] Deleted: {key}");
    }

    public async Task ClearAllAsync()
    {
        Console.WriteLine("[CACHE] Clear all called - clearing secure storage");
        await _db.ClearAllSecureAsync();
    }

    #endregion

    #region Secure Storage

    public async Task SaveSecureAsync(string key, string data)
    {
        await _db.SaveSecureAsync(key, data);
        Console.WriteLine($"[CACHE] Saved secure: {key}");
    }

    public async Task<string?> LoadSecureAsync(string key)
    {
        var result = await _db.GetSecureAsync(key);
        Console.WriteLine($"[CACHE] Loaded secure {key}: {(result != null ? "found" : "not found")}");
        return result;
    }

    public async Task ClearSecureAsync()
    {
        await _db.ClearAllSecureAsync();
        Console.WriteLine("[CACHE] Cleared all secure data");
    }

    #endregion

    #region Audit Logs

    public async Task SaveAuditLogsAsync(string groupId, List<AuditLogEntry> logs)
    {
        var entities = logs.Select(l => new AuditLogEntity
        {
            AuditLogId = l.Id,
            GroupId = groupId,
            EventType = l.EventType,
            Description = l.Description,
            ActorId = l.ActorId,
            ActorName = l.ActorName,
            TargetId = l.TargetId,
            TargetName = l.TargetName,
            CreatedAt = l.CreatedAt,
            RawData = l.RawData
        });

        await _db.SaveAuditLogsAsync(entities);
    }

    public async Task<List<AuditLogEntry>> LoadAuditLogsAsync(string groupId)
    {
        var entities = await _db.GetAuditLogsAsync(groupId);
        return entities.Select(EntityToEntry).ToList();
    }

    public async Task<List<AuditLogEntry>> SearchAuditLogsAsync(
        string groupId,
        string? searchQuery = null,
        string? eventType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var entities = await _db.SearchAuditLogsAsync(groupId, searchQuery, eventType, fromDate, toDate);
        return entities.Select(EntityToEntry).ToList();
    }

    public async Task<int> AppendAuditLogsAsync(string groupId, List<AuditLogEntry> newLogs)
    {
        if (newLogs.Count == 0) return 0;

        var entities = newLogs.Select(l => new AuditLogEntity
        {
            AuditLogId = l.Id,
            GroupId = groupId,
            EventType = l.EventType,
            Description = l.Description,
            ActorId = l.ActorId,
            ActorName = l.ActorName,
            TargetId = l.TargetId,
            TargetName = l.TargetName,
            CreatedAt = l.CreatedAt,
            RawData = l.RawData
        });

        return await _db.SaveAuditLogsAsync(entities);
    }

    public async Task<DateTime?> GetLastAuditLogTimestampAsync(string groupId)
    {
        return await _db.GetLastAuditLogTimestampAsync(groupId);
    }

    public async Task<int> GetAuditLogCountAsync(string groupId)
    {
        return await _db.GetAuditLogCountAsync(groupId);
    }

    private static AuditLogEntry EntityToEntry(AuditLogEntity entity)
    {
        return new AuditLogEntry
        {
            Id = entity.AuditLogId,
            EventType = entity.EventType,
            Description = entity.Description,
            ActorId = entity.ActorId,
            ActorName = entity.ActorName,
            TargetId = entity.TargetId,
            TargetName = entity.TargetName,
            CreatedAt = entity.CreatedAt,
            EventColor = GetEventColor(entity.EventType),
            RawData = entity.RawData
        };
    }

    private static string GetEventColor(string eventType)
    {
        return eventType.ToLower() switch
        {
            var t when t.Contains("join") => "#4CAF50",
            var t when t.Contains("leave") => "#FF9800",
            var t when t.Contains("kick") => "#f44336",
            var t when t.Contains("ban") => "#B71C1C",
            var t when t.Contains("unban") => "#81C784",
            var t when t.Contains("role") => "#2196F3",
            var t when t.Contains("announcement") => "#9C27B0",
            var t when t.Contains("invite") => "#00BCD4",
            _ => "#607D8B"
        };
    }

    #endregion

    #region Group Members

    public async Task SaveGroupMembersAsync(string groupId, List<GroupMemberInfo> members)
    {
        var entities = members.Select(m => new GroupMemberEntity
        {
            GroupId = groupId,
            UserId = m.UserId,
            DisplayName = m.DisplayName,
            ThumbnailUrl = m.ThumbnailUrl,
            RoleId = m.RoleId,
            RoleName = m.RoleName,
            JoinedAt = m.JoinedAt,
            HasBadge = m.HasBadge
        });

        await _db.SaveGroupMembersAsync(entities);
        Console.WriteLine($"[CACHE] Saved {members.Count} group members for {groupId}");
    }

    public async Task<List<GroupMemberInfo>> LoadGroupMembersAsync(string groupId)
    {
        var entities = await _db.GetGroupMembersAsync(groupId);
        return entities.Select(e => new GroupMemberInfo
        {
            UserId = e.UserId,
            DisplayName = e.DisplayName,
            ThumbnailUrl = e.ThumbnailUrl,
            RoleId = e.RoleId,
            RoleName = e.RoleName,
            JoinedAt = e.JoinedAt,
            HasBadge = e.HasBadge
        }).ToList();
    }

    #endregion

    #region Users

    public async Task SaveUserAsync(UserInfo user)
    {
        var entity = new UserEntity
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName,
            ThumbnailUrl = user.ThumbnailUrl,
            Status = user.Status,
            StatusDescription = user.StatusDescription,
            Bio = user.Bio,
            IsPlus = user.IsPlus,
            RawData = user.RawData
        };

        await _db.SaveUserAsync(entity);
        Console.WriteLine($"[CACHE] Saved user: {user.DisplayName}");
    }

    public async Task<UserInfo?> LoadUserAsync(string userId)
    {
        var entity = await _db.GetUserAsync(userId);
        if (entity == null)
            return null;

        return new UserInfo
        {
            UserId = entity.UserId,
            DisplayName = entity.DisplayName,
            ThumbnailUrl = entity.ThumbnailUrl,
            Status = entity.Status,
            StatusDescription = entity.StatusDescription,
            Bio = entity.Bio,
            IsPlus = entity.IsPlus,
            RawData = entity.RawData
        };
    }

    #endregion
}
