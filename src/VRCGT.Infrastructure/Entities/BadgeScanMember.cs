namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// One member's result within a <see cref="BadgeScan"/>.
/// </summary>
public sealed class BadgeScanMember
{
    public long Id { get; set; }

    public long ScanId { get; set; }
    public BadgeScan Scan { get; set; } = null!;

    public string UserId      { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c>  = 18+ badge confirmed.<br/>
    /// <c>false</c> = badge absent.<br/>
    /// <c>null</c>  = API call failed / unknown.
    /// </summary>
    public bool? IsAgeVerified { get; set; }

    /// <summary>Whether this member was kicked via the badge scanner.</summary>
    public bool WasKicked { get; set; }

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}
