using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VRCGT.Core;
using VRCGT.Infrastructure;
using VRCGT.Infrastructure.Entities;
using Xunit;

namespace VRCGT.Infrastructure.Tests;

/// <summary>
/// Unit tests for LocalUserService using an in-memory SQLite database.
/// Each test gets its own fresh database via SqliteTestDbFactory so tests
/// are fully isolated and do not interfere with each other.
/// </summary>
public sealed class LocalUserServiceTests : IDisposable
{
    private readonly SqliteTestDbFactory _dbFactory;
    private readonly IPasswordHasher<LocalUser> _hasher;
    private readonly LocalUserService _sut;

    public LocalUserServiceTests()
    {
        _dbFactory = new SqliteTestDbFactory();
        _hasher    = new PasswordHasher<LocalUser>();
        _sut       = new LocalUserService(_dbFactory, _hasher);
    }

    public void Dispose() => _dbFactory.Dispose();

    // ── Setup status ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IsSetupRequired_returns_true_when_no_owner_exists()
    {
        var result = await _sut.IsSetupRequiredAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task IsSetupRequired_returns_false_after_owner_is_created()
    {
        await _sut.CreateOwnerAsync("admin", "Password1!", "Admin");

        var result = await _sut.IsSetupRequiredAsync();
        Assert.False(result);
    }

    // ── Owner creation ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOwner_returns_LocalUserDto_with_expected_fields()
    {
        var dto = await _sut.CreateOwnerAsync("owner1", "Passw0rd!", "Owner One");

        Assert.Equal("owner1",    dto.Username);
        Assert.Equal("Owner One", dto.DisplayName);
        Assert.Equal("Owner",     dto.Role);
        Assert.True(dto.IsEnabled);
        Assert.True(dto.Id > 0);
    }

    [Fact]
    public async Task CreateOwner_stores_hashed_password_not_plaintext()
    {
        await _sut.CreateOwnerAsync("owner2", "MySecret#8", "Owner Two");

        // Verify directly via the DB that no column contains the plaintext password
        await using var db = _dbFactory.CreateDbContext();
        var user = await db.LocalUsers.SingleAsync(u => u.Username == "owner2");

        Assert.NotEqual("MySecret#8", user.PasswordHash);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
    }

    [Fact]
    public async Task CreateOwner_twice_throws_InvalidOperationException()
    {
        await _sut.CreateOwnerAsync("ownerA", "Password1!", "Owner A");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateOwnerAsync("ownerB", "Password2!", "Owner B"));
    }

    [Theory]
    [InlineData("user@example.com")]   // @ not allowed
    [InlineData("user.name")]          // . not allowed
    [InlineData("ab")]                 // too short
    public async Task CreateOwner_throws_ArgumentException_for_invalid_username(string username)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateOwnerAsync(username, "Password1!", "Test"));
    }

    [Theory]
    [InlineData("short")]   // < 8 chars
    [InlineData("1234567")] // exactly 7 chars
    public async Task CreateOwner_throws_ArgumentException_for_short_password(string password)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateOwnerAsync("validname", password, "Test"));
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task Authenticate_returns_dto_for_correct_credentials()
    {
        await _sut.CreateOwnerAsync("authuser", "CorrectPass1", "Auth User");

        var dto = await _sut.AuthenticateAsync("authuser", "CorrectPass1");

        Assert.NotNull(dto);
        Assert.Equal("authuser", dto!.Username);
    }

    [Fact]
    public async Task Authenticate_is_case_insensitive_on_username()
    {
        await _sut.CreateOwnerAsync("caseuser", "Pass1234!", "Case User");

        // Username stored lowercase; login with mixed case should still work.
        var dto = await _sut.AuthenticateAsync("CaseUser", "Pass1234!");
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task Authenticate_returns_null_for_wrong_password()
    {
        await _sut.CreateOwnerAsync("passuser", "RealPass1!", "Pass User");

        var dto = await _sut.AuthenticateAsync("passuser", "WrongPass!");
        Assert.Null(dto);
    }

    [Fact]
    public async Task Authenticate_returns_null_for_nonexistent_username()
    {
        var dto = await _sut.AuthenticateAsync("nobody", "AnyPass123");
        Assert.Null(dto);
    }

    [Theory]
    [InlineData("", "pass")]
    [InlineData("user", "")]
    [InlineData("   ", "pass")]
    public async Task Authenticate_returns_null_for_empty_or_whitespace_inputs(
        string username, string password)
    {
        var dto = await _sut.AuthenticateAsync(username, password);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Authenticate_updates_LastLoginAt_on_success()
    {
        await _sut.CreateOwnerAsync("logintrack", "Tr@ckPass1", "Login Tracker");

        var before = DateTime.UtcNow;
        await _sut.AuthenticateAsync("logintrack", "Tr@ckPass1");
        var after = DateTime.UtcNow;

        await using var db = _dbFactory.CreateDbContext();
        var user = await db.LocalUsers.SingleAsync(u => u.Username == "logintrack");

        Assert.NotNull(user.LastLoginAt);
        Assert.True(user.LastLoginAt >= before.AddSeconds(-1));
        Assert.True(user.LastLoginAt <= after.AddSeconds(1));
    }

    // ── Password hashing round-trip ───────────────────────────────────────────

    [Fact]
    public void PasswordHasher_round_trip_succeeds_for_same_password()
    {
        var user = new LocalUser { Username = "testuser" };
        var hash = _hasher.HashPassword(user, "MyTestPass1!");

        var result = _hasher.VerifyHashedPassword(user, hash, "MyTestPass1!");

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void PasswordHasher_fails_for_different_password()
    {
        var user = new LocalUser { Username = "testuser" };
        var hash = _hasher.HashPassword(user, "OriginalPass1");

        var result = _hasher.VerifyHashedPassword(user, hash, "DifferentPass1");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }
}
