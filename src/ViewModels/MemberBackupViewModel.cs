using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Data.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class MemberBackupViewModel : ObservableObject
{
    private readonly IMemberBackupService _backupService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<BackupViewModel> _backups = new();

    [ObservableProperty]
    private BackupViewModel? _selectedBackup;

    [ObservableProperty]
    private ObservableCollection<MemberBackupItemViewModel> _backupMembers = new();

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isCreatingBackup;

    [ObservableProperty]
    private bool _isRestoringBackup;

    [ObservableProperty]
    private bool _isLoadingBackups;

    [ObservableProperty]
    private bool _isLoadingMembers;

    [ObservableProperty]
    private string _backupDescription = "";

    [ObservableProperty]
    private bool _onlyRestoreMissing = true;

    [ObservableProperty]
    private int _progressCurrent;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    private bool _showProgress;

    [ObservableProperty]
    private ComparisonResultViewModel? _comparisonResult;

    public MemberBackupViewModel(IMemberBackupService backupService, ISettingsService settingsService)
    {
        _backupService = backupService;
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        await LoadBackupsAsync();
    }

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        try
        {
            IsLoadingBackups = true;
            Backups.Clear();
            StatusMessage = "Loading backups...";

            var groupId = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
            if (string.IsNullOrEmpty(groupId))
            {
                StatusMessage = "⚠ Please set a Group ID first";
                return;
            }

            var backups = await _backupService.GetBackupsAsync(groupId);

            foreach (var backup in backups)
            {
                Backups.Add(new BackupViewModel(backup));
            }

            StatusMessage = backups.Count > 0 
                ? $"Loaded {backups.Count} backup(s)" 
                : "No backups found. Create your first backup!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to load backups: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to load backups");
        }
        finally
        {
            IsLoadingBackups = false;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        LoggingService.Info("BACKUP-VM", "CreateBackupAsync called");
        try
        {
            IsCreatingBackup = true;
            ShowProgress = true;
            ProgressCurrent = 0;
            ProgressTotal = 100;
            StatusMessage = "Creating backup...";

            var groupId = _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;
            LoggingService.Info("BACKUP-VM", $"Group ID: {groupId ?? "NULL"}");
            
            if (string.IsNullOrEmpty(groupId))
            {
                StatusMessage = "⚠ Please set a Group ID first";
                LoggingService.Warn("BACKUP-VM", "No group ID set");
                return;
            }

            var description = string.IsNullOrWhiteSpace(BackupDescription) ? null : BackupDescription;
            LoggingService.Info("BACKUP-VM", $"Starting backup with description: {description ?? "none"}");

            var result = await _backupService.CreateBackupAsync(groupId, description, (current, total) =>
            {
                ProgressCurrent = current;
                ProgressTotal = total;
            });

            LoggingService.Info("BACKUP-VM", $"Backup result: Success={result.Success}, MemberCount={result.MemberCount}");
            
            if (result.Success)
            {
                StatusMessage = $"✓ Backup created successfully! ({result.MemberCount} members)";
                BackupDescription = "";
                await LoadBackupsAsync();
            }
            else
            {
                StatusMessage = $"✗ Failed to create backup: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to create backup: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to create backup");
        }
        finally
        {
            IsCreatingBackup = false;
            ShowProgress = false;
        }
    }

    [RelayCommand]
    private async Task LoadBackupMembersAsync(BackupViewModel? backup)
    {
        if (backup == null) return;

        try
        {
            IsLoadingMembers = true;
            BackupMembers.Clear();
            SelectedBackup = backup;
            ComparisonResult = null;

            var members = await _backupService.GetBackupMembersAsync(backup.BackupId);

            foreach (var member in members)
            {
                BackupMembers.Add(new MemberBackupItemViewModel(member));
            }

            StatusMessage = $"Loaded {members.Count} members from backup";

            // Automatically compare with current
            await CompareWithCurrentAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to load backup members: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to load backup members");
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    [RelayCommand]
    private async Task CompareWithCurrentAsync()
    {
        if (SelectedBackup == null) return;

        try
        {
            StatusMessage = "Comparing with current members...";

            var comparison = await _backupService.CompareBackupWithCurrentAsync(SelectedBackup.BackupId);

            ComparisonResult = new ComparisonResultViewModel
            {
                BackupMemberCount = comparison.BackupMemberCount,
                CurrentMemberCount = comparison.CurrentMemberCount,
                MissingMemberCount = comparison.MissingMembers.Count,
                NewMemberCount = comparison.NewMembers.Count
            };

            // Update member statuses
            var missingIds = comparison.MissingMembers.Select(m => m.UserId).ToHashSet();
            foreach (var member in BackupMembers)
            {
                member.IsMissing = missingIds.Contains(member.UserId);
            }

            StatusMessage = $"Comparison complete: {comparison.MissingMembers.Count} missing, {comparison.NewMembers.Count} new";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to compare: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to compare backup");
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup == null) return;

        try
        {
            IsRestoringBackup = true;
            ShowProgress = true;
            ProgressCurrent = 0;
            ProgressTotal = BackupMembers.Count;

            var mode = OnlyRestoreMissing ? "missing members" : "all members";
            StatusMessage = $"Restoring {mode}...";

            var result = await _backupService.RestoreMembersAsync(
                SelectedBackup.BackupId,
                OnlyRestoreMissing,
                (current, total) =>
                {
                    ProgressCurrent = current;
                    ProgressTotal = total;
                });

            if (result.Success)
            {
                var summary = $"✓ Restore complete!\n" +
                             $"• Invites sent: {result.InvitesSent}\n" +
                             $"• Already members: {result.AlreadyMembers}\n" +
                             $"• Failed: {result.Failed}";

                if (result.Errors.Count > 0)
                {
                    summary += $"\n\nErrors:\n{string.Join("\n", result.Errors.Take(5))}";
                    if (result.Errors.Count > 5)
                    {
                        summary += $"\n...and {result.Errors.Count - 5} more";
                    }
                }

                StatusMessage = summary;

                // Refresh comparison
                await CompareWithCurrentAsync();
            }
            else
            {
                StatusMessage = $"✗ Restore failed:\n{string.Join("\n", result.Errors)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to restore: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to restore backup");
        }
        finally
        {
            IsRestoringBackup = false;
            ShowProgress = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupViewModel? backup)
    {
        if (backup == null) return;

        try
        {
            StatusMessage = "Deleting backup...";

            var success = await _backupService.DeleteBackupAsync(backup.BackupId);

            if (success)
            {
                StatusMessage = "✓ Backup deleted";
                Backups.Remove(backup);

                if (SelectedBackup?.BackupId == backup.BackupId)
                {
                    SelectedBackup = null;
                    BackupMembers.Clear();
                    ComparisonResult = null;
                }
            }
            else
            {
                StatusMessage = "✗ Failed to delete backup";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to delete backup: {ex.Message}";
            LoggingService.Error("BACKUP-UI", ex, "Failed to delete backup");
        }
    }
}

public partial class BackupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _backupId;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private int _memberCount;

    [ObservableProperty]
    private string? _description;

    public string DisplayText => string.IsNullOrEmpty(Description)
        ? $"{CreatedAt:yyyy-MM-dd HH:mm:ss} ({MemberCount} members)"
        : $"{Description} - {CreatedAt:yyyy-MM-dd HH:mm:ss} ({MemberCount} members)";

    public BackupViewModel(BackupInfo backup)
    {
        BackupId = backup.BackupId;
        CreatedAt = backup.CreatedAt;
        MemberCount = backup.MemberCount;
        Description = backup.Description;
    }
}

public partial class MemberBackupItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userId;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string? _profilePicUrl;

    [ObservableProperty]
    private string? _roleNames;

    [ObservableProperty]
    private DateTime? _joinedAt;

    [ObservableProperty]
    private bool _wasReInvited;

    [ObservableProperty]
    private bool _isMissing;

    public MemberBackupItemViewModel(MemberBackupEntity member)
    {
        UserId = member.UserId;
        DisplayName = member.DisplayName;
        ProfilePicUrl = member.ProfilePicUrl;
        RoleNames = member.RoleNames;
        JoinedAt = member.JoinedAt;
        WasReInvited = member.WasReInvited;
    }
}

public partial class ComparisonResultViewModel : ObservableObject
{
    [ObservableProperty]
    private int _backupMemberCount;

    [ObservableProperty]
    private int _currentMemberCount;

    [ObservableProperty]
    private int _missingMemberCount;

    [ObservableProperty]
    private int _newMemberCount;

    public int Difference => CurrentMemberCount - BackupMemberCount;
}
