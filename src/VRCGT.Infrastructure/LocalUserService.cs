using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure.Entities;

namespace VRCGT.Infrastructure;

/// <summary>
/// Manages local VRCGT user accounts.
///
/// Password hashing uses ASP.NET Core's PasswordHasher&lt;LocalUser&gt; which
/// defaults to PBKDF2 with HMAC-SHA512, 100 000 iterations, and a 128-bit
/// random salt (v3 format, compatible with OWASP recommendations).
/// The hash is self-describing: no separate salt column is required.
/// </summary>
public sealed class LocalUserService : ILocalUserService
{
    private readonly IDbContextFactory<VrcgtDbContext> _dbFactory;
    private readonly IPasswordHasher<LocalUser>        _hasher;

    public LocalUserService(
        IDbContextFactory<VrcgtDbContext> dbFactory,
        IPasswordHasher<LocalUser>        hasher)
    {
        _dbFactory = dbFactory;
        _hasher    = hasher;
    }

    /// <inheritdoc />
    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return !await db.LocalUsers.AnyAsync(u => u.Role == LocalUserRole.Owner && u.IsEnabled, ct);
    }

    /// <inheritdoc />
    public async Task<LocalUserDto> CreateOwnerAsync(
        string username, string password, string displayName, CancellationToken ct = default)
    {
        ValidateUsername(username);
        ValidatePassword(password);
        ValidateDisplayName(displayName);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (await db.LocalUsers.AnyAsync(u => u.Role == LocalUserRole.Owner, ct))
            throw new InvalidOperationException("An Owner account already exists. Setup can only be performed once.");

        var normalised = username.Trim().ToLowerInvariant();

        if (await db.LocalUsers.AnyAsync(u => u.Username == normalised, ct))
            throw new InvalidOperationException($"Username '{normalised}' is already taken.");

        var user = new LocalUser
        {
            Username    = normalised,
            DisplayName = displayName.Trim(),
            Role        = LocalUserRole.Owner,
            IsEnabled   = true,
            CreatedAt   = DateTime.UtcNow
        };

        // Hash with a temporary instance so the hash includes the (as-yet-unsaved) user identity
        user.PasswordHash = _hasher.HashPassword(user, password);

        db.LocalUsers.Add(user);
        await db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    /// <inheritdoc />
    public async Task<LocalUserDto?> AuthenticateAsync(
        string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var normalised = username.Trim().ToLowerInvariant();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers
            .FirstOrDefaultAsync(u => u.Username == normalised && u.IsEnabled, ct);

        if (user is null) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
            return null;

        // Rehash on Success with a newer algorithm version if needed
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    /// <inheritdoc />
    public async Task<LocalUserDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers.FindAsync([id], ct);
        return user is null ? null : ToDto(user);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalUserDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var users = await db.LocalUsers.OrderBy(u => u.Id).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<LocalUserDto> CreateUserAsync(
        string username, string password, string displayName, string role,
        CancellationToken ct = default)
    {
        ValidateUsername(username);
        ValidatePassword(password);
        ValidateDisplayName(displayName);

        if (!Enum.TryParse<LocalUserRole>(role, out var roleEnum))
            throw new ArgumentException($"Invalid role '{role}'.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var normalised = username.Trim().ToLowerInvariant();
        if (await db.LocalUsers.AnyAsync(u => u.Username == normalised, ct))
            throw new InvalidOperationException($"Username '{normalised}' is already taken.");

        var user = new LocalUser
        {
            Username    = normalised,
            DisplayName = displayName.Trim(),
            Role        = roleEnum,
            IsEnabled   = true,
            CreatedAt   = DateTime.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        db.LocalUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <inheritdoc />
    public async Task<LocalUserDto> UpdateUserAsync(
        int id, string? displayName, string? role, bool? isEnabled,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.LocalUsers.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"User {id} not found.");

        if (user.Role == LocalUserRole.Owner)
        {
            bool wouldLoseOwner = (isEnabled == false) ||
                                  (role is not null && role != "Owner");
            if (wouldLoseOwner)
            {
                var ownerCount = await db.LocalUsers
                    .CountAsync(u => u.Role == LocalUserRole.Owner && u.IsEnabled, ct);
                if (ownerCount <= 1)
                    throw new InvalidOperationException(
                        "Cannot disable or demote the last Owner account.");
            }
        }

        if (displayName is not null)
        {
            ValidateDisplayName(displayName);
            user.DisplayName = displayName.Trim();
        }

        if (role is not null)
        {
            if (!Enum.TryParse<LocalUserRole>(role, out var roleEnum))
                throw new ArgumentException($"Invalid role '{role}'.");
            user.Role = roleEnum;
        }

        if (isEnabled.HasValue)
            user.IsEnabled = isEnabled.Value;

        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.LocalUsers.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"User {id} not found.");

        if (user.Role == LocalUserRole.Owner)
        {
            var ownerCount = await db.LocalUsers
                .CountAsync(u => u.Role == LocalUserRole.Owner && u.IsEnabled, ct);
            if (ownerCount <= 1)
                throw new InvalidOperationException(
                    "Cannot delete the last Owner account.");
        }

        db.LocalUsers.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(int id, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"User {id} not found.");

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------------------------

    private static LocalUserDto ToDto(LocalUser u) => new(
        u.Id, u.Username, u.DisplayName,
        u.Role.ToString(), u.IsEnabled,
        u.CreatedAt, u.LastLoginAt,
        u.VrchatUserId, u.VrchatDisplayName,
        PermissionMap.NamesFor(u.Role.ToString())
    );

    private static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username must not be empty.");

        var trimmed = username.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 64)
            throw new ArgumentException("Username must be 3–64 characters.");

        foreach (var ch in trimmed)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                throw new ArgumentException("Username may only contain letters, digits, underscores, and hyphens.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name must not be empty.");
        if (displayName.Trim().Length > 256)
            throw new ArgumentException("Display name must not exceed 256 characters.");
    }
}
