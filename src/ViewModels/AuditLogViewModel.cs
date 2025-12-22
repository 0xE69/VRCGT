using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class AuditLogViewModel : ObservableObject
{
    private readonly IAuditLogService _auditLogService;
    private readonly IVRChatApiService _apiService;
    private List<AuditLogEntry> _allLogs = new();

    [ObservableProperty]
    private ObservableCollection<AuditLogDisplayItem> _filteredLogs = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _selectedEventType = "All Events";

    [ObservableProperty]
    private ObservableCollection<string> _eventTypes = new()
    {
        "All Events",
        "Join/Leave",
        "Kicks & Bans",
        "Role Changes",
        "Group Updates",
        "Announcements",
        "Invites",
        "Instances",
        "Gallery",
        "Posts"
    };

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _statusMessage = "Ready to load audit logs";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isFetchingHistory;

    [ObservableProperty]
    private bool _hasLoadedLogs;  // True once logs have been loaded at least once

    [ObservableProperty]
    private int _totalLogCount;

    [ObservableProperty]
    private int _fetchProgress;  // Number of pages fetched

    [ObservableProperty]
    private string _fetchProgressText = "";  // "Fetched 500 logs (5 pages)..."

    [ObservableProperty]
    private bool _isPollingActive;

    [ObservableProperty]
    private AuditLogDisplayItem? _selectedLog;

    public AuditLogViewModel(IAuditLogService auditLogService, IVRChatApiService apiService)
    {
        _auditLogService = auditLogService;
        _apiService = apiService;

        // Subscribe to events
        _auditLogService.NewLogsReceived += OnNewLogsReceived;
        _auditLogService.StatusChanged += OnStatusChanged;

        // Set default date range (last 30 days)
        EndDate = DateTime.Now;
        StartDate = DateTime.Now.AddDays(-30);
    }

    public async Task InitializeAsync()
    {
        Console.WriteLine("[AUDIT-VM] InitializeAsync() called");
        IsLoading = true;
        StatusMessage = "Initializing audit logs...";
        
        try
        {
            var groupId = _apiService.CurrentGroupId;
            Console.WriteLine($"[AUDIT-VM] Current Group ID: {groupId ?? "(none)"}");
            
            if (!string.IsNullOrEmpty(groupId))
            {
                StatusMessage = $"Loading audit logs for group {groupId}...";
                Console.WriteLine($"[AUDIT-VM] Starting polling for group: {groupId}");
                await _auditLogService.StartPollingAsync(groupId);
                IsPollingActive = _auditLogService.IsPolling;
                Console.WriteLine($"[AUDIT-VM] Polling started: {IsPollingActive}");
            }
            else
            {
                StatusMessage = "⚠ No group selected. Set Group ID on the main page first.";
                Console.WriteLine("[AUDIT-VM] ERROR: No group ID configured");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            Console.WriteLine($"[AUDIT-VM] ERROR: {ex}");
        }
        finally
        {
            IsLoading = false;
            Console.WriteLine($"[AUDIT-VM] InitializeAsync complete. Logs loaded: {_allLogs.Count}");
        }
    }

    private void OnNewLogsReceived(object? sender, List<AuditLogEntry> logs)
    {
        Console.WriteLine($"[AUDIT-VM] OnNewLogsReceived: {logs.Count} logs");
        Application.Current.Dispatcher.Invoke(() =>
        {
            _allLogs = logs;
            TotalLogCount = logs.Count;
            HasLoadedLogs = true;  // Mark that we've loaded at least once
            Console.WriteLine($"[AUDIT-VM] Applying filters to {logs.Count} logs...");
            ApplyFilters();
            Console.WriteLine($"[AUDIT-VM] After filtering: {FilteredLogs.Count} logs displayed");
            Console.WriteLine($"[AUDIT-VM] Visibility state: IsLoading={IsLoading}, IsFetchingHistory={IsFetchingHistory}, HasLoadedLogs={HasLoadedLogs}");
            
            if (logs.Count > 0)
            {
                StatusMessage = $"✓ Showing {FilteredLogs.Count} of {logs.Count} logs";
            }
        });
    }

    private void OnFetchProgressChanged(object? sender, FetchProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            FetchProgress = e.PagesFetched;
            FetchProgressText = $"📥 Fetched {e.TotalLogsFetched} logs ({e.PagesFetched} pages)...";
            StatusMessage = FetchProgressText;
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Console.WriteLine($"[AUDIT-VM] Status: {status}");
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = status;
        });
    }

    partial void OnSearchQueryChanged(string value)
    {
        Console.WriteLine($"[AUDIT-VM] Search query changed: \"{value}\"");
        ApplyFilters();
    }

    partial void OnSelectedEventTypeChanged(string value)
    {
        Console.WriteLine($"[AUDIT-VM] Event type filter changed: {value}");
        ApplyFilters();
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        Console.WriteLine($"[AUDIT-VM] Start date changed: {value?.ToString("yyyy-MM-dd") ?? "(none)"}");
        ApplyFilters();
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        Console.WriteLine($"[AUDIT-VM] End date changed: {value?.ToString("yyyy-MM-dd") ?? "(none)"}");
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allLogs.AsEnumerable();

        // Filter by search query
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(l =>
                (l.ActorName?.ToLowerInvariant().Contains(query) ?? false) ||
                (l.TargetName?.ToLowerInvariant().Contains(query) ?? false) ||
                l.Description.ToLowerInvariant().Contains(query) ||
                l.EventType.ToLowerInvariant().Contains(query));
        }

        // Filter by event type
        if (SelectedEventType != "All Events")
        {
            filtered = SelectedEventType switch
            {
                "Join/Leave" => filtered.Where(l => l.EventType.Contains("join") || l.EventType.Contains("leave")),
                "Kicks & Bans" => filtered.Where(l => l.EventType.Contains("kick") || l.EventType.Contains("ban")),
                "Role Changes" => filtered.Where(l => l.EventType.Contains("role")),
                "Group Updates" => filtered.Where(l => l.EventType == "group.update"),
                "Announcements" => filtered.Where(l => l.EventType.Contains("announcement")),
                "Invites" => filtered.Where(l => l.EventType.Contains("invite")),
                "Instances" => filtered.Where(l => l.EventType.Contains("instance")),
                "Gallery" => filtered.Where(l => l.EventType.Contains("gallery")),
                "Posts" => filtered.Where(l => l.EventType.Contains("post")),
                _ => filtered
            };
        }

        // Filter by date range
        if (StartDate.HasValue)
        {
            filtered = filtered.Where(l => l.CreatedAt >= StartDate.Value);
        }
        if (EndDate.HasValue)
        {
            filtered = filtered.Where(l => l.CreatedAt <= EndDate.Value.AddDays(1));
        }

        // Convert to display items
        var displayItems = filtered
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new AuditLogDisplayItem
            {
                Id = l.Id,
                Timestamp = l.CreatedAt,
                FormattedTime = FormatTime(l.CreatedAt),
                EventType = l.EventType,
                EventIcon = GetEventIcon(l.EventType),
                EventColor = GetEventColor(l.EventType),
                ActorName = l.ActorName ?? "Unknown",
                TargetName = l.TargetName,
                Description = l.Description
            })
            .ToList();

        FilteredLogs.Clear();
        foreach (var item in displayItems)
        {
            FilteredLogs.Add(item);
        }
    }

    private static string FormatTime(DateTime dt)
    {
        var now = DateTime.Now;
        var diff = now - dt;

        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return dt.ToString("MMM dd, yyyy HH:mm");
    }

    private static string GetEventIcon(string eventType)
    {
        return eventType switch
        {
            var t when t.Contains("join") => "➕",
            var t when t.Contains("leave") => "➖",
            var t when t.Contains("kick") => "👢",
            var t when t.Contains("ban") => "🔨",
            var t when t.Contains("unban") => "✅",
            var t when t.Contains("role") => "🏷️",
            var t when t.Contains("update") => "⚙️",
            var t when t.Contains("announcement") => "📢",
            var t when t.Contains("invite") => "✉️",
            var t when t.Contains("instance") => "🌐",
            var t when t.Contains("gallery") => "🖼️",
            var t when t.Contains("post") => "📝",
            _ => "📋"
        };
    }

    private static string GetEventColor(string eventType)
    {
        return eventType switch
        {
            var t when t.Contains("join") => "#4CAF50",
            var t when t.Contains("leave") => "#FF9800",
            var t when t.Contains("kick") => "#F44336",
            var t when t.Contains("ban") => "#D32F2F",
            var t when t.Contains("unban") => "#4CAF50",
            var t when t.Contains("role") => "#9C27B0",
            var t when t.Contains("update") => "#2196F3",
            var t when t.Contains("announcement") => "#FF5722",
            var t when t.Contains("invite") => "#00BCD4",
            var t when t.Contains("instance") => "#3F51B5",
            var t when t.Contains("gallery") => "#E91E63",
            var t when t.Contains("post") => "#795548",
            _ => "#888888"
        };
    }

    [RelayCommand]
    private async Task FetchHistoryAsync()
    {
        if (IsFetchingHistory) return;

        Console.WriteLine("[AUDIT-VM] FetchHistoryAsync() called - fetching historical logs");
        IsFetchingHistory = true;
        FetchProgress = 0;
        FetchProgressText = "Starting fetch...";
        StatusMessage = "📥 Fetching historical logs from VRChat API...";

        try
        {
            Console.WriteLine("[AUDIT-VM] Requesting up to 100 pages of historical logs...");
            // Subscribe to progress updates
            _auditLogService.FetchProgressChanged += OnFetchProgressChanged;
            await _auditLogService.FetchHistoricalLogsAsync(100); // Up to 100 pages
            _auditLogService.FetchProgressChanged -= OnFetchProgressChanged;
            
            Console.WriteLine($"[AUDIT-VM] Historical fetch complete. Total logs: {TotalLogCount}");
            FetchProgressText = "";
            StatusMessage = $"✓ Fetched {TotalLogCount} logs total";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error fetching history: {ex.Message}";
            Console.WriteLine($"[AUDIT-VM] ERROR fetching history: {ex}");
        }
        finally
        {
            IsFetchingHistory = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;

        Console.WriteLine("[AUDIT-VM] RefreshAsync() called");
        IsLoading = true;
        StatusMessage = "🔄 Refreshing audit logs...";

        try
        {
            await _auditLogService.RefreshLogsAsync();
            Console.WriteLine($"[AUDIT-VM] Refresh complete. Total logs: {TotalLogCount}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Refresh error: {ex.Message}";
            Console.WriteLine($"[AUDIT-VM] ERROR refreshing: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Console.WriteLine("[AUDIT-VM] ClearFilters() called");
        SearchQuery = "";
        SelectedEventType = "All Events";
        StartDate = DateTime.Now.AddDays(-30);
        EndDate = DateTime.Now;
        StatusMessage = "Filters cleared";
    }

    [RelayCommand]
    private void TogglePolling()
    {
        Console.WriteLine($"[AUDIT-VM] TogglePolling() called - currently polling: {IsPollingActive}");
        if (IsPollingActive)
        {
            _auditLogService.StopPolling();
            StatusMessage = "⏸ Polling paused";
        }
        else
        {
            var groupId = _apiService.CurrentGroupId;
            Console.WriteLine($"[AUDIT-VM] Starting polling for group: {groupId}");
            if (!string.IsNullOrEmpty(groupId))
            {
                _ = _auditLogService.StartPollingAsync(groupId);
                StatusMessage = "▶ Polling resumed";
            }
            else
            {
                StatusMessage = "⚠ Cannot start polling - no group selected";
            }
        }
        IsPollingActive = _auditLogService.IsPolling;
        Console.WriteLine($"[AUDIT-VM] Polling is now: {(IsPollingActive ? "ACTIVE" : "STOPPED")}");
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        Console.WriteLine($"[AUDIT-VM] ExportLogsAsync() called - {_allLogs.Count} logs to export");
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"audit_logs_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".csv",
                Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                Console.WriteLine($"[AUDIT-VM] Exporting to: {dialog.FileName}");
                StatusMessage = $"Exporting {_allLogs.Count} logs...";
                
                if (dialog.FileName.EndsWith(".json"))
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(_allLogs, 
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
                }
                else
                {
                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("Timestamp,Event Type,Actor,Target,Description");
                    
                    foreach (var log in _allLogs.OrderByDescending(l => l.CreatedAt))
                    {
                        csv.AppendLine($"\"{ log.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{log.EventType}\",\"{log.ActorName ?? ""}\",\"{log.TargetName ?? ""}\",\"{log.Description.Replace("\"", "\"\"")}\"");
                    }
                    
                    await System.IO.File.WriteAllTextAsync(dialog.FileName, csv.ToString());
                }

                StatusMessage = $"✓ Exported {_allLogs.Count} logs to {System.IO.Path.GetFileName(dialog.FileName)}";
                Console.WriteLine($"[AUDIT-VM] Export complete: {dialog.FileName}");
            }
            else
            {
                Console.WriteLine("[AUDIT-VM] Export cancelled by user");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Export error: {ex.Message}";
            Console.WriteLine($"[AUDIT-VM] ERROR exporting: {ex}");
        }
    }
}

public class AuditLogDisplayItem
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string FormattedTime { get; set; } = "";
    public string EventType { get; set; } = "";
    public string EventIcon { get; set; } = "";
    public string EventColor { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string? TargetName { get; set; }
    public string Description { get; set; } = "";
}
