using System.Text.Json;
using System.Timers;
using Timer = System.Timers.Timer;

namespace VRCGroupTools.Services;

public interface IAuditLogService
{
    event EventHandler<List<AuditLogEntry>>? NewLogsReceived;
    event EventHandler<string>? StatusChanged;
    event EventHandler<FetchProgressEventArgs>? FetchProgressChanged;
    
    Task StartPollingAsync(string groupId);
    void StopPolling();
    Task<List<AuditLogEntry>> GetAllLogsAsync();
    Task<List<AuditLogEntry>> SearchLogsAsync(string? searchQuery = null, string? eventType = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<AuditLogEntry>> FetchHistoricalLogsAsync(int maxPages = 100);
    Task RefreshLogsAsync();
    bool IsPolling { get; }
    int TotalLogCount { get; }
}

public class FetchProgressEventArgs : EventArgs
{
    public int PagesFetched { get; set; }
    public int TotalLogsFetched { get; set; }
}

public class AuditLogService : IAuditLogService, IDisposable
{
    private readonly IVRChatApiService _apiService;
    private readonly ICacheService _cacheService;
    private readonly IDiscordWebhookService _discordService;
    private Timer? _pollingTimer;
    private string? _currentGroupId;
    private bool _isPolling;
    private int _totalLogCount;

    public event EventHandler<List<AuditLogEntry>>? NewLogsReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<FetchProgressEventArgs>? FetchProgressChanged;

    public bool IsPolling => _isPolling;
    public int TotalLogCount => _totalLogCount;

    public AuditLogService(IVRChatApiService apiService, ICacheService cacheService, IDiscordWebhookService discordService)
    {
        _apiService = apiService;
        _cacheService = cacheService;
        _discordService = discordService;
    }

    public async Task StartPollingAsync(string groupId)
    {
        Console.WriteLine($"[AUDIT-SVC] StartPollingAsync called for group: {groupId}");
        _currentGroupId = groupId;
        
        // Get count from database
        _totalLogCount = await _cacheService.GetAuditLogCountAsync(groupId);
        Console.WriteLine($"[AUDIT-SVC] Database has {_totalLogCount} cached audit logs for this group");
        StatusChanged?.Invoke(this, $"Loading {_totalLogCount} cached logs from database...");
        
        // Load cached logs and notify UI
        var cachedLogs = await _cacheService.LoadAuditLogsAsync(groupId);
        Console.WriteLine($"[AUDIT-SVC] Loaded {cachedLogs.Count} logs from cache");
        
        if (cachedLogs.Count > 0)
        {
            NewLogsReceived?.Invoke(this, cachedLogs);
            StatusChanged?.Invoke(this, $"✓ Loaded {cachedLogs.Count} cached logs from database");
        }
        else
        {
            StatusChanged?.Invoke(this, "No cached logs. Click 'Fetch History' to download.");
        }

        // Start polling timer (60 seconds)
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(60000); // 60 seconds
        _pollingTimer.Elapsed += async (s, e) => await PollForNewLogsAsync();
        _pollingTimer.AutoReset = true;
        _pollingTimer.Start();
        _isPolling = true;

        // Do an initial poll
        Console.WriteLine("[AUDIT-SVC] Starting initial poll...");
        await PollForNewLogsAsync();
        
        Console.WriteLine("[AUDIT-SVC] Polling timer started (60 second interval)");
        StatusChanged?.Invoke(this, $"▶ Polling active - auto-checking every 60s | Total: {_totalLogCount} logs");
    }

    public void StopPolling()
    {
        Console.WriteLine("[AUDIT-SVC] StopPolling() called");
        _pollingTimer?.Stop();
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isPolling = false;
        Console.WriteLine("[AUDIT-SVC] Polling stopped");
        StatusChanged?.Invoke(this, $"⏸ Polling stopped | Total: {_totalLogCount} logs in database");
    }

