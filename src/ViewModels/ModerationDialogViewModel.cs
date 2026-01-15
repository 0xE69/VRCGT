using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Models;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class ModerationDialogViewModel : ObservableObject
{
    private readonly IModerationService _moderationService;
    private readonly string _groupId;
    private readonly string _currentUserId;
    private readonly string _currentUserDisplayName;

    [ObservableProperty]
    private string _actionType = ""; // "kick", "ban", "warning"

    [ObservableProperty]
    private string _actionTypeDisplay = "";

    [ObservableProperty]
    private string _targetUserId = "";

    [ObservableProperty]
    private string _targetDisplayName = "";

    [ObservableProperty]
    private List<string> _availableReasons = new();

    [ObservableProperty]
    private string? _selectedReason;

    [ObservableProperty]
    private List<ModerationDuration> _availableDurations = new();

    [ObservableProperty]
    private ModerationDuration? _selectedDuration;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private InfractionHistory _infractionHistory = new();

    [ObservableProperty]
    private bool _showDurationPicker;

    [ObservableProperty]
    private bool _showReasonSpecificCounts;

    [ObservableProperty]
    private bool _showExpirationDate;

    [ObservableProperty]
    private string _expirationDateDisplay = "";

    [ObservableProperty]
    private bool _hasRecentActions;

    [ObservableProperty]
    private string _confirmButtonText = "Confirm";

    [ObservableProperty]
    private Color _confirmButtonColor = Colors.Red;

    [ObservableProperty]
    private SolidColorBrush _warningCountColor = new SolidColorBrush(Colors.Gray);

    public bool DialogResult { get; private set; }
    public ModerationActionRequest? Request { get; private set; }

    public ModerationDialogViewModel(
        IModerationService moderationService,
        string groupId,
        string actionType,
        string targetUserId,
        string targetDisplayName,
        string currentUserId,
        string currentUserDisplayName)
    {
        _moderationService = moderationService;
        _groupId = groupId;
        _actionType = actionType.ToLower();
        _targetUserId = targetUserId;
        _targetDisplayName = targetDisplayName;
        _currentUserId = currentUserId;
        _currentUserDisplayName = currentUserDisplayName;

        InitializeDialog();
    }

    private async void InitializeDialog()
    {
        // Set display properties based on action type
        switch (ActionType)
        {
            case "kick":
                ActionTypeDisplay = "Kick";
                AvailableReasons = ModerationReasons.KickReasons.ToList();
                AvailableDurations = ModerationDuration.Durations.ToList();
                ShowDurationPicker = true;
                ConfirmButtonText = "⚠️ Kick User";
                ConfirmButtonColor = Color.FromRgb(255, 152, 0); // Orange
                break;
            case "ban":
                ActionTypeDisplay = "Ban";
                AvailableReasons = ModerationReasons.BanReasons.ToList();
                AvailableDurations = ModerationDuration.Durations.ToList();
                ShowDurationPicker = true;
                ConfirmButtonText = "🔨 Ban User";
                ConfirmButtonColor = Color.FromRgb(244, 67, 54); // Red
                break;
            case "warning":
                ActionTypeDisplay = "Warn";
                AvailableReasons = ModerationReasons.WarningReasons.ToList();
                ShowDurationPicker = false;
                ConfirmButtonText = "⚠️ Issue Warning";
                ConfirmButtonColor = Color.FromRgb(255, 193, 7); // Yellow
                break;
        }

        // Set default selections
        if (AvailableReasons.Count > 0)
        {
            SelectedReason = AvailableReasons[0];
        }

        if (AvailableDurations.Count > 0)
        {
            SelectedDuration = AvailableDurations[0];
        }

        // Load infraction history
        await LoadInfractionHistoryAsync();
    }

    private async Task LoadInfractionHistoryAsync()
    {
        try
        {
            InfractionHistory = await _moderationService.GetInfractionHistoryAsync(_groupId, TargetUserId);
            HasRecentActions = InfractionHistory.RecentActions.Count > 0;

            // Update warning count color
            if (InfractionHistory.WarningsLastMonth >= 3)
            {
                WarningCountColor = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            }
            else if (InfractionHistory.WarningsLastMonth >= 2)
            {
                WarningCountColor = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
            }
            else if (InfractionHistory.WarningsLastMonth >= 1)
            {
                WarningCountColor = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
            }
        }
        catch (Exception ex)
        {
            LoggingService.Error("MODERATION-DIALOG", $"Failed to load infraction history: {ex.Message}");
        }
    }

    partial void OnSelectedReasonChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ShowReasonSpecificCounts = true;
            _ = UpdateReasonSpecificCountsAsync(value);
        }
    }

    private async Task UpdateReasonSpecificCountsAsync(string reason)
    {
        try
        {
            var history = await _moderationService.GetInfractionHistoryAsync(_groupId, TargetUserId, reason);
            InfractionHistory.KicksForReason = history.KicksForReason;
            InfractionHistory.BansForReason = history.BansForReason;
            InfractionHistory.WarningsForReason = history.WarningsForReason;
            OnPropertyChanged(nameof(InfractionHistory));
        }
        catch (Exception ex)
        {
            LoggingService.Error("MODERATION-DIALOG", $"Failed to update reason-specific counts: {ex.Message}");
        }
    }

    partial void OnSelectedDurationChanged(ModerationDuration? value)
    {
        if (value != null)
        {
            UpdateExpirationDisplay(value);
        }
    }

    private void UpdateExpirationDisplay(ModerationDuration duration)
    {
        if (duration.Days > 0)
        {
            var expiresAt = DateTime.UtcNow.AddDays(duration.Days);
            ExpirationDateDisplay = expiresAt.ToString("MMM dd, yyyy HH:mm") + " UTC";
            ShowExpirationDate = true;
        }
        else if (duration.Days == 0)
        {
            ExpirationDateDisplay = "Permanent";
            ShowExpirationDate = true;
        }
        else
        {
            ShowExpirationDate = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseDialog();
    }

    [RelayCommand]
    private void Confirm()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(SelectedReason))
        {
            MessageBox.Show("Please select a reason.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ShowDurationPicker && SelectedDuration == null)
        {
            MessageBox.Show("Please select a duration.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Create request
        Request = new ModerationActionRequest
        {
            ActionType = ActionType,
            TargetUserId = TargetUserId,
            TargetDisplayName = TargetDisplayName,
            Reason = SelectedReason!,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            DurationDays = SelectedDuration?.Days ?? -1,
            AllowsAppeal = SelectedDuration?.AllowsAppeal ?? true,
            IsInstanceAction = false
        };

        DialogResult = true;
        CloseDialog();
    }

    private void CloseDialog()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext == this)
                {
                    window.DialogResult = DialogResult;
                    window.Close();
                    break;
                }
            }
        });
    }
}
