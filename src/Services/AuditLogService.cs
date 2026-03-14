using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Timers;
using VRCGroupTools.Data;
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

/// <summary>
/// Optional cache-level pending invite store.
/// If your cache layer implements this, AuditLogService will use it.
/// Otherwise it can fall back to IDatabaseService (if provided) or in-memory.
/// </summary>
public interface IPendingInviteCacheService
{
    Task UpsertPendingInviteAsync(string groupId, string? targetUserId, string? targetDisplayName, string inviteLogId, DateTime invitedAtUtc);

    /// <summary>
    /// Consume (remove) a pending invite if it exists for this user/name, returning invitedAt time.
    /// </summary>
    Task<(bool found, DateTime invitedAtUtc)> TryConsumePendingInviteAsync(string groupId, string? targetUserId, string? targetDisplayName, string joinLogId, DateTime joinedAtUtc);
}

public class AuditLogService : IAuditLogService, IDisposable
{
    private readonly IVRChatApiService _apiService;
    private readonly ICacheService _cacheService;
    private readonly IDiscordWebhookService _discordService;
    private readonly ISecurityMonitorService? _securityMonitor;
    private readonly ISettingsService _settingsService;

    // Optional direct DB service (for pending invites persistence)
    private readonly IDatabaseService? _databaseService;

    private Timer? _pollingTimer;
    private string? _currentGroupId;
    private bool _isPolling;
    private int _totalLogCount;
    private bool _isSendingUnsentDiscordLogs;

    // Prevent overlapping polls (timer can tick while previous poll is still running)
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    // Only apply max-age filter on first poll to prevent startup backlog spam
    private bool _initialPollComplete = false;

    // In-memory fallback pending-invite store (used until DB-backed store is available)
    private readonly Dictionary<string, DateTime> _pendingInviteByUserId = new();
    private readonly Dictionary<string, DateTime> _pendingInviteByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PendingInviteTtl = TimeSpan.FromDays(7);

    public event EventHandler<List<AuditLogEntry>>? NewLogsReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<FetchProgressEventArgs>? FetchProgressChanged;

    public bool IsPolling => _isPolling;
    public int TotalLogCount => _totalLogCount;

    // ✅ Original constructor (kept) — does not require IDatabaseService
    public AuditLogService(
        IVRChatApiService apiService,
        ICacheService cacheService,
        IDiscordWebhookService discordService,
        ISettingsService settingsService,
        ISecurityMonitorService? securityMonitor = null)
    {
        _apiService = apiService;
        _cacheService = cacheService;
        _discordService = discordService;
        _settingsService = settingsService;
        _securityMonitor = securityMonitor;
        _databaseService = null;
    }

    // ✅ New overload — DI will use this if IDatabaseService is registered
    public AuditLogService(
        IVRChatApiService apiService,
        ICacheService cacheService,
        IDiscordWebhookService discordService,
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ISecurityMonitorService? securityMonitor = null)
    {
        _apiService = apiService;
        _cacheService = cacheService;
        _discordService = discordService;
        _settingsService = settingsService;
        _securityMonitor = securityMonitor;
        _databaseService = databaseService;
    }