    public async Task<List<AuditLogEntry>> GetAllLogsAsync()
    {
        if (string.IsNullOrEmpty(_currentGroupId)) return new List<AuditLogEntry>();
        return await _cacheService.LoadAuditLogsAsync(_currentGroupId);
    }

    public async Task<List<AuditLogEntry>> SearchLogsAsync(
        string? searchQuery = null, 
        string? eventType = null, 
        DateTime? fromDate = null, 
        DateTime? toDate = null)
    {
        if (string.IsNullOrEmpty(_currentGroupId)) return new List<AuditLogEntry>();
        return await _cacheService.SearchAuditLogsAsync(_currentGroupId, searchQuery, eventType, fromDate, toDate);
    }

    public async Task RefreshLogsAsync()
    {
        await PollForNewLogsAsync();
    }

    private async Task PollForNewLogsAsync()
    {
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            Console.WriteLine("[AUDIT-SVC] PollForNewLogsAsync: No group ID set, skipping");
            return;
        }

        try
        {
            Console.WriteLine($"[AUDIT-SVC] Polling VRChat API for new audit logs (group: {_currentGroupId})...");
            StatusChanged?.Invoke(this, "🔍 Checking VRChat API for new audit logs...");

            // Get existing log IDs before fetch
            var existingLogs = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
            var existingIds = new HashSet<string>(existingLogs.Select(l => l.Id));

            var newLogs = await FetchLogsFromApiAsync(100); // Get latest 100
            Console.WriteLine($"[AUDIT-SVC] API returned {newLogs.Count} log entries");
            
            if (newLogs.Count > 0)
            {
                // Find truly new logs (not in existing)
                var trulyNewLogs = newLogs.Where(l => !existingIds.Contains(l.Id)).ToList();
                
                var savedCount = await _cacheService.AppendAuditLogsAsync(_currentGroupId, newLogs);
                _totalLogCount = await _cacheService.GetAuditLogCountAsync(_currentGroupId);
                Console.WriteLine($"[AUDIT-SVC] Saved {savedCount} new entries (duplicates skipped). Total in DB: {_totalLogCount}");
                
                // Send Discord notifications for truly new logs
                if (trulyNewLogs.Count > 0 && _discordService.IsConfigured)
                {
                    Console.WriteLine($"[AUDIT-SVC] Sending {trulyNewLogs.Count} Discord notifications...");
                    foreach (var log in trulyNewLogs.Take(10)) // Limit to 10 to avoid spam
                    {
                        await SendDiscordNotificationAsync(log);
                        await Task.Delay(500); // Rate limit
                    }
                }
                
                // Reload from database and notify
                var allLogs = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
                NewLogsReceived?.Invoke(this, allLogs);
                
                if (savedCount > 0)
                {
                    StatusChanged?.Invoke(this, $"✓ Found {savedCount} new entries | Total: {_totalLogCount} logs");
                }
                else
                {
                    StatusChanged?.Invoke(this, $"✓ Up to date | Total: {_totalLogCount} logs");
                }
            }
            else
            {
                Console.WriteLine("[AUDIT-SVC] API returned 0 logs (may be rate limited or no new logs)");
                StatusChanged?.Invoke(this, $"✓ No new logs from API | Total: {_totalLogCount} logs");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] ERROR polling: {ex.Message}");
            Console.WriteLine($"[AUDIT-SVC] Stack trace: {ex.StackTrace}");
            StatusChanged?.Invoke(this, $"✗ Polling error: {ex.Message}");
        }
    }

    public async Task<List<AuditLogEntry>> FetchHistoricalLogsAsync(int maxPages = 100)
    {
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            Console.WriteLine("[AUDIT-SVC] FetchHistoricalLogsAsync: No group ID set");
            return new List<AuditLogEntry>();
        }

        Console.WriteLine($"[AUDIT-SVC] FetchHistoricalLogsAsync starting (max {maxPages} pages, 100 logs per page)");
        StatusChanged?.Invoke(this, "📥 Starting historical fetch from VRChat API...");

