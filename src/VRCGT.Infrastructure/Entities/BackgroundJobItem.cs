namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A single item processed (or to be processed) by a <see cref="BackgroundJob"/>.
/// E.g. one user in a mass-invite job, or one member row in a backup job.
/// </summary>
public sealed class BackgroundJobItem
{
    public long Id { get; set; }

    public long JobId { get; set; }
    public BackgroundJob Job { get; set; } = null!;

    /// <summary>VRChat entity ID this item references (user ID, group ID, etc.).</summary>
    public string? EntityId { get; set; }

    public BackgroundJobItemStatus Status { get; set; } = BackgroundJobItemStatus.Pending;

    /// <summary>Optional error detail if this item failed.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

public enum BackgroundJobItemStatus
{
    Pending   = 0,
    Success   = 1,
    Failed    = 2,
    Skipped   = 3
}
