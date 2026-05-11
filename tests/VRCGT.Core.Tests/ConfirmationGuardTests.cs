using VRCGT.Core;
using Xunit;

namespace VRCGT.Core.Tests;

public sealed class ConfirmationGuardTests
{
    // ── BuildToken ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildToken_returns_ACTION_colon_RESOURCE()
    {
        var token = ConfirmationGuard.BuildToken("DELETE_GROUP", "grp_abc123");
        Assert.Equal("DELETE_GROUP:grp_abc123", token);
    }

    // ── IsValid ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_returns_true_for_matching_token()
    {
        var token = ConfirmationGuard.BuildToken(ConfirmationGuard.DeleteGroup, "grp_1");
        Assert.True(ConfirmationGuard.IsValid(token, ConfirmationGuard.DeleteGroup, "grp_1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_returns_false_for_null_or_whitespace_token(string? token)
    {
        Assert.False(ConfirmationGuard.IsValid(token, ConfirmationGuard.DeleteGroup, "grp_1"));
    }

    [Fact]
    public void IsValid_returns_false_for_wrong_action()
    {
        var token = ConfirmationGuard.BuildToken(ConfirmationGuard.KickMember, "usr_1");
        Assert.False(ConfirmationGuard.IsValid(token, ConfirmationGuard.BanMember, "usr_1"));
    }

    [Fact]
    public void IsValid_returns_false_for_wrong_resource()
    {
        var token = ConfirmationGuard.BuildToken(ConfirmationGuard.DeleteGroup, "grp_1");
        Assert.False(ConfirmationGuard.IsValid(token, ConfirmationGuard.DeleteGroup, "grp_2"));
    }

    [Fact]
    public void IsValid_is_case_sensitive()
    {
        var token = ConfirmationGuard.BuildToken(ConfirmationGuard.DeleteGroup, "grp_1");
        // lower-cased action should NOT match
        Assert.False(ConfirmationGuard.IsValid(token, "delete_group", "grp_1"));
    }

    [Fact]
    public void IsValid_returns_false_for_partial_match()
    {
        // A token that is a prefix of the expected value must not match
        Assert.False(ConfirmationGuard.IsValid("DELETE_GROUP", ConfirmationGuard.DeleteGroup, "grp_1"));
    }

    // ── Require ───────────────────────────────────────────────────────────────

    [Fact]
    public void Require_does_not_throw_for_valid_token()
    {
        var token = ConfirmationGuard.BuildToken(ConfirmationGuard.DeleteBackup, "bak_99");
        // Should not throw
        ConfirmationGuard.Require(token, ConfirmationGuard.DeleteBackup, "bak_99");
    }

    [Fact]
    public void Require_throws_ConfirmationRequiredException_for_null_token()
    {
        var ex = Assert.Throws<ConfirmationRequiredException>(
            () => ConfirmationGuard.Require(null, ConfirmationGuard.DeleteGroup, "grp_1"));

        Assert.Equal(ConfirmationGuard.DeleteGroup, ex.Action);
        Assert.Equal("grp_1", ex.ResourceId);
    }

    [Fact]
    public void Require_throws_ConfirmationRequiredException_for_wrong_token()
    {
        var ex = Assert.Throws<ConfirmationRequiredException>(
            () => ConfirmationGuard.Require("wrong_token", ConfirmationGuard.KickMember, "usr_42"));

        Assert.Equal(ConfirmationGuard.KickMember, ex.Action);
        Assert.Equal("usr_42", ex.ResourceId);
    }

    [Fact]
    public void ConfirmationRequiredException_message_includes_action_and_resource()
    {
        var ex = new ConfirmationRequiredException("MY_ACTION", "res_1");
        Assert.Contains("MY_ACTION", ex.Message);
        Assert.Contains("res_1", ex.Message);
    }
}