        var allNewLogs = new List<AuditLogEntry>();
        int page = 0;
        int pageSize = 100;
        bool hasMore = true;
        int totalSaved = 0;

        while (hasMore && page < maxPages)
        {
            Console.WriteLine($"[AUDIT-SVC] Fetching page {page + 1}/{maxPages} (offset: {page * pageSize})...");
            StatusChanged?.Invoke(this, $"📥 Fetching page {page + 1}/{maxPages}... ({allNewLogs.Count} fetched, {totalSaved} new)");
            
            // Emit progress
            FetchProgressChanged?.Invoke(this, new FetchProgressEventArgs 
            { 
                PagesFetched = page + 1, 
                TotalLogsFetched = allNewLogs.Count 
            });
            
            var logs = await FetchLogsFromApiAsync(pageSize, page * pageSize);
            Console.WriteLine($"[AUDIT-SVC] Page {page + 1} returned {logs.Count} entries");
            
            if (logs.Count == 0)
            {
                Console.WriteLine("[AUDIT-SVC] Received 0 logs, stopping fetch");
                hasMore = false;
            }
            else
            {
                // Save to database immediately
                var savedCount = await _cacheService.AppendAuditLogsAsync(_currentGroupId, logs);
                totalSaved += savedCount;
                allNewLogs.AddRange(logs);
                Console.WriteLine($"[AUDIT-SVC] Saved {savedCount} new entries from page {page + 1}");
                page++;
                
                // If we got fewer than requested, we've reached the end
                if (logs.Count < pageSize)
                {
                    Console.WriteLine($"[AUDIT-SVC] Received {logs.Count} < {pageSize}, reached end of logs");
                    hasMore = false;
                }
                
                // Rate limit delay - VRChat API prefers slower requests
                // 1 second delay to avoid hitting rate limits
                if (hasMore)
                {
                    Console.WriteLine("[AUDIT-SVC] Rate limiting: waiting 1000ms before next request...");
                    await Task.Delay(1000);
                }
            }
        }

        _totalLogCount = await _cacheService.GetAuditLogCountAsync(_currentGroupId);
        Console.WriteLine($"[AUDIT-SVC] Historical fetch complete: {allNewLogs.Count} fetched from {page} pages, {totalSaved} new entries saved");
        
        // Reload and notify
        var allLogs = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
        NewLogsReceived?.Invoke(this, allLogs);

        StatusChanged?.Invoke(this, $"✓ Complete! Fetched {page} pages, saved {totalSaved} new | Total: {_totalLogCount} logs");
        
