using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using System.Linq;

namespace VRCGroupTools.Services;

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "Public"; // Public/Group
    public string GroupId { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public List<string> Languages { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? ExternalId { get; set; }
    public string? ExternalImageId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? ThumbnailPath { get; set; }
    public bool SendNotification { get; set; }
    public bool Followed { get; set; }
    public RecurrenceOptions Recurrence { get; set; } = new();
    public List<Guid> ExecutedRuleIds { get; set; } = new();
}

public class RecurrenceOptions
{
    public bool Enabled { get; set; }
    public string Type { get; set; } = "None"; // None, Weekly, Monthly, SpecificDates, LegacyInterval
    public int IntervalDays { get; set; } = 7; // legacy fallback
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
    public List<int> MonthDays { get; set; } = new();
    public List<DateTime> SpecificDates { get; set; } = new();
    public DateTime? Until { get; set; }
}

public class EventTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "Public";
    public string GroupId { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public List<string> Languages { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(1);
    public bool SendNotification { get; set; }
    public string? ThumbnailPath { get; set; }
}

public enum AutomationTriggerType
{
    BeforeEventStart,
    AfterEventEnd
}

public class EventAutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Rule";
    public bool Enabled { get; set; } = false; // New rules are explicitly opt-in; loaded rules are preserved.
    public AutomationTriggerType TriggerType { get; set; }
    public TimeSpan TimeOffset { get; set; } // e.g. 30 mins
    public string? FilterGroupId { get; set; }
    
    // Action
    public string PostTitle { get; set; } = string.Empty;
    public string PostBody { get; set; } = string.Empty;
    public string? PostImageId { get; set; }
    public bool SendToDiscord { get; set; }
}

public interface ICalendarEventService
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync();
    Task<IReadOnlyList<EventTemplate>> GetTemplatesAsync();
    Task<IReadOnlyList<EventAutomationRule>> GetAutomationRulesAsync();
    
    Task AddOrUpdateEventAsync(CalendarEvent evt);
    Task DeleteEventAsync(Guid id);
    Task<CalendarEvent?> DuplicateEventAsync(Guid id, DateTime newStart, DateTime newEnd);
    
    Task AddOrUpdateTemplateAsync(EventTemplate template);
    Task DeleteTemplateAsync(Guid id);
    Task<CalendarEvent?> CreateFromTemplateAsync(Guid templateId, DateTime start, DateTime end);
    
    Task AddOrUpdateAutomationRuleAsync(EventAutomationRule rule);
    Task DeleteAutomationRuleAsync(Guid id);
    Task RunAutomationChecksAsync(System.Threading.CancellationToken cancellation = default);
    Task<IReadOnlyList<ScheduledPostDraft>> GetPendingPostsAsync();
    Task SavePostDraftAsync(ScheduledPostDraft post);
    
    Task GenerateRecurringEventsAsync(int daysAhead = 30);
    Task ExportDataAsync(string filePath);
    Task ImportDataAsync(string filePath);
}

public class CalendarEventService : ICalendarEventService, IDisposable
{
    private readonly string _eventPath;
    private readonly string _templatePath;
    private readonly string _automationPath;
    private readonly IVRChatApiService _apiService;
    private readonly IDiscordWebhookService _discordService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<CalendarEvent> _events = new();
    private List<EventTemplate> _templates = new();
    private List<EventAutomationRule> _automationRules = new();
    private System.Timers.Timer? _automationTimer;
    private System.Threading.CancellationTokenSource? _scheduleRun;
    private bool _loadFailed;

    private readonly Func<DateTime> _utcNow;

