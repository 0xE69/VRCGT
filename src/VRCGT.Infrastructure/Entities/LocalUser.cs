namespace VRCGT.Infrastructure.Entities;

/// <summary>
/// A local VRCGT account used to authenticate with the web UI.
/// This is NOT a VRChat account — it is an application-level account
/// that grants access to the local VRCGT web console.
///
/// Each installation has exactly one Owner account (created during first-run
/// setup) and may have additional Admin / Moderator / Viewer accounts.
///
/// Passwords are stored as Argon2-style hashes via ASP.NET Core's
/// PasswordHasher&lt;T&gt; (PBKDF2 / SHA-512, 100 000 iterations by default in v3).
/// The hash includes algorithm, iteration count, and salt — no separate salt
/// column is needed.  Plain-text passwords are never stored.
/// </summary>
public sealed class LocalUser
{
    /// <summary>Surrogate integer primary key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Short login handle, e.g. "admin".
    /// Unique, case-insensitive (stored lowercase).
    /// Max 64 characters; only alphanumeric, underscore, hyphen allowed
    /// (validated at the service layer, not enforced by the DB column).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2/SHA-512 hash produced by ASP.NET Core PasswordHasher&lt;LocalUser&gt;.
    /// Format: "AQ…base64…" (version 3 prefix).
    /// Never returned in API responses.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Human-readable display name shown in the UI, e.g. "Alice".</summary>
    public string DisplayName { get; set; } = string.Empty;

    public LocalUserRole Role { get; set; } = LocalUserRole.Viewer;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    // ---------- VRChat identity (populated after VRChat login, Phase 2) ------
    /// <summary>VRChat user ID linked to this account. Null until VRChat login.</summary>
    public string? VrchatUserId { get; set; }

    public string? VrchatDisplayName { get; set; }
}

/// <summary>
/// Access roles for local VRCGT accounts.
/// Higher numeric value = more privilege (not relied upon in code — always
/// compare by enum name for clarity).
/// </summary>
public enum LocalUserRole
{
    Viewer       = 0,
    EventManager = 1,
    Moderator    = 2,
    Admin        = 3,
    Owner        = 4
}
