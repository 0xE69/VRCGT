using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;

namespace VRCGroupTools.Services;

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "Public"; // Public/Group
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
    public List<string> Languages { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(1);
    public bool SendNotification { get; set; }
    public string? ThumbnailPath { get; set; }
}

public interface ICalendarEventService
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync();
    Task<IReadOnlyList<EventTemplate>> GetTemplatesAsync();
    Task AddOrUpdateEventAsync(CalendarEvent evt);
    Task DeleteEventAsync(Guid id);
    Task<CalendarEvent?> DuplicateEventAsync(Guid id, DateTime newStart, DateTime newEnd);
    Task AddOrUpdateTemplateAsync(EventTemplate template);
    Task DeleteTemplateAsync(Guid id);
    Task<CalendarEvent?> CreateFromTemplateAsync(Guid templateId, DateTime start, DateTime end);
    Task GenerateRecurringEventsAsync(int daysAhead = 30);
}

public class CalendarEventService : ICalendarEventService
{
    private readonly string _eventPath;
    private readonly string _templatePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<CalendarEvent> _events = new();
    private List<EventTemplate> _templates = new();

    public CalendarEventService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "VRCGroupTools");
        Directory.CreateDirectory(appFolder);
        _eventPath = Path.Combine(appFolder, "events.json");
        _templatePath = Path.Combine(appFolder, "event_templates.json");

        Load();
        GenerateRecurringEventsInternal(60);
        Save();
    }

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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENTS] Failed to load: {ex.Message}");
            _events = new();
            _templates = new();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_eventPath, JsonSerializer.Serialize(_events, _jsonOptions));
            File.WriteAllText(_templatePath, JsonSerializer.Serialize(_templates, _jsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENTS] Failed to save: {ex.Message}");
        }
    }

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync()
        => Task.FromResult<IReadOnlyList<CalendarEvent>>(_events.OrderBy(e => e.StartTime).ToList());

    public Task<IReadOnlyList<EventTemplate>> GetTemplatesAsync()
        => Task.FromResult<IReadOnlyList<EventTemplate>>(_templates.OrderBy(t => t.Name).ToList());

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
        source.Languages ??= new List<string>();
        source.Platforms ??= new List<string>();
        source.Tags ??= new List<string>();
        var clone = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Name = source.Name,
            Category = source.Category,
            Description = source.Description,
            Visibility = source.Visibility,
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
                DaysOfWeek = new List<DayOfWeek>(source.Recurrence.DaysOfWeek),
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
        var now = DateTime.Now;
        var horizon = now.AddDays(daysAhead);
        var newEvents = new List<CalendarEvent>();

        foreach (var evt in _events.Where(e => e.Recurrence.Enabled))
        {
            var rec = evt.Recurrence ?? new RecurrenceOptions();
            rec.Type ??= "None";
            rec.DaysOfWeek ??= new List<DayOfWeek>();
            rec.MonthDays ??= new List<int>();
            rec.SpecificDates ??= new List<DateTime>();
            evt.Languages ??= new List<string>();
            evt.Platforms ??= new List<string>();
            evt.Tags ??= new List<string>();

            var duration = evt.EndTime - evt.StartTime;
            var baseTime = evt.StartTime.TimeOfDay;
            var startFloor = evt.StartTime > now ? evt.StartTime : now;

            IEnumerable<DateTime> occurrences = rec.Type switch
            {
                "Weekly" => GetWeeklyOccurrences(rec, startFloor, horizon, baseTime),
                "Monthly" => GetMonthlyOccurrences(rec, startFloor, horizon, baseTime),
                "SpecificDates" => GetSpecificDateOccurrences(rec, startFloor, horizon, baseTime),
                _ => GetLegacyIntervalOccurrences(evt.StartTime, rec, horizon)
            };

            foreach (var nextStart in occurrences)
            {
                if (nextStart <= evt.StartTime) continue; // avoid duplicating the original
                var nextEnd = nextStart.Add(duration);
                if (_events.Any(e => e.StartTime == nextStart && e.Name == evt.Name))
                {
                    continue;
                }

                newEvents.Add(new CalendarEvent
                {
                    Id = Guid.NewGuid(),
                    Name = evt.Name,
                    Category = evt.Category,
                    Description = evt.Description,
                    Visibility = evt.Visibility,
                    Languages = new List<string>(evt.Languages),
                    Platforms = new List<string>(evt.Platforms),
                    Tags = new List<string>(evt.Tags),
                    ThumbnailPath = evt.ThumbnailPath,
                    ExternalImageId = evt.ExternalImageId,
                    SendNotification = evt.SendNotification,
                    Followed = evt.Followed,
                    StartTime = nextStart,
                    EndTime = nextEnd,
                    Recurrence = CloneRecurrence(evt.Recurrence)
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
}
