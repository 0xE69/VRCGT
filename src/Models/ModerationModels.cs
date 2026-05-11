namespace VRCGroupTools.Models;

/// <summary>
/// Predefined moderation reasons
/// </summary>
public static class ModerationReasons
{
    // Ban/Kick Reasons
    public static readonly string[] BanReasons = new[]
    {
        "Harassment",
        "Hate Speech",
        "Inappropriate Content",
        "Spam/Advertising",
        "Underage User",
        "Terms of Service Violation",
        "Doxxing/Privacy Violation",
        "Threatening Behavior",
        "Alt Account",
        "Ban Evasion",
        "Trolling/Disruptive Behavior",
        "NSFW Content in SFW Space",
        "Repeated Rule Violations",
        "Impersonation",
        "Scamming/Fraud",
        "Other"
    };

    public static readonly string[] KickReasons = new[]
    {
        "Warning - Rule Violation",
        "Disruptive Behavior",
        "Inappropriate Language",
        "Spam",
        "NSFW Content",
        "Trolling",
        "Request - User Asked to Leave",
        "Cooldown Period",
        "Temporary Removal",
        "Other"
    };

    public static readonly string[] WarningReasons = new[]
    {
        "Minor Rule Violation",
        "Inappropriate Language",
        "Spam",
        "Off-Topic Discussion",
        "Disruptive Behavior",
        "NSFW Content Warning",
        "Tone/Attitude",
        "Backseat Moderating",
        "General Warning",
        "Other"
    };
}

/// <summary>
/// Predefined duration options for moderation actions
/// </summary>
public class ModerationDuration
{
    public string Label { get; set; } = string.Empty;
    public int Days { get; set; }
    public bool AllowsAppeal { get; set; } = true;
    
    public static readonly ModerationDuration[] Durations = new[]
    {
        new ModerationDuration { Label = "7 Days", Days = 7, AllowsAppeal = true },
        new ModerationDuration { Label = "14 Days", Days = 14, AllowsAppeal = true },
        new ModerationDuration { Label = "30 Days", Days = 30, AllowsAppeal = true },
        new ModerationDuration { Label = "6 Months (180 Days)", Days = 180, AllowsAppeal = true },
        new ModerationDuration { Label = "Permanent (Appeal)", Days = 0, AllowsAppeal = true },
        new ModerationDuration { Label = "Permanent (No Appeal)", Days = 0, AllowsAppeal = false }
    };
    
    public override string ToString() => Label;
}

/// <summary>
/// Request model for executing a moderation action
/// </summary>
public class ModerationActionRequest
{
    public string ActionType { get; set; } = string.Empty; // "kick", "ban", "warning"
    public string TargetUserId { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationDays { get; set; }
    public bool AllowsAppeal { get; set; } = true;
    public bool IsInstanceAction { get; set; }
    public string? InstanceId { get; set; }
}

/// <summary>
/// Result of fetching infraction history
/// </summary>
public class InfractionHistory
{
    public int TotalKicks { get; set; }
    public int TotalBans { get; set; }
    public int TotalWarnings { get; set; }
    public int TotalActive { get; set; }
    
    public int KicksForReason { get; set; }
    public int BansForReason { get; set; }
    public int WarningsForReason { get; set; }
    public int WarningsLastMonth { get; set; }
    
    public DateTime? LastKickDate { get; set; }
    public DateTime? LastBanDate { get; set; }
    public DateTime? LastWarningDate { get; set; }
    
    public List<ModerationActionSummary> RecentActions { get; set; } = new();
}

/// <summary>
/// Summary of a moderation action for display
/// </summary>
public class ModerationActionSummary
{
    public string ActionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime ActionTime { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
