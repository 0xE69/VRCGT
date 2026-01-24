using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Timers;
using System.Linq;
using VRCGroupTools.Data.Models;
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
    private readonly ISecurityMonitorService? _securityMonitor;
    private readonly ISettingsService _settingsService;
    private Timer? _pollingTimer;
    private string? _currentGroupId;
    private bool _isPolling;
    private int _totalLogCount;
    private bool _isSendingUnsentDiscordLogs;

    public event EventHandler<List<AuditLogEntry>>? NewLogsReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<FetchProgressEventArgs>? FetchProgressChanged;

    public bool IsPolling => _isPolling;
    public int TotalLogCount => _totalLogCount;

    public AuditLogService(IVRChatApiService apiService, ICacheService cacheService, IDiscordWebhookService discordService, ISettingsService settingsService, ISecurityMonitorService? securityMonitor = null)
    {
        _apiService = apiService;
        _cacheService = cacheService;
        _discordService = discordService;
        _settingsService = settingsService;
        _securityMonitor = securityMonitor;
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

        // Don't send unsent logs on initial load - only send newly fetched logs going forward
        // This prevents spamming Discord with old logs when app starts

        // Start polling timer with configurable interval
        var pollingIntervalMs = _settingsService.Settings.AuditLogPollingIntervalSeconds * 1000;
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(pollingIntervalMs);
        _pollingTimer.Elapsed += async (s, e) => await PollForNewLogsAsync();
        _pollingTimer.AutoReset = true;
        _pollingTimer.Start();
        _isPolling = true;

        // Do an initial poll
        Console.WriteLine("[AUDIT-SVC] Starting initial poll...");
        await PollForNewLogsAsync();
        
        Console.WriteLine($"[AUDIT-SVC] Polling timer started ({_settingsService.Settings.AuditLogPollingIntervalSeconds} second interval)");
        StatusChanged?.Invoke(this, $"▶ Polling active - auto-checking every {_settingsService.Settings.AuditLogPollingIntervalSeconds}s | Total: {_totalLogCount} logs");
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
                // Find truly new logs (not in existing) AND not older than configured max age
                var maxAgeMinutes = _settingsService.Settings.AuditLogDiscordNotificationMaxAgeMinutes;
                var cutoffTime = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);
                var trulyNewLogs = newLogs.Where(l => !existingIds.Contains(l.Id) && l.CreatedAt >= cutoffTime).ToList();
                Console.WriteLine($"[AUDIT-SVC] Found {trulyNewLogs.Count} new logs within last {maxAgeMinutes} minutes (filtered from {newLogs.Where(l => !existingIds.Contains(l.Id)).Count()} total new logs)");
                
                var savedCount = await _cacheService.AppendAuditLogsAsync(_currentGroupId, newLogs);
                _totalLogCount = await _cacheService.GetAuditLogCountAsync(_currentGroupId);
                Console.WriteLine($"[AUDIT-SVC] Saved {savedCount} new entries (duplicates skipped). Total in DB: {_totalLogCount}");
                
                // Send Discord notifications for truly new logs
                Console.WriteLine($"[AUDIT-SVC] Truly new logs count: {trulyNewLogs.Count}, Discord configured: {_discordService.IsConfigured}");
                
                if (trulyNewLogs.Count > 0 && _discordService.IsConfigured)
                {
                    Console.WriteLine($"[AUDIT-SVC] Sending {trulyNewLogs.Count} Discord notifications...");
                    int sent = 0;
                    var sentLogIds = new List<string>();
                    foreach (var log in trulyNewLogs) // Send all new logs
                    {
                        Console.WriteLine($"[AUDIT-SVC] Processing log {sent + 1}/{trulyNewLogs.Count}: EventType={log.EventType}, Actor={log.ActorName}");
                        var success = await SendDiscordNotificationAsync(log);
                        if (success)
                        {
                            sentLogIds.Add(log.Id);
                        }
                        sent++;
                        await Task.Delay(500); // Rate limit
                    }
                    
                    // Mark successfully sent logs
                    if (sentLogIds.Count > 0)
                    {
                        await _cacheService.MarkLogsAsSentToDiscordAsync(sentLogIds);
                        Console.WriteLine($"[AUDIT-SVC] Marked {sentLogIds.Count} logs as sent to Discord");
                    }
                    
                    Console.WriteLine($"[AUDIT-SVC] Completed sending {sent} Discord notifications");
                }
                else if (trulyNewLogs.Count > 0 && !_discordService.IsConfigured)
                {
                    Console.WriteLine($"[AUDIT-SVC] Discord webhook not configured - skipping {trulyNewLogs.Count} notifications");
                }
                else if (trulyNewLogs.Count == 0)
                {
                    Console.WriteLine($"[AUDIT-SVC] No truly new logs to send to Discord");
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
            var rawJson = entry.GetRawText();
            var idValue = entry.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(idValue))
            {
                idValue = ComputeAuditLogId(rawJson);
            }
            
            var log = new AuditLogEntry
            {
                Id = idValue,
                EventType = eventType,
                CreatedAt = entry.TryGetProperty("created_at", out var createdAt) 
                    ? DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString()) 
                    : DateTime.UtcNow,
                RawData = rawJson
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

            // Track action in security monitor if available
            if (_securityMonitor != null && !string.IsNullOrEmpty(_currentGroupId))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TrackSecurityActionAsync(log);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AUDIT] Error tracking security action: {ex.Message}");
                    }
                });
            }

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
            "group.user.join_request" => $"{target} requested to join the group",
            "group.joinRequest" => $"{target} requested to join the group",
            "group.role.create" => $"{actor} created a new role",
            "group.role.update" => $"{actor} updated a role",
            "group.role.delete" => $"{actor} deleted a role",
            "group.update" => $"{actor} updated group settings",
            "group.announcement.create" => $"{actor} created an announcement",
            "group.announcement.delete" => $"{actor} deleted an announcement",
            "group.invite.create" => $"{actor} invited {target}",
            "group.user.invite" => $"{actor} invited {target}",
            "group.invite.accept" => $"{target} accepted an invite",
            "group.invite.reject" => $"{target} rejected an invite",
            "group.instance.create" => $"{actor} created a group instance",
            "group.instance.delete" => $"{actor} deleted a group instance",
            "group.instance.warn" => $"{actor} issued an instance warning for {target}",
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

    private async Task<bool> SendDiscordNotificationAsync(AuditLogEntry log)
    {
        try
        {
            if (_discordService is DiscordWebhookService discordSvc)
            {
                var success = await discordSvc.SendAuditEventAsync(
                    log.EventType,
                    log.ActorName ?? "Unknown",
                    log.TargetName,
                    log.Description
                );
                return success;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] Discord notification failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SendDiscordNotificationAsync(AuditLogEntity log)
    {
        try
        {
            if (_discordService is DiscordWebhookService discordSvc)
            {
                var success = await discordSvc.SendAuditEventAsync(
                    log.EventType,
                    log.ActorName ?? "Unknown",
                    log.TargetName,
                    log.Description
                );
                return success;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] Discord notification failed: {ex.Message}");
            return false;
        }
    }

    private async Task SendUnsentDiscordLogsAsync()
    {
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            Console.WriteLine("[AUDIT-SVC] SendUnsentDiscordLogsAsync: No group ID set");
            return;
        }

        if (_isSendingUnsentDiscordLogs)
        {
            Console.WriteLine("[AUDIT-SVC] SendUnsentDiscordLogsAsync: already running, skipping");
            return;
        }

        _isSendingUnsentDiscordLogs = true;

        try
        {
            Console.WriteLine("[AUDIT-SVC] Checking for unsent Discord logs...");
            var unsentLogs = await _cacheService.GetUnsentDiscordLogsAsync(_currentGroupId, 100);
            
            if (unsentLogs.Count == 0)
            {
                Console.WriteLine("[AUDIT-SVC] No unsent Discord logs found");
                return;
            }

            var groupedLogs = unsentLogs
                .GroupBy(BuildDiscordDedupKey)
                .ToList();

            Console.WriteLine($"[AUDIT-SVC] Found {unsentLogs.Count} unsent logs ({groupedLogs.Count} unique). Sending to Discord...");
            StatusChanged?.Invoke(this, $"📤 Sending {groupedLogs.Count} pending Discord notifications...");
            
            int sent = 0;
            int skipped = 0;
            var sentLogIds = new List<string>();

            foreach (var group in groupedLogs.OrderBy(g => g.Min(l => l.CreatedAt)))
            {
                var log = group.OrderBy(l => l.CreatedAt).First();
                var groupIds = group.Select(l => l.Id).ToList();

                if (_discordService is DiscordWebhookService discordSvc && !discordSvc.ShouldSendAuditEvent(log.EventType))
                {
                    sentLogIds.AddRange(groupIds);
                    skipped++;
                    Console.WriteLine($"[AUDIT-SVC] Skipping disabled event type '{log.EventType}' for {groupIds.Count} log(s)");
                    continue;
                }

                Console.WriteLine($"[AUDIT-SVC] Sending unsent log {sent + 1}/{groupedLogs.Count}: {log.EventType} - {log.ActorName}");
                var success = await SendDiscordNotificationAsync(log);

                if (success)
                {
                    sentLogIds.AddRange(groupIds);
                    sent++;
                }

                await Task.Delay(500); // Rate limit - 500ms between messages
            }
            
            // Mark successfully sent logs
            if (sentLogIds.Count > 0)
            {
                await _cacheService.MarkLogsAsSentToDiscordAsync(sentLogIds);
                Console.WriteLine($"[AUDIT-SVC] Successfully sent and marked {sentLogIds.Count} logs");
                var sentMessage = skipped > 0
                    ? $"✓ Sent {sent} notifications (skipped {skipped} disabled)"
                    : $"✓ Sent {sent} pending Discord notifications";
                StatusChanged?.Invoke(this, sentMessage);
            }
            
            if (sent < groupedLogs.Count)
            {
                Console.WriteLine($"[AUDIT-SVC] Warning: Only {sent}/{groupedLogs.Count} logs were successfully sent");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] Error sending unsent Discord logs: {ex.Message}");
            StatusChanged?.Invoke(this, $"✗ Error sending pending notifications: {ex.Message}");
        }
        finally
        {
            _isSendingUnsentDiscordLogs = false;
        }
    }

    private static string ComputeAuditLogId(string rawJson)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(rawJson);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static string BuildDiscordDedupKey(AuditLogEntry log)
    {
        if (!string.IsNullOrWhiteSpace(log.RawData))
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(log.RawData);
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        return string.Join("|", new[]
        {
            log.EventType,
            log.ActorId ?? string.Empty,
            log.TargetId ?? string.Empty,
            log.Description ?? string.Empty,
            log.CreatedAt.ToUniversalTime().ToString("o")
        });
    }

    private static string BuildDiscordDedupKey(AuditLogEntity log)
    {
        if (!string.IsNullOrWhiteSpace(log.RawData))
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(log.RawData);
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        return string.Join("|", new[]
        {
            log.EventType,
            log.ActorId ?? string.Empty,
            log.TargetId ?? string.Empty,
            log.Description ?? string.Empty,
            log.CreatedAt.ToUniversalTime().ToString("o")
        });
    }

    public void Dispose()
    {
        StopPolling();
    }

    private async Task TrackSecurityActionAsync(AuditLogEntry log)
    {
        if (_securityMonitor == null || string.IsNullOrEmpty(_currentGroupId))
            return;

        // Only track actions that have an actor (someone performed the action)
        if (string.IsNullOrEmpty(log.ActorId))
            return;

        // Check if this is an instance-specific action by looking for instanceId in RawData
        bool isInstanceAction = false;
        if (!string.IsNullOrEmpty(log.RawData))
        {
            try
            {
                var additionalData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(log.RawData);
                isInstanceAction = additionalData?.ContainsKey("instanceId") == true;
            }
            catch { /* Ignore parse errors */ }
        }

        // Map audit log event types to security action types, with instance vs group distinction
        var actionType = log.EventType.ToLower() switch
        {
            "group.user.kick" => isInstanceAction ? "instance_kick" : "group_kick",
            "group.user.ban" => isInstanceAction ? "instance_ban" : "group_ban",
            "group.user.role.remove" or "group.role.remove" => "role_remove",
            "group.invite.reject" => "invite_reject",
            "group.post.delete" => "post_delete",
            "group.announcement.delete" => "post_delete",
            "group.gallery.delete" => "post_delete",
            _ => null
        };

        if (actionType != null)
        {
            await _securityMonitor.TrackActionAsync(
                _currentGroupId,
                log.ActorId,
                log.ActorName ?? "Unknown",
                actionType,
                log.TargetId,
                log.TargetName,
                new { EventType = log.EventType, Timestamp = log.CreatedAt, IsInstanceAction = isInstanceAction }
            );
        }
    }
}
