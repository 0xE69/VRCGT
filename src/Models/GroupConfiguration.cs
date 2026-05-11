using System;

namespace VRCGroupTools.Models;

/// <summary>
/// Configuration for a single managed group
/// </summary>
public class GroupConfiguration
{
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string? GroupIconUrl { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessed { get; set; }
    
    // Discord Webhook URLs per category
    public string? DiscordWebhookMemberEvents { get; set; }
    public string? DiscordWebhookRoleEvents { get; set; }
    public string? DiscordWebhookInstanceEvents { get; set; }
    public string? DiscordWebhookGroupEvents { get; set; }
    public string? DiscordWebhookInviteEvents { get; set; }
    public string? DiscordWebhookAnnouncementEvents { get; set; }
    public string? DiscordWebhookGalleryEvents { get; set; }
    public string? DiscordWebhookPostEvents { get; set; }
    
    // Discord Settings per group
    public string? DiscordWebhookUrl { get; set; } // Legacy - kept for migration
    public bool DiscordNotifyUserJoins { get; set; } = false;
    public bool DiscordNotifyUserLeaves { get; set; } = false;
    public bool DiscordNotifyUserUpdates { get; set; } = false;
    public bool DiscordNotifyRoleAssignments { get; set; } = false;
    public bool DiscordNotifyUserKicked { get; set; } = false;
    public bool DiscordNotifyUserBanned { get; set; } = false;
    public bool DiscordNotifyRoleCreate { get; set; } = false;
    public bool DiscordNotifyRoleUpdate { get; set; } = false;
    public bool DiscordNotifyRoleDelete { get; set; } = false;
    public bool DiscordNotifyInstanceCreate { get; set; } = false;
    public bool DiscordNotifyInstanceDelete { get; set; } = false;
    public bool DiscordNotifyInstanceOpened { get; set; } = false;
    public bool DiscordNotifyInstanceClosed { get; set; } = false;
    public bool DiscordNotifyInstanceWarn { get; set; } = false;
    public bool DiscordNotifyGroupUpdate { get; set; } = false;
    public bool DiscordNotifyJoinRequest { get; set; } = false;
    public bool DiscordNotifyInviteSent { get; set; } = false;
    public bool DiscordNotifyJoinApproval { get; set; } = false;
    public bool DiscordNotifyJoinRejection { get; set; } = false;
    public bool DiscordNotifyAnnouncementCreate { get; set; } = false;
    public bool DiscordNotifyAnnouncementUpdate { get; set; } = false;
    public bool DiscordNotifyAnnouncementDelete { get; set; } = false;
    public bool DiscordNotifyGalleryImageSubmitted { get; set; } = false;
    public bool DiscordNotifyGalleryImageApproved { get; set; } = false;
    public bool DiscordNotifyGalleryImageDeleted { get; set; } = false;
    public bool DiscordNotifyPostCreate { get; set; } = false;
    public bool DiscordNotifyPostUpdate { get; set; } = false;
    public bool DiscordNotifyPostDelete { get; set; } = false;

    // Additional Discord properties to match ViewModel
    public bool DiscordNotifyUserUnbanned { get; set; } = false;
    public bool DiscordNotifyUserRoleAdd { get; set; } = false;
    public bool DiscordNotifyUserRoleRemove { get; set; } = false;
    public bool DiscordNotifyInviteCreate { get; set; } = false;
    public bool DiscordNotifyInviteAccept { get; set; } = false;
    public bool DiscordNotifyInviteReject { get; set; } = false;
    public bool DiscordNotifyJoinRequests { get; set; } = false;
    public bool DiscordNotifyGalleryCreate { get; set; } = false;
    public bool DiscordNotifyGalleryDelete { get; set; } = false;
    
    // Security Settings per group
    public bool SecurityMonitoringEnabled { get; set; } = false;
    public bool SecurityAutoRemoveRoles { get; set; } = true;
    public string? SecurityAlertWebhookUrl { get; set; }
    public bool SecurityMonitorInstanceKicks { get; set; } = true;
    public int SecurityInstanceKickThreshold { get; set; } = 10;
    public int SecurityInstanceKickTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorGroupKicks { get; set; } = true;
    public int SecurityGroupKickThreshold { get; set; } = 5;
    public int SecurityGroupKickTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorInstanceBans { get; set; } = true;
    public int SecurityInstanceBanThreshold { get; set; } = 10;
    public int SecurityInstanceBanTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorGroupBans { get; set; } = true;
    public int SecurityGroupBanThreshold { get; set; } = 3;
    public int SecurityGroupBanTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorRoleRemovals { get; set; } = true;
    public int SecurityRoleRemovalThreshold { get; set; } = 5;
    public int SecurityRoleRemovalTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorInviteRejections { get; set; } = true;
    public int SecurityInviteRejectionThreshold { get; set; } = 10;
    public int SecurityInviteRejectionTimeframeMinutes { get; set; } = 10;
    public bool SecurityMonitorPostDeletions { get; set; } = true;
    public int SecurityPostDeletionThreshold { get; set; } = 5;
    public int SecurityPostDeletionTimeframeMinutes { get; set; } = 10;
    public bool SecurityRequireOwnerRole { get; set; } = true;
    public bool SecurityNotifyDiscord { get; set; } = true;
    public bool SecurityLogAllActions { get; set; } = true;
    public string SecurityOwnerUserId { get; set; } = "";
    
    // Display property for UI
    public string DisplayName => string.IsNullOrWhiteSpace(GroupName) ? GroupId : $"{GroupName} ({GroupId})";
}
