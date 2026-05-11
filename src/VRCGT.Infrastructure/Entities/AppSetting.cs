namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// Key-value store for application settings.
/// No secrets are stored here — see docs/v2/storage.md.
/// </summary>
public sealed class AppSetting
{
    /// <summary>Dot-delimited setting key, e.g. "discord.webhookEnabled".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>String-serialised value. Complex values are stored as JSON.</summary>
    public string? Value { get; set; }

    /// <summary>UTC timestamp of the last write.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
