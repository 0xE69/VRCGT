// VRCGT.Core — Domain interfaces and shared models for VRCGT v2.
// This project has no dependency on ASP.NET Core, EF Core, or any infrastructure.
// All other projects reference this one; this project references nothing.

namespace VRCGT.Core;

/// <summary>
/// Provides basic application identity information.
/// Implemented by VRCGT.Infrastructure.AppInfo.
/// </summary>
public interface IAppInfo
{
    string AppName { get; }
    string Version { get; }
    string RuntimeDescription { get; }
}

/// <summary>
/// Manages local VRCGT user accounts (setup, login, lookup).
/// Implemented by VRCGT.Infrastructure.LocalUserService.
/// </summary>
public interface ILocalUserService
{
    /// <summary>Returns true when no Owner account exists in the database.</summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates the first Owner account during first-run setup.
    /// Throws <see cref="InvalidOperationException"/> if an Owner already exists.
    /// </summary>
    Task<LocalUserDto> CreateOwnerAsync(string username, string password, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Validates credentials and returns the user on success, or null on failure.
    /// Updates LastLoginAt on success.
    /// </summary>
    Task<LocalUserDto?> AuthenticateAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Returns user by ID, or null if not found.</summary>
    Task<LocalUserDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Returns all users ordered by ID.</summary>
    Task<IReadOnlyList<LocalUserDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates a new non-Owner local user.</summary>
    Task<LocalUserDto> CreateUserAsync(
        string username, string password, string displayName, string role,
        CancellationToken ct = default);

    /// <summary>
    /// Updates display name, role, and/or enabled state.
    /// Rejects changes that would leave zero active Owner accounts.
    /// </summary>
    Task<LocalUserDto> UpdateUserAsync(
        int id, string? displayName, string? role, bool? isEnabled,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a user.
    /// Rejects deletion of the last active Owner.
    /// </summary>
    Task DeleteUserAsync(int id, CancellationToken ct = default);

    /// <summary>Replaces a user's password hash.</summary>
    Task ResetPasswordAsync(int id, string newPassword, CancellationToken ct = default);
}

/// <summary>
/// Public projection of a LocalUser — safe to return in API responses.
/// PasswordHash is never included.
/// </summary>
public sealed record LocalUserDto(
    int    Id,
    string Username,
    string DisplayName,
    string Role,
    bool   IsEnabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    string? VrchatUserId,
    string? VrchatDisplayName,
    IReadOnlyList<string> Permissions
);

/// <summary>
/// Resolves the local application data directory for VRCGT v2 and the
/// canonical paths for its well-known subdirectories.
/// Implemented by VRCGT.Infrastructure.AppDataPathProvider.
/// </summary>
public interface IAppDataPathProvider
{
    /// <summary>Root v2 data directory (platform-specific).</summary>
    string RootPath { get; }

    /// <summary>Persistent database and user data files.</summary>
    string DataPath { get; }

    /// <summary>Rotating application log files.</summary>
    string LogsPath { get; }

    /// <summary>Short-lived cached API responses.</summary>
    string CachePath { get; }

    /// <summary>User-initiated CSV / JSON exports.</summary>
    string ExportsPath { get; }

    /// <summary>Automatic member-list backups.</summary>
    string BackupsPath { get; }

    /// <summary>Full path to the v2 SQLite database file.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Ensures all well-known subdirectories exist.
    /// Safe to call on every startup — no-op if they already exist.
    /// </summary>
    void EnsureDirectoriesExist();
}

/// <summary>
/// Well-known string constants shared across the server and launcher.
/// </summary>
public static class AppConstants
{
    public const string AppName = "VRCGT";
    public const string AppVersion = "2.0.0-alpha.1";

    /// <summary>
    /// Default port the Kestrel server listens on.
    /// Bound to 127.0.0.1 only. LAN mode is opt-in (see Section 12 of WEB_LOCAL_APP_MIGRATION_PLAN.md).
    /// </summary>
    public const int DefaultPort = 57432;

    /// <summary>
    /// Default bind address. Never changed to 0.0.0.0 without explicit LAN opt-in.
    /// </summary>
    public const string DefaultBindAddress = "127.0.0.1";
}
