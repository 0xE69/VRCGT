namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// Represents a long-running background operation (e.g. mass-invite, member backup).
/// Contains one or more <see cref="BackgroundJobItem"/> rows for per-item progress.
/// </summary>
public sealed class BackgroundJob
{
    public long Id { get; set; }

    /// <summary>Machine-readable job type, e.g. "mass-invite", "member-backup".</summary>
    public string JobType { get; set; } = string.Empty;

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Pending;

    /// <summary>Human-readable label shown in the UI job progress panel.</summary>
    public string? Label { get; set; }

    /// <summary>Optional JSON payload specific to the job type.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>Optional error message set when Status is <see cref="BackgroundJobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }

    /// <summary>Human-readable label of the item currently being processed. Updated during execution.</summary>
    public string? CurrentItemLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<BackgroundJobItem> Items { get; set; } = [];
}

public enum BackgroundJobStatus
{
    Pending    = 0,
    Running    = 1,
    Completed  = 2,
    Failed     = 3,
    Cancelled  = 4,
    Cancelling = 5,
}
