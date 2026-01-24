using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;
using Timer = System.Timers.Timer;

namespace VRCGroupTools.ViewModels;

public partial class GroupJoinRequestsViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private Timer? _autoAcceptTimer;

    [ObservableProperty]
    private ObservableCollection<JoinRequestItemViewModel> _joinRequests = new();

    [ObservableProperty]
    private ObservableCollection<JoinRequestItemViewModel> _filteredJoinRequests = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _only18Plus;

    [ObservableProperty]
    private bool _autoAcceptEnabled;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private int _totalRequests;

    [ObservableProperty]
    private int _autoAcceptIntervalSeconds = 30;

    public GroupJoinRequestsViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        ApplyFilters();
    }

    partial void OnIsMonitoringChanged(bool value)
    {
        if (value)
        {
            StartMonitoring();
        }
        else
        {
            StopMonitoring();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadJoinRequestsAsync();
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
    }

    private void StartMonitoring()
    {
        StatusMessage = "Monitoring started";
        
        // Initial load
        _ = LoadJoinRequestsAsync();

        // Setup auto-refresh timer
        _autoAcceptTimer = new Timer(AutoAcceptIntervalSeconds * 1000);
        _autoAcceptTimer.Elapsed += async (s, e) => await LoadJoinRequestsAsync();
        _autoAcceptTimer.Start();
    }

    private void StopMonitoring()
    {
        StatusMessage = "Monitoring stopped";
        
        _autoAcceptTimer?.Stop();
        _autoAcceptTimer?.Dispose();
        _autoAcceptTimer = null;
    }

    private async Task LoadJoinRequestsAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Loading join requests...";

            var groupId = _settingsService.CurrentGroupId;
            
            if (string.IsNullOrEmpty(groupId))
            {
                // Try fallback to Settings.GroupId
                groupId = _settingsService.Settings.GroupId;
            }

            if (string.IsNullOrEmpty(groupId))
            {
                StatusMessage = "No group configured";
                IsLoading = false;
                return;
            }

            var requests = await _apiService.GetGroupJoinRequestsAsync(groupId);

            JoinRequests.Clear();
            foreach (var request in requests)
            {
                JoinRequests.Add(new JoinRequestItemViewModel(request, this));
            }

            TotalRequests = JoinRequests.Count;
            ApplyFilters();

            StatusMessage = $"Loaded {TotalRequests} join requests";

            // Trigger enrichment in background
            _ = EnrichRequestsAsync();

            // Auto-accept if enabled
            if (AutoAcceptEnabled && IsMonitoring)
            {
                await AutoAcceptPendingAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading join requests: {ex.Message}";
            MessageBox.Show($"Failed to load join requests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task EnrichRequestsAsync()
    {
        var requestsToEnrich = JoinRequests.Where(r => !r.IsEnriched).ToList();
        if (requestsToEnrich.Count == 0) return;

        foreach (var req in requestsToEnrich)
        {
            if (!IsMonitoring && !IsLoading) 
            {
                // Basic check effectively
            }

            try 
            {
                var user = await _apiService.GetUserAsync(req.Request.UserId);
                if (user != null)
                {
                    req.UpdatedUserDetails(user);
                    
                    // Re-apply filters to update the view (e.g. for 18+ items popping in)
                    Application.Current.Dispatcher.Invoke(ApplyFilters);
                }
                
                // Be gentle with the API
                await Task.Delay(250); 
            }
            catch 
            {
                // Ignore errors during enrichment
            }
        }
    }

    private void ApplyFilters()
    {
        FilteredJoinRequests.Clear();

        var filtered = JoinRequests.Where(r =>
        {
            // Apply 18+ filter
            if (Only18Plus && !r.Request.IsAgeVerified)
                return false;

            return true;
        });

        foreach (var request in filtered)
        {
            FilteredJoinRequests.Add(request);
        }
    }

    private async Task AutoAcceptPendingAsync()
    {
        var requestsToAccept = FilteredJoinRequests.Where(r => !r.IsProcessing).ToList();
        
        if (requestsToAccept.Count == 0)
            return;

        StatusMessage = $"Auto-accepting {requestsToAccept.Count} requests...";

        foreach (var request in requestsToAccept)
        {
            await AcceptRequestAsync(request);
            await Task.Delay(500); // Rate limiting
        }

        await LoadJoinRequestsAsync();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var request in FilteredJoinRequests)
        {
            request.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var request in JoinRequests)
        {
            request.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = JoinRequests.Count(r => r.IsSelected);
    }

    [RelayCommand]
    private async Task ApproveSelectedAsync()
    {
        var selectedRequests = JoinRequests.Where(r => r.IsSelected).ToList();
        if (selectedRequests.Count == 0)
        {
            MessageBox.Show("Please select at least one request to approve.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;

            foreach (var requestVM in selectedRequests)
            {
                var success = await AcceptRequestAsync(requestVM);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }

                // Small delay to avoid rate limiting
                await Task.Delay(500);
            }

            StatusMessage = $"Approved: {successCount} succeeded, {failCount} failed";
            MessageBox.Show($"Approved {successCount} requests successfully.\n{failCount} failed.", "Approval Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh the list
            await LoadJoinRequestsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error approving requests: {ex.Message}";
            MessageBox.Show($"Failed to approve requests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            UpdateSelectedCount();
        }
    }

    [RelayCommand]
    private async Task RejectSelectedAsync()
    {
        var selectedRequests = JoinRequests.Where(r => r.IsSelected).ToList();
        if (selectedRequests.Count == 0)
        {
            MessageBox.Show("Please select at least one request to reject.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to reject {selectedRequests.Count} join request(s)?", 
            "Confirm Rejection", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;

            var groupId = _settingsService.Settings.GroupId;
            if (string.IsNullOrEmpty(groupId))
            {
                MessageBox.Show("No group configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            foreach (var requestVM in selectedRequests)
            {
                StatusMessage = $"Rejecting {requestVM.Request.DisplayName}...";
                requestVM.IsProcessing = true;

                var success = await _apiService.RespondToGroupJoinRequestAsync(groupId, requestVM.Request.UserId, "reject");

                if (success)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }

                requestVM.IsProcessing = false;

                // Small delay to avoid rate limiting
                await Task.Delay(500);
            }

            StatusMessage = $"Rejected: {successCount} succeeded, {failCount} failed";
            MessageBox.Show($"Rejected {successCount} requests successfully.\n{failCount} failed.", "Rejection Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh the list
            await LoadJoinRequestsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error rejecting requests: {ex.Message}";
            MessageBox.Show($"Failed to reject requests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            UpdateSelectedCount();
        }
    }

    private async Task<bool> AcceptRequestAsync(JoinRequestItemViewModel requestVM)
    {
        var groupId = _settingsService.Settings.GroupId;
        if (string.IsNullOrEmpty(groupId))
            return false;

        StatusMessage = $"Accepting {requestVM.Request.DisplayName}...";
        requestVM.IsProcessing = true;

        var success = await _apiService.RespondToGroupJoinRequestAsync(groupId, requestVM.Request.UserId, "accept");
        
        requestVM.IsProcessing = false;
        return success;
    }

    [RelayCommand]
    private void OpenUserProfile(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        try
        {
            var vm = new UserProfileViewModel(userId, _apiService);
            var window = new VRCGroupTools.Views.UserProfileWindow(vm);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public partial class JoinRequestItemViewModel : ObservableObject
{
    private readonly GroupJoinRequestsViewModel? _parent;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isEnriched;

    public GroupJoinRequest Request { get; }

    // Cache enriched user details
    private UserDetails? _cachedUser;

    public JoinRequestItemViewModel(GroupJoinRequest request, GroupJoinRequestsViewModel? parent)
    {
        Request = request;
        _parent = parent;
        // If we already have tags from somewhere (cached), mark enriched? 
        // No, assume not enriched unless _cachedUser is set or we explicitly flag it.
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent?.UpdateSelectedCount();
    }

    public void UpdatedUserDetails(UserDetails user)
    {
        _cachedUser = user;
        IsEnriched = true;
        
        Request.Tags = user.Tags;
        Request.IsAgeVerified = user.IsAgeVerified; // Sync
        
        OnPropertyChanged(nameof(TrustLevel));
        OnPropertyChanged(nameof(TrustLevelBrush));
        OnPropertyChanged(nameof(Is18Plus));
    }

    public void UpdateTags(List<string> tags)
    {
        Request.Tags = tags;
        OnPropertyChanged(nameof(TrustLevel));
        OnPropertyChanged(nameof(TrustLevelBrush));
    }

    public bool Is18Plus
    {
        get
        {
            if (Request.IsAgeVerified) return true;
            if (Request.Tags != null && Request.Tags.Contains("system_age_verified_adult")) return true;
            return false;
        }
    }

    public string? TrustLevel
    {
        get
        {
            if (Request.Tags == null || Request.Tags.Count == 0)
                return "Visitor";

            if (Request.Tags.Contains("system_trust_legend")) return "Trusted User";
            if (Request.Tags.Contains("system_trust_veteran")) return "Trusted User";
            if (Request.Tags.Contains("system_trust_trusted")) return "Known User";
            if (Request.Tags.Contains("system_trust_known")) return "User";
            if (Request.Tags.Contains("system_trust_basic")) return "New User";

            var tagsLower = Request.Tags.Select(t => t.ToLowerInvariant()).ToList();
            if (tagsLower.Any(t => t.Contains("legend"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("veteran"))) return "Trusted User";
            if (tagsLower.Any(t => t.Contains("trusted"))) return "Known User";
            if (tagsLower.Any(t => t.Contains("known"))) return "User";
            if (tagsLower.Any(t => t.Contains("basic"))) return "New User";

            return "Visitor";
        }
    }

    public Brush TrustLevelBrush
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted User" => new SolidColorBrush(Color.FromRgb(138, 43, 226)),
                "Known User" => new SolidColorBrush(Color.FromRgb(255, 123, 0)),
                "User" => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                "New User" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                "Visitor" => new SolidColorBrush(Color.FromRgb(149, 165, 166)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }
}
