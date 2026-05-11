namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A single audit log event — either fetched from the VRChat API or recorded
/// locally when VRCGT itself performs a moderation action.
/// </summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }

    /// <summary>VRChat group ID this event belongs to.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the event (from VRChat API, or UtcNow for local events).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// VRChat audit event type, e.g. "group.member.ban", "group.member.kick".
    /// Local VRCGT events use "vrcgt.*" prefix.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>VRChat user ID of the moderator who performed the action.</summary>
    public string? ActorId { get; set; }

    /// <summary>Display name of the actor at time of event.</summary>
    public string? ActorDisplayName { get; set; }

    /// <summary>VRChat user ID of the member the action was taken against.</summary>
    public string? TargetId { get; set; }

    /// <summary>Display name of the target at time of event.</summary>
    public string? TargetDisplayName { get; set; }

    /// <summary>Human-readable description of the event.</summary>
    public string? Description { get; set; }

    /// <summary>Full JSON response from the VRChat API for this event. Null for local events.</summary>
    public string? RawJson { get; set; }

    /// <summary>"vrchat" for VRChat API entries; "vrcgt" for locally-generated events.</summary>
    public string Source { get; set; } = "vrchat";

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
