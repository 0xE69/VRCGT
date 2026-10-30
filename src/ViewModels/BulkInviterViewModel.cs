using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Data;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

/// <summary>
/// Bulk group inviter: paste a text list of VRChat user IDs (one per line) and send
/// group invites in one run. Dedupes against the built-in InvitedUsers database and
/// applies the same trust-level / 18+ filters used by the Instance Inviter
/// (new users / users / known users / non-18+).
/// </summary>
public partial class BulkInviterViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty] private string _userIdsText = string.Empty;

    // Skip filters. Defaults mirror the Instance Inviter defaults.
    [ObservableProperty] private bool _skipAlreadyInvited = true;
    [ObservableProperty] private bool _only18Plus = true;
    [ObservableProperty] private bool _skipNewUsers;
    [ObservableProperty] private bool _skipUsers;
    [ObservableProperty] private bool _skipKnown;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Paste one VRChat user ID per line, then run the bulk invite.";
    [ObservableProperty] private string _summaryText = string.Empty;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _invitedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _failedCount;

    public ObservableCollection<BulkInviteResult> Results { get; } = new();

    public BulkInviterViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    /// <summary>Open a text file of VRChat user IDs (one per line, or CSV) and load it into the list.</summary>
    [RelayCommand]
    private void LoadFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a text file with VRChat user IDs",
            Filter = "Text files (*.txt)|*.txt|Comma-separated (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            UserIdsText = text;
            StatusText = $"Loaded {Path.GetFileName(dlg.FileName)} — {ParsedIdCount} user ID(s) parsed.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read file: {ex.Message}";
        }
    }

    /// <summary>Live count of distinct IDs parsed from the text box.</summary>
    public int ParsedIdCount => ParseUserIds().Count;

    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(UserIdsText);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunBulkAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusText = "Set a Group ID first.";
            SummaryText = string.Empty;
            return;
        }

        var userIds = ParseUserIds();
        if (userIds.Count == 0)
        {
            StatusText = "No user IDs found. Paste one user ID per line.";
            SummaryText = string.Empty;
            return;
        }

        IsRunning = true;
        Results.Clear();
        InvitedCount = 0;
        SkippedCount = 0;
        FailedCount = 0;
        TotalCount = userIds.Count;
        SummaryText = string.Empty;
        StatusText = $"Loaded {userIds.Count} user ID(s). Starting...";

        try
        {
            using var db = new AppDbContext();

            // Pre-load the set of IDs already in the InvitedUsers DB (global, by UserId).
            var alreadyInvitedSet = new HashSet<string>();
            if (SkipAlreadyInvited)
            {
                var existing = await db.InvitedUsers
                    .Where(u => userIds.Contains(u.UserId))
                    .Select(u => u.UserId)
                    .ToListAsync();
                alreadyInvitedSet = new HashSet<string>(existing, StringComparer.Ordinal);
            }

            foreach (var userId in userIds)
            {
                var result = new BulkInviteResult { UserId = userId };

                try
                {
                    var profile = await _apiService.GetUserAsync(userId);
                    if (profile == null)
                    {
                        result.Outcome = "Failed";
                        result.Detail = "Could not fetch profile (invalid or deleted ID).";
                        FailedCount++;
                        Results.Add(result);
                        await Task.Delay(300);
                        continue;
                    }

                    result.DisplayName = profile.DisplayName;
                    result.ProfilePicUrl = profile.ProfilePicUrl;
                    var trustLevel = GetTrustLevel(profile.Tags);
                    result.TrustLevel = trustLevel;
                    result.IsAgeVerified = profile.IsAgeVerified;

                    // ---- Skip checks (order matters; first match wins) ----
                    if (SkipAlreadyInvited && alreadyInvitedSet.Contains(userId))
                    {
                        MarkSkipped(result, "Already invited (database).");
                        Results.Add(result);
                        await Task.Delay(150);
                        continue;
                    }

                    if (Only18Plus && !profile.IsAgeVerified)
                    {
                        MarkSkipped(result, "Not 18+ verified.");
                        Results.Add(result);
                        await Task.Delay(150);
                        continue;
                    }

                    if (SkipNewUsers && trustLevel == "New User")
                    {
                        MarkSkipped(result, "New user (trust: basic).");
                        Results.Add(result);
                        await Task.Delay(150);
                        continue;
                    }

                    if (SkipUsers && trustLevel == "User")
                    {
                        MarkSkipped(result, "User (trust: known).");
                        Results.Add(result);
                        await Task.Delay(150);
                        continue;
                    }

                    if (SkipKnown && trustLevel == "Known User")
                    {
                        MarkSkipped(result, "Known user (trust: trusted).");
                        Results.Add(result);
                        await Task.Delay(150);
                        continue;
                    }

                    // ---- Send the invite ----
                    StatusText = $"Inviting {profile.DisplayName} ({userId})...";
                    var ok = await _apiService.SendGroupInviteAsync(groupId, userId);

                    result.InviteSuccessful = ok;
                    result.Outcome = ok ? "Invited" : "Failed";
                    result.Detail = ok ? "Invite sent." : "Invite failed (rate-limited or rejected).";
                    if (ok) InvitedCount++; else FailedCount++;

                    // Record in the built-in DB so a future run skips this user.
                    var record = new InvitedUser
                    {
                        UserId = userId,
                        DisplayName = profile.DisplayName,
                        ProfilePicUrl = profile.ProfilePicUrl,
                        IsAgeVerified = profile.IsAgeVerified,
                        TrustLevel = trustLevel,
                        InvitedAt = DateTime.UtcNow,
                        InviteSuccessful = ok
                    };
                    db.InvitedUsers.Add(record);
                    await db.SaveChangesAsync();
                    alreadyInvitedSet.Add(userId);
                }
                catch (Exception ex)
                {
                    result.Outcome = "Failed";
                    result.Detail = $"Error: {ex.Message}";
                    FailedCount++;
                }

                Results.Add(result);
                // Small delay to avoid VRChat rate limiting.
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error during bulk invite: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            SummaryText = $"Done. Invited {InvitedCount}, skipped {SkippedCount}, failed {FailedCount} (of {TotalCount}).";
            StatusText = SummaryText;
        }
    }

    private void MarkSkipped(BulkInviteResult result, string reason)
    {
        result.Outcome = "Skipped";
        result.Detail = reason;
        SkippedCount++;
    }

    private List<string> ParseUserIds()
    {
        if (string.IsNullOrWhiteSpace(UserIdsText))
            return new List<string>();

        return UserIdsText
            .Split(new[] { '\n', '\r', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Trust-level detection, replicated exactly from InstanceInviterViewModel so the
    /// bulk feature and the instance feature classify users identically.
    /// </summary>
    private static string GetTrustLevel(List<string> tags)
    {
        if (tags == null) tags = new List<string>();

        if (tags.Contains("system_trust_legend")) return "Trusted User";
        if (tags.Contains("system_trust_veteran")) return "Trusted User";
        if (tags.Contains("system_trust_trusted")) return "Known User";
        if (tags.Contains("system_trust_known")) return "User";
        if (tags.Contains("system_trust_basic")) return "New User";

        var tagsLower = tags.Select(t => t.ToLowerInvariant()).ToList();
        if (tagsLower.Any(t => t.Contains("legend"))) return "Trusted User";
        if (tagsLower.Any(t => t.Contains("veteran"))) return "Trusted User";
        if (tagsLower.Any(t => t.Contains("trusted"))) return "Known User";
        if (tagsLower.Any(t => t.Contains("known"))) return "User";
        if (tagsLower.Any(t => t.Contains("basic"))) return "New User";

        return "Visitor";
    }

    partial void OnIsRunningChanged(bool value) => CommandManager.InvalidateRequerySuggested();
    partial void OnUserIdsTextChanged(string value) => CommandManager.InvalidateRequerySuggested();
}

/// <summary>Per-user outcome row for the bulk invite results grid.</summary>
public class BulkInviteResult
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePicUrl { get; set; }
    public string TrustLevel { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool InviteSuccessful { get; set; }

    /// <summary>Colored badge text for the grid.</summary>
    public string OutcomeDisplay => Outcome switch
    {
        "Invited" => "✓ Invited",
        "Skipped" => "⏭ Skipped",
        "Failed" => "✗ Failed",
        _ => Outcome
    };
}
