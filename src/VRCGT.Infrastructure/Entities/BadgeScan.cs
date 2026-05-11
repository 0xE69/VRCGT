using VRCGT.Core;

namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A single badge-scan run for a managed group.
/// Scanning happens inside a <see cref="BackgroundJob"/>; the <see cref="JobId"/>
/// links the two records so progress can be streamed via the existing job hub.
/// </summary>
public sealed class BadgeScan
{
    public long Id { get; set; }

    public string GroupId { get; set; } = string.Empty;

    public BadgeScanStatus Status { get; set; } = BadgeScanStatus.Pending;

    /// <summary>UTC time the scan was requested.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time the scan finished (Completed / Failed / Cancelled).</summary>
    public DateTime? CompletedAt { get; set; }

    public int TotalMembers   { get; set; }
    public int ScannedMembers { get; set; }
    public int VerifiedCount  { get; set; }
    public int UnverifiedCount{ get; set; }
    public int UnknownCount   { get; set; }
    public int KickedCount    { get; set; }

    /// <summary>FK to the BackgroundJob that performed (or is performing) this scan.</summary>
    public long? JobId { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<BadgeScanMember> Members { get; set; } = new List<BadgeScanMember>();
}
