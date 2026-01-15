using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Models;

namespace VRCGroupTools.Services;

public interface IModerationService
{
    Task<string> LogModerationActionAsync(string groupId, ModerationActionRequest request, string actorUserId, string actorDisplayName);
    Task<InfractionHistory> GetInfractionHistoryAsync(string groupId, string userId, string? specificReason = null);
    Task<List<ModerationActionSummary>> GetUserModerationHistoryAsync(string groupId, string userId, int limit = 10);
    Task<bool> RevokeActionAsync(string actionId, string revokedByUserId, string revokeReason);
    Task<int> GetActiveWarningsCountAsync(string groupId, string userId, int daysBack = 30);
}

public class ModerationService : IModerationService
{
    private readonly IDatabaseService _databaseService;

    public ModerationService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<string> LogModerationActionAsync(string groupId, ModerationActionRequest request, string actorUserId, string actorDisplayName)
    {
        using var context = new AppDbContext();
        
        var actionId = Guid.NewGuid().ToString();
        var actionTime = DateTime.UtcNow;
        
        DateTime? expiresAt = null;
        if (request.DurationDays > 0)
        {
            expiresAt = actionTime.AddDays(request.DurationDays);
        }
        
        var entity = new ModerationActionEntity
        {
            ActionId = actionId,
            GroupId = groupId,
            ActionType = request.ActionType.ToLower(),
            TargetUserId = request.TargetUserId,
            TargetDisplayName = request.TargetDisplayName,
            ActorUserId = actorUserId,
            ActorDisplayName = actorDisplayName,
            Reason = request.Reason,
            Description = request.Description,
            DurationDays = request.DurationDays,
            AllowsAppeal = request.AllowsAppeal,
            IsInstanceAction = request.IsInstanceAction,
            InstanceId = request.InstanceId,
            ActionTime = actionTime,
            ExpiresAt = expiresAt,
            IsActive = true
        };
        
        context.ModerationActions.Add(entity);
        await context.SaveChangesAsync();
        
        LoggingService.Info("MODERATION", $"Logged {request.ActionType} action: {request.TargetDisplayName} ({request.TargetUserId}) - Reason: {request.Reason}");
        
        return actionId;
    }

    public async Task<InfractionHistory> GetInfractionHistoryAsync(string groupId, string userId, string? specificReason = null)
    {
        using var context = new AppDbContext();
        
        var allActions = await context.ModerationActions
            .Where(a => a.GroupId == groupId && a.TargetUserId == userId)
            .OrderByDescending(a => a.ActionTime)
            .ToListAsync();
        
        var oneMonthAgo = DateTime.UtcNow.AddDays(-30);
        
        var history = new InfractionHistory
        {
            TotalKicks = allActions.Count(a => a.ActionType == "kick"),
            TotalBans = allActions.Count(a => a.ActionType == "ban"),
            TotalWarnings = allActions.Count(a => a.ActionType == "warning"),
            TotalActive = allActions.Count(a => a.IsActive),
            
            LastKickDate = allActions.Where(a => a.ActionType == "kick").Max(a => (DateTime?)a.ActionTime),
            LastBanDate = allActions.Where(a => a.ActionType == "ban").Max(a => (DateTime?)a.ActionTime),
            LastWarningDate = allActions.Where(a => a.ActionType == "warning").Max(a => (DateTime?)a.ActionTime),
            
            WarningsLastMonth = allActions.Count(a => a.ActionType == "warning" && a.ActionTime >= oneMonthAgo)
        };
        
        if (!string.IsNullOrEmpty(specificReason))
        {
            history.KicksForReason = allActions.Count(a => a.ActionType == "kick" && a.Reason == specificReason);
            history.BansForReason = allActions.Count(a => a.ActionType == "ban" && a.Reason == specificReason);
            history.WarningsForReason = allActions.Count(a => a.ActionType == "warning" && a.Reason == specificReason);
        }
        
        // Get recent actions (last 10)
        history.RecentActions = allActions.Take(10).Select(a => new ModerationActionSummary
        {
            ActionType = a.ActionType,
            Reason = a.Reason,
            Description = a.Description,
            ActionTime = a.ActionTime,
            ActorDisplayName = a.ActorDisplayName,
            IsActive = a.IsActive,
            ExpiresAt = a.ExpiresAt
        }).ToList();
        
        return history;
    }

    public async Task<List<ModerationActionSummary>> GetUserModerationHistoryAsync(string groupId, string userId, int limit = 10)
    {
        using var context = new AppDbContext();
        
        var actions = await context.ModerationActions
            .Where(a => a.GroupId == groupId && a.TargetUserId == userId)
            .OrderByDescending(a => a.ActionTime)
            .Take(limit)
            .Select(a => new ModerationActionSummary
            {
                ActionType = a.ActionType,
                Reason = a.Reason,
                Description = a.Description,
                ActionTime = a.ActionTime,
                ActorDisplayName = a.ActorDisplayName,
                IsActive = a.IsActive,
                ExpiresAt = a.ExpiresAt
            })
            .ToListAsync();
        
        return actions;
    }

    public async Task<bool> RevokeActionAsync(string actionId, string revokedByUserId, string revokeReason)
    {
        using var context = new AppDbContext();
        
        var action = await context.ModerationActions
            .FirstOrDefaultAsync(a => a.ActionId == actionId);
        
        if (action == null)
        {
            return false;
        }
        
        action.IsActive = false;
        action.RevokedAt = DateTime.UtcNow;
        action.RevokedByUserId = revokedByUserId;
        action.RevokeReason = revokeReason;
        
        await context.SaveChangesAsync();
        
        LoggingService.Info("MODERATION", $"Revoked action {actionId} by {revokedByUserId}");
        
        return true;
    }

    public async Task<int> GetActiveWarningsCountAsync(string groupId, string userId, int daysBack = 30)
    {
        using var context = new AppDbContext();
        
        var cutoffDate = DateTime.UtcNow.AddDays(-daysBack);
        
        var count = await context.ModerationActions
            .Where(a => a.GroupId == groupId 
                && a.TargetUserId == userId 
                && a.ActionType == "warning"
                && a.ActionTime >= cutoffDate
                && a.IsActive)
            .CountAsync();
        
        return count;
    }
}