        return allNewLogs;
    }

    private async Task<List<AuditLogEntry>> FetchLogsFromApiAsync(int count = 100, int offset = 0)
    {
        var logs = new List<AuditLogEntry>();
        
        try
        {
            Console.WriteLine($"[AUDIT-SVC] API Request: GetGroupAuditLogsAsync(group={_currentGroupId}, count={count}, offset={offset})");
            var response = await _apiService.GetGroupAuditLogsAsync(_currentGroupId!, count, offset);
            
            if (response.HasValue && response.Value.TryGetProperty("results", out var results))
            {
                var resultCount = 0;
                foreach (var entry in results.EnumerateArray())
                {
                    var log = ParseAuditLogEntry(entry);
                    if (log != null)
                    {
                        logs.Add(log);
                        resultCount++;
                    }
                }
                Console.WriteLine($"[AUDIT-SVC] API Response: Parsed {resultCount} log entries");
            }
            else
            {
                Console.WriteLine($"[AUDIT-SVC] API Response: No 'results' property or empty response");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] API ERROR: {ex.Message}");
        }

        return logs;
    }

    private AuditLogEntry? ParseAuditLogEntry(JsonElement entry)
    {
        try
        {
            var eventType = entry.TryGetProperty("eventType", out var et) ? et.GetString() ?? "unknown" : "unknown";
            
            var log = new AuditLogEntry
            {
                Id = entry.TryGetProperty("id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                EventType = eventType,
                CreatedAt = entry.TryGetProperty("created_at", out var createdAt) 
                    ? DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString()) 
                    : DateTime.UtcNow,
                RawData = entry.GetRawText()
            };

            // Parse actor info
            if (entry.TryGetProperty("actorId", out var actorId))
            {
                log.ActorId = actorId.GetString();
            }
            if (entry.TryGetProperty("actorDisplayName", out var actorName))
            {
                log.ActorName = actorName.GetString() ?? "Unknown";
            }

            // Parse target info if exists
            if (entry.TryGetProperty("targetId", out var targetId))
            {
                log.TargetId = targetId.GetString();
            }
            if (entry.TryGetProperty("targetDisplayName", out var targetName))
            {
                log.TargetName = targetName.GetString();
            }

            // Parse description
            if (entry.TryGetProperty("description", out var desc))
            {
                log.Description = desc.GetString() ?? "";
            }

            // Generate a readable description if not provided
            if (string.IsNullOrEmpty(log.Description))
            {
                log.Description = GenerateDescription(log);
            }

            // Set event color
            log.EventColor = GetEventColor(eventType);

            return log;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT] Error parsing entry: {ex.Message}");
            return null;
        }
    }

    private string GenerateDescription(AuditLogEntry log)
    {
        var actor = log.ActorName ?? "Someone";
        var target = log.TargetName ?? "a user";

        return log.EventType switch
        {
            "group.user.join" => $"{target} joined the group",
            "group.user.leave" => $"{target} left the group",
            "group.user.kick" => $"{actor} kicked {target}",
            "group.user.ban" => $"{actor} banned {target}",
            "group.user.unban" => $"{actor} unbanned {target}",
            "group.user.role.add" => $"{actor} added a role to {target}",
            "group.user.role.remove" => $"{actor} removed a role from {target}",
            "group.role.create" => $"{actor} created a new role",
            "group.role.update" => $"{actor} updated a role",
            "group.role.delete" => $"{actor} deleted a role",
            "group.update" => $"{actor} updated group settings",
            "group.announcement.create" => $"{actor} created an announcement",
            "group.announcement.delete" => $"{actor} deleted an announcement",
            "group.invite.create" => $"{actor} invited {target}",
            "group.invite.accept" => $"{target} accepted an invite",
            "group.invite.reject" => $"{target} rejected an invite",
            "group.instance.create" => $"{actor} created a group instance",
            "group.instance.delete" => $"{actor} deleted a group instance",
            "group.gallery.create" => $"{actor} added to gallery",
            "group.gallery.delete" => $"{actor} removed from gallery",
            "group.post.create" => $"{actor} created a post",
            "group.post.delete" => $"{actor} deleted a post",
            _ => $"{actor}: {log.EventType}"
        };
    }

    private static string GetEventColor(string eventType)
    {
        return eventType.ToLower() switch
        {
            var t when t.Contains("join") => "#4CAF50",
            var t when t.Contains("leave") => "#FF9800",
            var t when t.Contains("kick") => "#f44336",
            var t when t.Contains("ban") => "#B71C1C",
            var t when t.Contains("unban") => "#81C784",
            var t when t.Contains("role") => "#2196F3",
            var t when t.Contains("announcement") => "#9C27B0",
            var t when t.Contains("invite") => "#00BCD4",
            _ => "#607D8B"
        };
    }

    private async Task SendDiscordNotificationAsync(AuditLogEntry log)
    {
        try
        {
            if (_discordService is DiscordWebhookService discordSvc)
            {
                await discordSvc.SendAuditEventAsync(
                    log.EventType,
                    log.ActorName ?? "Unknown",
                    log.TargetName,
                    log.Description
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] Discord notification failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}