    public CalendarEventService(IVRChatApiService apiService, IDiscordWebhookService discordService)
        : this(apiService, discordService, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCGroupTools"), () => DateTime.UtcNow) { }

    // Construction never starts background work or rewrites existing schedules.
    public CalendarEventService(IVRChatApiService apiService, IDiscordWebhookService discordService, string appFolder, Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
        _apiService = apiService;
        _discordService = discordService;
        Directory.CreateDirectory(appFolder);
        _eventPath = Path.Combine(appFolder, "events.json");
        _templatePath = Path.Combine(appFolder, "event_templates.json");
        _automationPath = Path.Combine(appFolder, "event_automation.json");

        Load();
    }

    public void StartAutomationService()
    {
        if (_automationTimer != null) return;
        _automationTimer = new System.Timers.Timer(60000); // Check every minute
        _scheduleRun = new();
        var token = _scheduleRun.Token;
        _automationTimer.Elapsed += async (s, e) =>
        {
            try { await RunAutomationChecksAsync(token); }
            catch (OperationCanceledException) { }
            catch { Console.WriteLine("[EVENTS] Draft check failed; schedules retained. No external actions performed."); }
        };
        _automationTimer.AutoReset = true;
        _automationTimer.Start();
        Console.WriteLine("[EVENTS] Automation service started.");
    }
    
    public void StopAutomationService()
    {
        _scheduleRun?.Cancel();
        _scheduleRun?.Dispose();
        _scheduleRun = null;
        _automationTimer?.Stop();
        _automationTimer?.Dispose();
        _automationTimer = null;
    }

    public void Dispose() => StopAutomationService();

    private void Load()
    {
        try
        {
            if (File.Exists(_eventPath))
            {
                var json = File.ReadAllText(_eventPath);
                _events = JsonSerializer.Deserialize<List<CalendarEvent>>(json, _jsonOptions) ?? new();
            }
            if (File.Exists(_templatePath))
            {
                var json = File.ReadAllText(_templatePath);
                _templates = JsonSerializer.Deserialize<List<EventTemplate>>(json, _jsonOptions) ?? new();
            }
            if (File.Exists(_automationPath))
            {
                var json = File.ReadAllText(_automationPath);
                _automationRules = JsonSerializer.Deserialize<List<EventAutomationRule>>(json, _jsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            Console.WriteLine($"[EVENTS] Failed to load: {ex.Message}");
            _events ??= new();
            _templates ??= new();
            _automationRules ??= new();
        }
    }

    private void Save()
    {
        if (_loadFailed) throw new InvalidDataException("Schedule data could not be loaded; refusing to overwrite existing files.");
        AtomicWrite(_eventPath, JsonSerializer.Serialize(_events, _jsonOptions));
        AtomicWrite(_templatePath, JsonSerializer.Serialize(_templates, _jsonOptions));
        AtomicWrite(_automationPath, JsonSerializer.Serialize(_automationRules, _jsonOptions));
    }

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync()
        => Task.FromResult<IReadOnlyList<CalendarEvent>>(_events.OrderBy(e => e.StartTime).ToList());

    public Task<IReadOnlyList<EventTemplate>> GetTemplatesAsync()
        => Task.FromResult<IReadOnlyList<EventTemplate>>(_templates.OrderBy(t => t.Name).ToList());

    public Task<IReadOnlyList<EventAutomationRule>> GetAutomationRulesAsync()
        => Task.FromResult<IReadOnlyList<EventAutomationRule>>(_automationRules.ToList());

    public Task AddOrUpdateEventAsync(CalendarEvent evt)
    {
        var existing = _events.FirstOrDefault(e => e.Id == evt.Id);
        if (existing is null)
        {
            _events.Add(evt);
        }
        else
        {
            var idx = _events.IndexOf(existing);
            _events[idx] = evt;
        }
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(Guid id)
    {
        _events.RemoveAll(e => e.Id == id);
        Save();
        return Task.CompletedTask;
    }

    public Task<CalendarEvent?> DuplicateEventAsync(Guid id, DateTime newStart, DateTime newEnd)
    {
        var source = _events.FirstOrDefault(e => e.Id == id);
        if (source is null) return Task.FromResult<CalendarEvent?>(null);
        
        // Ensure lists are initialized
        source.Languages ??= new List<string>();
        source.Platforms ??= new List<string>();
        source.Tags ??= new List<string>();
        source.Recurrence ??= new RecurrenceOptions();
        
        var clone = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            Category = source.Category,
            Description = source.Description,
            Visibility = source.Visibility,
            GroupId = source.GroupId,
            TimeZoneId = source.TimeZoneId,
            Languages = new List<string>(source.Languages),
            Platforms = new List<string>(source.Platforms),
            Tags = new List<string>(source.Tags),
            ThumbnailPath = source.ThumbnailPath,
            ExternalImageId = source.ExternalImageId,
            SendNotification = source.SendNotification,
            Followed = false,
            StartTime = newStart,
            EndTime = newEnd,
            Recurrence = new RecurrenceOptions
            {
                Enabled = source.Recurrence.Enabled,
                IntervalDays = source.Recurrence.IntervalDays,
                DaysOfWeek = new List<DayOfWeek>(source.Recurrence.DaysOfWeek ?? new()),
                MonthDays = new List<int>(source.Recurrence.MonthDays ?? new()),
                SpecificDates = new List<DateTime>(source.Recurrence.SpecificDates ?? new()),
                Type = source.Recurrence.Type,
                Until = source.Recurrence.Until
            }
        };
        _events.Add(clone);
        Save();
        return Task.FromResult<CalendarEvent?>(clone);
    }

    public Task AddOrUpdateTemplateAsync(EventTemplate template)
    {
        var existing = _templates.FirstOrDefault(t => t.Id == template.Id);
        if (existing is null)
        {
            _templates.Add(template);
        }
        else
        {
            var idx = _templates.IndexOf(existing);
            _templates[idx] = template;
        }
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteTemplateAsync(Guid id)
    {
        _templates.RemoveAll(t => t.Id == id);
        Save();
        return Task.CompletedTask;
    }

    public Task<CalendarEvent?> CreateFromTemplateAsync(Guid templateId, DateTime start, DateTime end)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template is null) return Task.FromResult<CalendarEvent?>(null);
        template.Languages ??= new List<string>();
        template.Platforms ??= new List<string>();
        template.Tags ??= new List<string>();

        var evt = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Name = template.Name,
            Category = template.Category,
            Description = template.Description,
            Visibility = template.Visibility,
            GroupId = template.GroupId,
            TimeZoneId = template.TimeZoneId,
            Languages = new List<string>(template.Languages),
            Platforms = new List<string>(template.Platforms),
            Tags = new List<string>(template.Tags),
            ThumbnailPath = template.ThumbnailPath,
            ExternalImageId = null,
            SendNotification = template.SendNotification,
            StartTime = start,
            EndTime = end
        };
        _events.Add(evt);
        Save();
        return Task.FromResult<CalendarEvent?>(evt);
    }

    public Task AddOrUpdateAutomationRuleAsync(EventAutomationRule rule)
    {
        var existing = _automationRules.FirstOrDefault(r => r.Id == rule.Id);
        if (existing is null)
        {
            _automationRules.Add(rule);
        }
        else
        {
            var idx = _automationRules.IndexOf(existing);
            _automationRules[idx] = rule;
        }
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteAutomationRuleAsync(Guid id)
    {
        _automationRules.RemoveAll(r => r.Id == id);
        Save();
        return Task.CompletedTask;
    }

    // Scheduling only prepares drafts; publication always requires separate per-item approval.
    public async Task RunAutomationChecksAsync(System.Threading.CancellationToken cancellation = default)
    {
        await _scheduleGate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var fileLock = new FileStream(_pendingPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var now = _utcNow();
            var pending = ReadPendingPosts();
            foreach (var rule in _automationRules.Where(r => r.Enabled))
            foreach (var evt in _events)
            {
                if (string.IsNullOrWhiteSpace(evt.GroupId) ||
                    (!string.IsNullOrEmpty(rule.FilterGroupId) && rule.FilterGroupId != evt.GroupId) ||
                    (evt.ExecutedRuleIds?.Contains(rule.Id) ?? false)) continue;
                if (!ScheduleTime.TryUtc(evt.StartTime, evt.TimeZoneId, out var start) ||
                    !ScheduleTime.TryUtc(evt.EndTime, evt.TimeZoneId, out var end) || end <= start) continue;
                var due = rule.TriggerType == AutomationTriggerType.BeforeEventStart
                    ? start - rule.TimeOffset : end + rule.TimeOffset;
                if (now < due || now - due > TimeSpan.FromHours(24) ||
                    (rule.TriggerType == AutomationTriggerType.BeforeEventStart && now >= start)) continue;
                var key = $"{evt.Id}:{rule.Id}";
                if (pending.Any(p => p.Key == key)) continue;
                if (string.IsNullOrWhiteSpace(rule.PostTitle) || string.IsNullOrWhiteSpace(rule.PostBody)) continue;
                pending.Add(new ScheduledPostDraft(key, evt.GroupId,
                    rule.PostTitle.Replace("{EventName}", evt.Name).Replace("{StartTime}", evt.StartTime.ToString("g")),
                    rule.PostBody.Replace("{EventName}", evt.Name).Replace("{Description}", evt.Description),
                    rule.PostImageId, due));
            }
            cancellation.ThrowIfCancellationRequested();
            AtomicWrite(_pendingPath, JsonSerializer.Serialize(pending, _jsonOptions));
        }
        finally { _scheduleGate.Release(); }
    }

    private static readonly System.Threading.SemaphoreSlim _scheduleGate = new(1, 1);
    private string _pendingPath => _eventPath + ".pending-posts.json";
    private List<ScheduledPostDraft> ReadPendingPosts() => File.Exists(_pendingPath)
        ? JsonSerializer.Deserialize<List<ScheduledPostDraft>>(File.ReadAllText(_pendingPath), _jsonOptions) ?? new()
        : new();
    public async Task<IReadOnlyList<ScheduledPostDraft>> GetPendingPostsAsync()
    {
        await _scheduleGate.WaitAsync();
        try { return ReadPendingPosts(); }
        finally { _scheduleGate.Release(); }
    }
    public async Task SavePostDraftAsync(ScheduledPostDraft post)
    {
        if (!ApprovalPublisher.Id(post.GroupId, "grp") || string.IsNullOrWhiteSpace(post.Key)) throw new ArgumentException("Select the exact group and draft.");
        await _scheduleGate.WaitAsync();
        try
        {
            using var fileLock = new FileStream(_pendingPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var pending = ReadPendingPosts();
            pending.RemoveAll(p => p.Key == post.Key && p.GroupId == post.GroupId);
            pending.Add(post);
            AtomicWrite(_pendingPath, JsonSerializer.Serialize(pending, _jsonOptions));
        }
        finally { _scheduleGate.Release(); }
    }
    private static void AtomicWrite(string path, string text)
    {
        var temp = path + "." + Guid.NewGuid() + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }

    public Task ExportDataAsync(string filePath)
    {
        var data = new
        {
            Events = _events,
            Templates = _templates,
            Rules = _automationRules
        };
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(filePath, json);
        return Task.CompletedTask;
    }
    
    public Task ImportDataAsync(string filePath)
    {
        if (!File.Exists(filePath)) return Task.CompletedTask;
        try
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<ExportData>(json, _jsonOptions);
            
            if (data?.Events != null) _events.AddRange(data.Events);
            if (data?.Templates != null) _templates.AddRange(data.Templates);
            if (data?.Rules != null) _automationRules.AddRange(data.Rules);
            
            // Deduplicate by ID
            _events = _events.GroupBy(e => e.Id).Select(g => g.First()).ToList();
            _templates = _templates.GroupBy(t => t.Id).Select(g => g.First()).ToList();
            _automationRules = _automationRules.GroupBy(r => r.Id).Select(r => r.First()).ToList();
            
            Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENTS] Import failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task GenerateRecurringEventsAsync(int daysAhead = 30)
    {
        var created = GenerateRecurringEventsInternal(daysAhead);
        if (created)
        {
            Save();
        }

        return Task.CompletedTask;
    }

    private bool GenerateRecurringEventsInternal(int daysAhead)
    {
        var utcNow = _utcNow();
        var newEvents = new List<CalendarEvent>();

        foreach (var evt in _events.Where(e => e.Recurrence?.Enabled == true))
        {
            var rec = evt.Recurrence ?? new RecurrenceOptions();
            rec.Type ??= "None";
            rec.DaysOfWeek ??= new List<DayOfWeek>();
            rec.MonthDays ??= new List<int>();
            rec.SpecificDates ??= new List<DateTime>();
            evt.Languages ??= new List<string>();
            evt.Platforms ??= new List<string>();
            evt.Tags ??= new List<string>();

            // Ensure timezone consistency - base recurring generation on StartTime's local time logic
            // or explicit standard? We'll assume StartTime is the "base" local time for the recurrence.
            
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(evt.TimeZoneId); }
            catch (TimeZoneNotFoundException) { continue; }
            catch (InvalidTimeZoneException) { continue; }
            var now = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            var horizon = now.AddDays(Math.Clamp(daysAhead, 0, 366));
            var baseStart = evt.StartTime.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(evt.StartTime, zone) : DateTime.SpecifyKind(evt.StartTime, DateTimeKind.Unspecified);
            var baseEnd = evt.EndTime.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(evt.EndTime, zone) : DateTime.SpecifyKind(evt.EndTime, DateTimeKind.Unspecified);
            var duration = baseEnd - baseStart;
            var baseTime = baseStart.TimeOfDay;
            var startFloor = baseStart > now ? baseStart : now;

            IEnumerable<DateTime> occurrences = rec.Type switch
            {
                "Weekly" => GetWeeklyOccurrences(rec, startFloor, horizon, baseTime),
                "Monthly" => GetMonthlyOccurrences(rec, startFloor, horizon, baseTime),
                "SpecificDates" => GetSpecificDateOccurrences(rec, startFloor, horizon, baseTime),
                _ => GetLegacyIntervalOccurrences(baseStart, rec, horizon)
            };

            foreach (var nextStart in occurrences)
            {
                if (nextStart <= baseStart || nextStart < startFloor || nextStart > horizon) continue;
                
                // Do not create duplicates
                if (_events.Concat(newEvents).Any(e => e.StartTime == nextStart && e.Name == evt.Name && e.GroupId == evt.GroupId && e.TimeZoneId == evt.TimeZoneId))
                {
                    continue;
                }
                
                var nextEnd = nextStart.Add(duration);
                if (!ScheduleTime.TryUtc(nextStart, evt.TimeZoneId, out _) || !ScheduleTime.TryUtc(nextEnd, evt.TimeZoneId, out _)) continue;

                newEvents.Add(new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    Name = evt.Name,
                    Category = evt.Category,
                    Description = evt.Description,
                    Visibility = evt.Visibility,
                    GroupId = evt.GroupId,
                    TimeZoneId = evt.TimeZoneId,
                    Languages = new List<string>(evt.Languages),
                    Platforms = new List<string>(evt.Platforms),
                    Tags = new List<string>(evt.Tags),
                    ThumbnailPath = evt.ThumbnailPath,
                    ExternalImageId = evt.ExternalImageId,
                    SendNotification = evt.SendNotification,
                    Followed = evt.Followed,
                    StartTime = nextStart,
                    EndTime = nextEnd,
                    Recurrence = new RecurrenceOptions { Enabled = false }
                });
            }
        }

        if (newEvents.Count > 0)
        {
            _events.AddRange(newEvents);
            return true;
        }

        return false;
    }

    private static IEnumerable<DateTime> GetWeeklyOccurrences(RecurrenceOptions rec, DateTime startFloor, DateTime horizon, TimeSpan baseTime)
    {
        if (!rec.Enabled || rec.DaysOfWeek.Count == 0) yield break;

        var cursor = startFloor.Date;
        var untilDate = rec.Until?.Date;
        while (cursor <= horizon.Date)
        {
            if (untilDate.HasValue && cursor > untilDate.Value) yield break;
            if (rec.DaysOfWeek.Contains(cursor.DayOfWeek))
            {
                yield return cursor + baseTime;
            }
            cursor = cursor.AddDays(1);
        }
    }

    private static IEnumerable<DateTime> GetMonthlyOccurrences(RecurrenceOptions rec, DateTime startFloor, DateTime horizon, TimeSpan baseTime)
    {
        if (!rec.Enabled || rec.MonthDays.Count == 0) yield break;
        var untilDate = rec.Until?.Date;
        var monthDays = rec.MonthDays.Distinct().Where(d => d >= 1 && d <= 31).OrderBy(d => d).ToList();
        var cursor = new DateTime(startFloor.Year, startFloor.Month, 1);
        var horizonMonth = new DateTime(horizon.Year, horizon.Month, 1);

        while (cursor <= horizonMonth)
        {
            foreach (var day in monthDays)
            {
                DateTime candidate;
                try
                {
                    candidate = new DateTime(cursor.Year, cursor.Month, day).Add(baseTime);
                }
                catch
                {
                    continue;
                }

                if (candidate < startFloor) continue;
                if (untilDate.HasValue && candidate.Date > untilDate.Value) yield break;
                if (candidate > horizon) yield break;
                yield return candidate;
            }
            cursor = cursor.AddMonths(1);
        }
    }

    private static IEnumerable<DateTime> GetSpecificDateOccurrences(RecurrenceOptions rec, DateTime startFloor, DateTime horizon, TimeSpan baseTime)
    {
        if (!rec.Enabled || rec.SpecificDates.Count == 0) yield break;
        var untilDate = rec.Until?.Date;
        foreach (var date in rec.SpecificDates.OrderBy(d => d))
        {
            var candidate = date.Date.Add(baseTime);
            if (candidate < startFloor) continue;
            if (untilDate.HasValue && candidate.Date > untilDate.Value) yield break;
            if (candidate > horizon) yield break;
            yield return candidate;
        }
    }

    private static IEnumerable<DateTime> GetLegacyIntervalOccurrences(DateTime start, RecurrenceOptions recurrence, DateTime horizon)
    {
        if (!recurrence.Enabled) yield break;
        var lastStart = start;
        while (true)
        {
            var next = lastStart.AddDays(Math.Max(1, recurrence.IntervalDays));
            if (recurrence.Until.HasValue && next > recurrence.Until.Value) yield break;
            if (next > horizon) yield break;
            yield return next;
            lastStart = next;
        }
    }

    private static RecurrenceOptions CloneRecurrence(RecurrenceOptions? source)
    {
        source ??= new RecurrenceOptions();
        source.Type ??= "None";
        source.DaysOfWeek ??= new List<DayOfWeek>();
        source.MonthDays ??= new List<int>();
        source.SpecificDates ??= new List<DateTime>();

        return new RecurrenceOptions
        {
            Enabled = source.Enabled,
            Type = source.Type,
            IntervalDays = source.IntervalDays,
            DaysOfWeek = new List<DayOfWeek>(source.DaysOfWeek),
            MonthDays = new List<int>(source.MonthDays),
            SpecificDates = new List<DateTime>(source.SpecificDates),
            Until = source.Until
        };
    }
    
    private class ExportData
    {
        public List<CalendarEvent>? Events { get; set; }
        public List<EventTemplate>? Templates { get; set; }
        public List<EventAutomationRule>? Rules { get; set; }
    }
}