    public async Task StartPollingAsync(string groupId)
    {
        Console.WriteLine($"[AUDIT-SVC] StartPollingAsync called for group: {groupId}");
        _currentGroupId = groupId;

        _totalLogCount = await _cacheService.GetAuditLogCountAsync(groupId);
        Console.WriteLine($"[AUDIT-SVC] Database has {_totalLogCount} cached audit logs for this group");
        StatusChanged?.Invoke(this, $"Loading {_totalLogCount} cached logs from database...");

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

        var pollingIntervalMs = _settingsService.Settings.AuditLogPollingIntervalSeconds * 1000;
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(pollingIntervalMs);
        _pollingTimer.Elapsed += async (s, e) => await PollForNewLogsAsync();
        _pollingTimer.AutoReset = true;
        _pollingTimer.Start();
        _isPolling = true;

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

    public async Task<List<AuditLogEntry>> SearchLogsAsync(string? searchQuery = null, string? eventType = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        if (string.IsNullOrEmpty(_currentGroupId)) return new List<AuditLogEntry>();
        return await _cacheService.SearchAuditLogsAsync(_currentGroupId, searchQuery, eventType, fromDate, toDate);
    }

    public async Task RefreshLogsAsync()
    {
        await PollForNewLogsAsync();
    }

    private async Task PrunePendingInvitesAsync()
    {
        // Clean in-memory
        var cutoff = DateTime.UtcNow - PendingInviteTtl;

        foreach (var key in _pendingInviteByUserId.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            _pendingInviteByUserId.Remove(key);

        foreach (var key in _pendingInviteByName.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            _pendingInviteByName.Remove(key);

        // Clean DB-backed pending invites (if available)
        if (_databaseService != null)
        {
            try
            {
                await _databaseService.CleanupExpiredPendingInvitesAsync(cutoff);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AUDIT-SVC] Pending invite cleanup failed: {ex.Message}");
            }
        }
    }

    private async Task TrackPendingInviteAsync(AuditLogEntry log)
    {
        if (_currentGroupId == null) return;

        if (!string.Equals(log.EventType, "group.invite.create", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(log.EventType, "group.user.invite", StringComparison.OrdinalIgnoreCase))
            return;

        // 1) Cache-layer store (if implemented)
        if (_cacheService is IPendingInviteCacheService store)
        {
            await store.UpsertPendingInviteAsync(_currentGroupId, log.TargetId, log.TargetName, log.Id, log.CreatedAt);
            return;
        }

        // 2) Direct DB store (if available)
        if (_databaseService != null)
        {
            await _databaseService.UpsertPendingInviteAsync(_currentGroupId, log.TargetId, log.TargetName, log.Id, log.CreatedAt);
            return;
        }

        // 3) In-memory fallback
        if (!string.IsNullOrWhiteSpace(log.TargetId))
            _pendingInviteByUserId[log.TargetId] = log.CreatedAt;

        if (!string.IsNullOrWhiteSpace(log.TargetName))
            _pendingInviteByName[log.TargetName] = log.CreatedAt;
    }

    private async Task<(bool found, DateTime invitedAtUtc)> TryConsumePendingInviteAsync(AuditLogEntry joinLog)
    {
        if (_currentGroupId == null) return (false, default);

        // 1) Cache-layer store (if implemented)
        if (_cacheService is IPendingInviteCacheService store)
            return await store.TryConsumePendingInviteAsync(_currentGroupId, joinLog.TargetId, joinLog.TargetName, joinLog.Id, joinLog.CreatedAt);

        // 2) Direct DB store (if available)
        if (_databaseService != null)
        {
            var (found, invitedAt) = await _databaseService.TryConsumePendingInviteAsync(_currentGroupId, joinLog.TargetId, joinLog.TargetName);
            return (found, invitedAt ?? default);
        }

        // 3) In-memory fallback
        if (!string.IsNullOrWhiteSpace(joinLog.TargetId) && _pendingInviteByUserId.TryGetValue(joinLog.TargetId, out var t1))
        {
            _pendingInviteByUserId.Remove(joinLog.TargetId);
            return (true, t1);
        }

        if (!string.IsNullOrWhiteSpace(joinLog.TargetName) && _pendingInviteByName.TryGetValue(joinLog.TargetName, out var t2))
        {
            _pendingInviteByName.Remove(joinLog.TargetName);
            return (true, t2);
        }

        return (false, default);
    }

    private static string ComputeSyntheticId(string seed)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(seed);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static AuditLogEntry CreateInviteAcceptedDerivedLog(string groupId, AuditLogEntry joinLog, DateTime invitedAtUtc)
    {
        var whoName = joinLog.TargetName ?? "User";
        var whoId = joinLog.TargetId ?? "";

        var seed = $"derived|invite.accept|{groupId}|{whoId}|{whoName}|{invitedAtUtc:o}|{joinLog.Id}";
        var derivedId = ComputeSyntheticId(seed);

        var derivedRaw = JsonSerializer.Serialize(new
        {
            derived = true,
            derivedType = "group.invite.accept",
            groupId,
            invitedAtUtc,
            joinedAtUtc = joinLog.CreatedAt,
            joinLogId = joinLog.Id,
            targetId = joinLog.TargetId,
            targetName = joinLog.TargetName
        });

        return new AuditLogEntry
        {
            Id = derivedId,
            EventType = "group.invite.accept",
            CreatedAt = joinLog.CreatedAt,
            ActorId = joinLog.TargetId,
            ActorName = whoName,
            TargetId = null,
            TargetName = null,
            Description = $"Accepted a group invite (invite sent {invitedAtUtc:u})",
            RawData = derivedRaw,
            EventColor = joinLog.EventColor
        };
    }

    private async Task PollForNewLogsAsync()
    {
        if (string.IsNullOrEmpty(_currentGroupId))
        {
            Console.WriteLine("[AUDIT-SVC] PollForNewLogsAsync: No group ID set, skipping");
            return;
        }

        if (!await _pollLock.WaitAsync(0))
        {
            Console.WriteLine("[AUDIT-SVC] Poll already running, skipping this tick");
            return;
        }

        try
        {
            Console.WriteLine($"[AUDIT-SVC] Polling VRChat API for new audit logs (group: {_currentGroupId})...");
            StatusChanged?.Invoke(this, "🔍 Checking VRChat API for new audit logs...");

            await PrunePendingInvitesAsync();

            var existingLogs = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
            var existingIds = new HashSet<string>(existingLogs.Select(l => l.Id));

            var newLogs = await FetchLogsFromApiAsync(100);
            Console.WriteLine($"[AUDIT-SVC] API returned {newLogs.Count} log entries");

            if (newLogs.Count > 0)
            {
                foreach (var l in newLogs)
                    l.EventType = NormalizeEventType(l.EventType);

                var newUnique = newLogs.Where(l => !existingIds.Contains(l.Id)).ToList();

                List<AuditLogEntry> sendCandidates;
                if (!_initialPollComplete)
                {
                    var maxAgeMinutes = _settingsService.Settings.AuditLogDiscordNotificationMaxAgeMinutes;
                    var cutoffTime = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);
                    sendCandidates = newUnique.Where(l => l.CreatedAt >= cutoffTime).ToList();

                    Console.WriteLine($"[AUDIT-SVC] Initial poll: {sendCandidates.Count} within last {maxAgeMinutes} minutes (filtered from {newUnique.Count} new unique logs)");
                }
                else
                {
                    sendCandidates = newUnique;
                    Console.WriteLine($"[AUDIT-SVC] New unique logs (not in DB): {newUnique.Count}");
                }

                // Track invite sends (pending invites)
                foreach (var log in newUnique)
                    await TrackPendingInviteAsync(log);

                // Build derived logs (inferred invite accept) and send list
                var derivedLogs = new List<AuditLogEntry>();
                var orderedToSend = new List<AuditLogEntry>();

                foreach (var log in sendCandidates.OrderBy(l => l.CreatedAt))
                {
                    if (string.Equals(log.EventType, "group.user.join", StringComparison.OrdinalIgnoreCase))
                    {
                        var (found, invitedAt) = await TryConsumePendingInviteAsync(log);
                        if (found)
                        {
                            var derived = CreateInviteAcceptedDerivedLog(_currentGroupId, log, invitedAt);
                            derivedLogs.Add(derived);
                            orderedToSend.Add(derived); // send accept before join
                        }
                    }

                    orderedToSend.Add(log);
                }

                // Persist both real logs and derived logs together
                var allToPersist = derivedLogs.Count > 0 ? newLogs.Concat(derivedLogs).ToList() : newLogs;

                var savedCount = await _cacheService.AppendAuditLogsAsync(_currentGroupId, allToPersist);
                _totalLogCount = await _cacheService.GetAuditLogCountAsync(_currentGroupId);
                Console.WriteLine($"[AUDIT-SVC] Saved {savedCount} new entries (duplicates skipped). Total in DB: {_totalLogCount}");

                bool discordConfigured = _discordService.IsConfiguredForGroup(_currentGroupId);
                Console.WriteLine($"[AUDIT-SVC] Send candidates: {orderedToSend.Count}, Discord configured for group: {discordConfigured}");

                if (orderedToSend.Count > 0 && discordConfigured)
                {
                    Console.WriteLine($"[AUDIT-SVC] Sending {orderedToSend.Count} Discord notifications...");
                    int attempted = 0;
                    int sentOk = 0;
                    int skippedDisabled = 0;
                    var sentLogIds = new List<string>();

                    foreach (var log in orderedToSend)
                    {
                        attempted++;

                        if (!_discordService.ShouldSendAuditEvent(log.EventType, _currentGroupId))
                        {
                            skippedDisabled++;
                            sentLogIds.Add(log.Id);
                            Console.WriteLine($"[AUDIT-SVC] Skipping disabled event type '{log.EventType}' ({attempted}/{orderedToSend.Count})");
                            continue;
                        }

                        Console.WriteLine($"[AUDIT-SVC] Processing {attempted}/{orderedToSend.Count}: EventType={log.EventType}, Actor={log.ActorName}");
                        var success = await SendDiscordNotificationAsync(log);

                        if (success)
                        {
                            sentOk++;
                            sentLogIds.Add(log.Id);
                        }

                        await Task.Delay(500);
                    }

                    if (sentLogIds.Count > 0)
                    {
                        await _cacheService.MarkLogsAsSentToDiscordAsync(sentLogIds);
                        Console.WriteLine($"[AUDIT-SVC] Marked {sentLogIds.Count} logs as sent/skipped for Discord (Sent={sentOk}, Skipped={skippedDisabled})");
                    }

                    Console.WriteLine($"[AUDIT-SVC] Completed Discord loop. Attempted={attempted}, Sent={sentOk}, SkippedDisabled={skippedDisabled}");
                }
                else if (orderedToSend.Count > 0 && !discordConfigured)
                {
                    Console.WriteLine($"[AUDIT-SVC] Discord webhook not configured for group - skipping {orderedToSend.Count} notifications");
                }
                else
                {
                    Console.WriteLine("[AUDIT-SVC] No send candidates to send to Discord");
                }

                var allLogs = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
                NewLogsReceived?.Invoke(this, allLogs);

                if (savedCount > 0)
                    StatusChanged?.Invoke(this, $"✓ Found {savedCount} new entries | Total: {_totalLogCount} logs");
                else
                    StatusChanged?.Invoke(this, $"✓ Up to date | Total: {_totalLogCount} logs");

                _initialPollComplete = true;
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
        finally
        {
            _pollLock.Release();
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
                var savedCount = await _cacheService.AppendAuditLogsAsync(_currentGroupId, logs);
                totalSaved += savedCount;
                allNewLogs.AddRange(logs);
                Console.WriteLine($"[AUDIT-SVC] Saved {savedCount} new entries from page {page + 1}");
                page++;

                if (logs.Count < pageSize)
                {
                    Console.WriteLine($"[AUDIT-SVC] Received {logs.Count} < {pageSize}, reached end of logs");
                    hasMore = false;
                }

                if (hasMore)
                {
                    Console.WriteLine("[AUDIT-SVC] Rate limiting: waiting 1000ms before next request...");
                    await Task.Delay(1000);
                }
            }
        }

        _totalLogCount = await _cacheService.GetAuditLogCountAsync(_currentGroupId);
        Console.WriteLine($"[AUDIT-SVC] Historical fetch complete: {allNewLogs.Count} fetched from {page} pages, {totalSaved} new entries saved");

        var allLogsReload = await _cacheService.LoadAuditLogsAsync(_currentGroupId);
        NewLogsReceived?.Invoke(this, allLogsReload);

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
            var eventTypeRaw = entry.TryGetProperty("eventType", out var et) ? et.GetString() ?? "unknown" : "unknown";
            var eventType = NormalizeEventType(eventTypeRaw);

            var rawJson = entry.GetRawText();
            var idValue = entry.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(idValue))
            {
                idValue = ComputeAuditLogId(rawJson);
            }

            DateTime createdUtc = DateTime.UtcNow;
            if (entry.TryGetProperty("created_at", out var createdAt))
            {
                var s = createdAt.GetString();
                if (!string.IsNullOrWhiteSpace(s) && DateTimeOffset.TryParse(s, out var dto))
                    createdUtc = dto.UtcDateTime;
            }

            var log = new AuditLogEntry
            {
                Id = idValue!,
                EventType = eventType,
                CreatedAt = createdUtc,
                RawData = rawJson
            };

            if (entry.TryGetProperty("actorId", out var actorId))
                log.ActorId = actorId.GetString();

            if (entry.TryGetProperty("actorDisplayName", out var actorName))
                log.ActorName = actorName.GetString() ?? "Unknown";

            if (entry.TryGetProperty("targetId", out var targetId))
                log.TargetId = targetId.GetString();

            if (entry.TryGetProperty("targetDisplayName", out var targetName))
                log.TargetName = targetName.GetString();

            if (entry.TryGetProperty("description", out var desc))
                log.Description = desc.GetString() ?? "";

            if (string.IsNullOrEmpty(log.Description))
                log.Description = GenerateDescription(log);

            log.EventColor = GetEventColor(eventType);

            if (_securityMonitor != null && !string.IsNullOrEmpty(_currentGroupId))
            {
                _ = Task.Run(async () =>
                {
                    try { await TrackSecurityActionAsync(log); }
                    catch (Exception ex) { Console.WriteLine($"[AUDIT] Error tracking security action: {ex.Message}"); }
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

    private static string NormalizeEventType(string eventType)
    {
        var t = (eventType ?? "unknown").Trim();

        t = t.Replace("group.joinRequest", "group.joinrequest", StringComparison.OrdinalIgnoreCase);

        if (t.Equals("group.user.join_request", StringComparison.OrdinalIgnoreCase))
            t = "group.request.create";

        if (t.StartsWith("group.member.", StringComparison.OrdinalIgnoreCase))
            t = t.Replace("group.member.", "group.user.", StringComparison.OrdinalIgnoreCase);

        if (t.Equals("group.user.role.assign", StringComparison.OrdinalIgnoreCase))
            t = "group.user.role.add";

        if (t.Equals("group.user.role.unassign", StringComparison.OrdinalIgnoreCase))
            t = "group.user.role.remove";

        if (t.Equals("group.member.role.assign", StringComparison.OrdinalIgnoreCase))
            t = "group.user.role.add";

        if (t.Equals("group.member.role.unassign", StringComparison.OrdinalIgnoreCase))
            t = "group.user.role.remove";

        if (t.Equals("group.user.remove", StringComparison.OrdinalIgnoreCase))
            t = "group.user.kick";

        if (t.Equals("group.member.remove", StringComparison.OrdinalIgnoreCase))
            t = "group.user.kick";

        return t.ToLowerInvariant();
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
            "group.invite.create" => $"{actor} invited {target}",
            "group.user.invite" => $"{actor} invited {target}",
            "group.invite.accept" => $"{actor} accepted an invite",
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
            var t when t.Contains("invite") => "#00BCD4",
            _ => "#607D8B"
        };
    }

    private async Task<bool> SendDiscordNotificationAsync(AuditLogEntry log)
    {
        try
        {
            return await _discordService.SendAuditEventAsync(
                log.EventType,
                log.ActorName ?? "Unknown",
                log.TargetName,
                log.Description,
                _currentGroupId
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-SVC] Discord notification failed: {ex.Message}");
            return false;
        }
    }

    private static string ComputeAuditLogId(string rawJson)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(rawJson);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        StopPolling();
    }

    private async Task TrackSecurityActionAsync(AuditLogEntry log)
    {
        if (_securityMonitor == null || string.IsNullOrEmpty(_currentGroupId))
            return;

        if (string.IsNullOrEmpty(log.ActorId))
            return;

        string? actionType = log.EventType.ToLower() switch
        {
            "group.user.kick" => "group_kick",
            "group.user.ban" => "group_ban",
            "group.user.role.remove" or "group.role.remove" => "role_remove",
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
                new { EventType = log.EventType, Timestamp = log.CreatedAt }
            );
        }
    }
}
