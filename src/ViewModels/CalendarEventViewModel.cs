using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class CalendarEventViewModel : ObservableObject
{
    private readonly ICalendarEventService _eventService;
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private ObservableCollection<CalendarEvent> _events = new();

    [ObservableProperty]
    private ObservableCollection<EventTemplate> _templates = new();

    [ObservableProperty]
    private CalendarEvent _draft = new()
    {
        StartTime = DateTime.Now.AddHours(1),
        EndTime = DateTime.Now.AddHours(2),
        Visibility = "Public",
        Category = "Hangout"
    };

    [ObservableProperty]
    private string _draftRecurrenceType = "None";

    [ObservableProperty] private bool _recMon;
    [ObservableProperty] private bool _recTue;
    [ObservableProperty] private bool _recWed;
    [ObservableProperty] private bool _recThu;
    [ObservableProperty] private bool _recFri;
    [ObservableProperty] private bool _recSat;
    [ObservableProperty] private bool _recSun;

    [ObservableProperty]
    private string _draftMonthDaysText = "";

    [ObservableProperty]
    private DateTime? _draftRecurrenceUntil;

    [ObservableProperty]
    private DateTime? _draftSpecificDate;

    [ObservableProperty]
    private ObservableCollection<DateTime> _draftSpecificDates = new();

    [ObservableProperty]
    private string _draftTagsText = "";

    [ObservableProperty] private bool _draftPlatformWindows;
    [ObservableProperty] private bool _draftPlatformAndroid;
    [ObservableProperty] private bool _draftPlatformIos;

    [ObservableProperty]
    private EventTemplate _templateDraft = new()
    {
        Duration = TimeSpan.FromHours(1),
        Visibility = "Public",
        Category = "Hangout"
    };

    [ObservableProperty]
    private string _templateTagsText = "";

    [ObservableProperty] private bool _templatePlatformWindows;
    [ObservableProperty] private bool _templatePlatformAndroid;
    [ObservableProperty] private bool _templatePlatformIos;

    [ObservableProperty]
    private CalendarEvent? _selectedEvent;

    [ObservableProperty]
    private EventTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _recurrenceHint = "Choose recurrence pattern";

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();
    public ObservableCollection<LanguageOption> TemplateLanguageOptions { get; } = new();

    public IReadOnlyList<string> VisibilityOptions { get; } = new[] { "Public", "Group" };

    public IReadOnlyList<string> Categories { get; } = new[]
    {
        "Music", "Gaming", "Hangout", "Exploring", "Avatars", "Film & Media",
        "Dance", "Roleplaying", "Performance", "Wellness", "Arts", "Education", "Other"
    };

    public IReadOnlyList<string> RecurrenceTypes { get; } = new[] { "None", "Weekly", "Monthly", "SpecificDates" };

    public CalendarEventViewModel()
    {
        _eventService = App.Services.GetRequiredService<ICalendarEventService>();
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        SeedLanguages(LanguageOptions);
        SeedLanguages(TemplateLanguageOptions);
        SyncDraftUi(Draft);
        SyncTemplateUi(TemplateDraft);
    }

    partial void OnDraftChanged(CalendarEvent value) => SyncDraftUi(value);
    partial void OnTemplateDraftChanged(EventTemplate value) => SyncTemplateUi(value);

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        var events = await _eventService.GetEventsAsync();
        var templates = await _eventService.GetTemplatesAsync();
        Events = new ObservableCollection<CalendarEvent>(events);
        Templates = new ObservableCollection<EventTemplate>(templates);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task SaveEventAsync()
    {
        ApplyDraftUiToModel();
        Draft.Visibility = NormalizeVisibility(Draft.Visibility);
        Draft.Category = NormalizeCategory(Draft.Category);

        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            StatusMessage = "Please enter an event name.";
            return;
        }

        if (Draft.EndTime <= Draft.StartTime)
        {
            StatusMessage = "End time must be after start time.";
            return;
        }

        IsBusy = true;
        await _eventService.AddOrUpdateEventAsync(Draft);
        await PublishToVrChatAsync(Draft);
        await _eventService.GenerateRecurringEventsAsync();
        await LoadAsync();
        StatusMessage = "Event saved.";
        Draft = new CalendarEvent
        {
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2),
            Visibility = Draft.Visibility,
            Category = Draft.Category,
            Recurrence = new RecurrenceOptions { Type = "None", Enabled = false }
        };
        DraftRecurrenceType = "None";
        RecSun = RecMon = RecTue = RecWed = RecThu = RecFri = RecSat = false;
        DraftMonthDaysText = "";
        DraftRecurrenceUntil = null;
        DraftSpecificDates = new ObservableCollection<DateTime>();
        SyncLanguageSelections(LanguageOptions, Draft.Languages);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DeleteEventAsync(CalendarEvent? evt)
    {
        if (evt == null) return;
        var groupId = _mainViewModel.GroupId;
        IsBusy = true;
        if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(evt.ExternalId))
        {
            var deleted = await _apiService.DeleteGroupEventAsync(groupId, evt.ExternalId);
            if (!deleted)
            {
                StatusMessage = "VRChat delete failed; removing local copy only (see console for details).";
            }
        }
        await _eventService.DeleteEventAsync(evt.Id);
        await LoadAsync();
        StatusMessage = "Event deleted.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DuplicateEventAsync(CalendarEvent? evt)
    {
        if (evt == null) return;
        var duration = evt.EndTime - evt.StartTime;
        var newStart = evt.StartTime.AddDays(7);
        var newEnd = newStart.Add(duration);
        IsBusy = true;
        await _eventService.DuplicateEventAsync(evt.Id, newStart, newEnd);
        await LoadAsync();
        StatusMessage = "Event duplicated +7 days.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(TemplateDraft.Name))
        {
            StatusMessage = "Template needs a name.";
            return;
        }
        ApplyTemplateUiToModel();
        IsBusy = true;
        await _eventService.AddOrUpdateTemplateAsync(TemplateDraft);
        await LoadAsync();
        StatusMessage = "Template saved.";
        TemplateDraft = new EventTemplate
        {
            Duration = TimeSpan.FromHours(1),
            Visibility = TemplateDraft.Visibility,
            Category = TemplateDraft.Category
        };
        SyncLanguageSelections(TemplateLanguageOptions, TemplateDraft.Languages);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync(EventTemplate? template)
    {
        if (template == null) return;
        IsBusy = true;
        await _eventService.DeleteTemplateAsync(template.Id);
        await LoadAsync();
        StatusMessage = "Template deleted.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task CreateFromTemplateAsync(EventTemplate? template)
    {
        if (template == null) return;
        var start = DateTime.Now.AddDays(1).Date.AddHours(18);
        var end = start.Add(template.Duration);
        IsBusy = true;
        await _eventService.CreateFromTemplateAsync(template.Id, start, end);
        await LoadAsync();
        StatusMessage = "Event created from template.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        await _eventService.GenerateRecurringEventsAsync();
        await LoadAsync();
        StatusMessage = "Events refreshed.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task SyncFromVrChatAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusMessage = "Set a Group ID first (sidebar).";
            return;
        }

        IsBusy = true;
        try
        {
            var remote = await _apiService.GetGroupEventsAsync(groupId);
            if (remote.Count == 0)
            {
                StatusMessage = "No events found on VRChat for this group.";
                return;
            }
            foreach (var evt in remote)
            {
                var mapped = new CalendarEvent
                {
                    ExternalId = evt.Id,
                    ExternalImageId = evt.ImageId,
                    Name = evt.Title,
                    Description = evt.Description,
                    Category = NormalizeCategory(evt.Category),
                    Visibility = NormalizeVisibility(evt.AccessType.Equals("group", StringComparison.OrdinalIgnoreCase) ? "Group" : "Public"),
                    StartTime = evt.StartsAt.ToLocalTime(),
                    EndTime = evt.EndsAt.ToLocalTime(),
                    Languages = evt.Languages ?? new List<string>(),
                    Platforms = evt.Platforms ?? new List<string>(),
                    Tags = evt.Tags ?? new List<string>(),
                    SendNotification = evt.SendCreationNotification,
                    Recurrence = new RecurrenceOptions { Enabled = false, Type = "None" }
                };
                await _eventService.AddOrUpdateEventAsync(mapped);
            }
            await LoadAsync();
            StatusMessage = $"Synced {Events.Count} events from VRChat.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddSpecificDate()
    {
        if (DraftSpecificDate.HasValue)
        {
            var date = DraftSpecificDate.Value.Date;
            if (!DraftSpecificDates.Contains(date))
            {
                DraftSpecificDates.Add(date);
                DraftSpecificDates = new ObservableCollection<DateTime>(DraftSpecificDates.OrderBy(d => d));
            }
            DraftSpecificDate = null;
        }
    }

    [RelayCommand]
    private void RemoveSpecificDate(DateTime? date)
    {
        if (!date.HasValue) return;
        if (DraftSpecificDates.Contains(date.Value))
        {
            DraftSpecificDates.Remove(date.Value);
            DraftSpecificDates = new ObservableCollection<DateTime>(DraftSpecificDates.OrderBy(d => d));
        }
    }

    [RelayCommand]
    private void BrowseEventThumbnail()
    {
        var dialog = BuildImageDialog();
        if (dialog.ShowDialog() == true)
        {
            Draft.ThumbnailPath = dialog.FileName;
            OnPropertyChanged(nameof(Draft));
        }
    }

    [RelayCommand]
    private void BrowseTemplateThumbnail()
    {
        var dialog = BuildImageDialog();
        if (dialog.ShowDialog() == true)
        {
            TemplateDraft.ThumbnailPath = dialog.FileName;
            OnPropertyChanged(nameof(TemplateDraft));
        }
    }

    private void ApplyDraftUiToModel()
    {
        Draft.Languages = SelectedLanguageCodes(LanguageOptions);
        Draft.Tags = ParseCsv(DraftTagsText);
        Draft.Platforms = BuildPlatforms(DraftPlatformWindows, DraftPlatformAndroid, DraftPlatformIos);
        var rec = Draft.Recurrence ??= new RecurrenceOptions();
        rec.Enabled = Draft.Recurrence.Enabled;
        rec.Type = DraftRecurrenceType;
        rec.DaysOfWeek = BuildSelectedDays();
        rec.MonthDays = ParseMonthDays(DraftMonthDaysText);
        rec.SpecificDates = DraftSpecificDates.ToList();
        rec.Until = DraftRecurrenceUntil;
        if (!rec.Enabled)
        {
            rec.Type = "None";
            rec.DaysOfWeek.Clear();
            rec.MonthDays.Clear();
            rec.SpecificDates.Clear();
            rec.Until = null;
        }
        Draft.Recurrence = rec;
    }

    private void ApplyTemplateUiToModel()
    {
        TemplateDraft.Languages = SelectedLanguageCodes(TemplateLanguageOptions);
        TemplateDraft.Tags = ParseCsv(TemplateTagsText);
        TemplateDraft.Platforms = BuildPlatforms(TemplatePlatformWindows, TemplatePlatformAndroid, TemplatePlatformIos);
        TemplateDraft.Visibility = NormalizeVisibility(TemplateDraft.Visibility);
        TemplateDraft.Category = NormalizeCategory(TemplateDraft.Category);
    }

    private void SyncDraftUi(CalendarEvent value)
    {
        value.Languages ??= new List<string>();
        value.Tags ??= new List<string>();
        value.Platforms ??= new List<string>();
        value.Recurrence ??= new RecurrenceOptions();
        value.Recurrence.DaysOfWeek ??= new List<DayOfWeek>();
        value.Recurrence.MonthDays ??= new List<int>();
        value.Recurrence.SpecificDates ??= new List<DateTime>();
        DraftTagsText = string.Join(", ", value.Tags);
        DraftPlatformWindows = value.Platforms.Contains("Windows", StringComparer.OrdinalIgnoreCase);
        DraftPlatformAndroid = value.Platforms.Contains("Android", StringComparer.OrdinalIgnoreCase);
        DraftPlatformIos = value.Platforms.Contains("iOS", StringComparer.OrdinalIgnoreCase);

        SyncLanguageSelections(LanguageOptions, value.Languages);

        DraftRecurrenceType = string.IsNullOrWhiteSpace(value.Recurrence.Type) ? "None" : value.Recurrence.Type;
        RecSun = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Sunday);
        RecMon = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Monday);
        RecTue = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Tuesday);
        RecWed = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Wednesday);
        RecThu = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Thursday);
        RecFri = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Friday);
        RecSat = value.Recurrence.DaysOfWeek.Contains(DayOfWeek.Saturday);
        DraftMonthDaysText = string.Join(", ", value.Recurrence.MonthDays);
        DraftRecurrenceUntil = value.Recurrence.Until;
        DraftSpecificDates = new ObservableCollection<DateTime>(value.Recurrence.SpecificDates.OrderBy(d => d));
    }

    private void SyncTemplateUi(EventTemplate value)
    {
        value.Languages ??= new List<string>();
        value.Tags ??= new List<string>();
        value.Platforms ??= new List<string>();
        TemplateTagsText = string.Join(", ", value.Tags);
        TemplatePlatformWindows = value.Platforms.Contains("Windows", StringComparer.OrdinalIgnoreCase);
        TemplatePlatformAndroid = value.Platforms.Contains("Android", StringComparer.OrdinalIgnoreCase);
        TemplatePlatformIos = value.Platforms.Contains("iOS", StringComparer.OrdinalIgnoreCase);
        SyncLanguageSelections(TemplateLanguageOptions, value.Languages);
    }

    private static List<string> ParseCsv(string text)
    {
        return text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildPlatforms(bool win, bool android, bool ios)
    {
        var list = new List<string>();
        if (win) list.Add("Windows");
        if (android) list.Add("Android");
        if (ios) list.Add("iOS");
        return list;
    }

    private List<DayOfWeek> BuildSelectedDays()
    {
        var list = new List<DayOfWeek>();
        if (RecSun) list.Add(DayOfWeek.Sunday);
        if (RecMon) list.Add(DayOfWeek.Monday);
        if (RecTue) list.Add(DayOfWeek.Tuesday);
        if (RecWed) list.Add(DayOfWeek.Wednesday);
        if (RecThu) list.Add(DayOfWeek.Thursday);
        if (RecFri) list.Add(DayOfWeek.Friday);
        if (RecSat) list.Add(DayOfWeek.Saturday);
        return list;
    }

    private static List<int> ParseMonthDays(string text)
    {
        return text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Select(t => int.TryParse(t, out var n) ? n : -1)
            .Where(n => n >= 1 && n <= 31)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    private async Task PublishToVrChatAsync(CalendarEvent evt)
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            StatusMessage = "Set a Group ID first (sidebar) to publish to VRChat.";
            return;
        }

        var platforms = MapPlatforms(evt.Platforms);
        if (platforms == null || platforms.Count == 0)
        {
            StatusMessage = "Select at least one platform (PC, Android, or iOS).";
            return;
        }

        var languages = MapLanguages(evt.Languages) ?? new List<string>();

        var request = new GroupEventCreateRequest
        {
            Title = evt.Name,
            Description = evt.Description,
            StartsAt = evt.StartTime.ToUniversalTime(),
            EndsAt = evt.EndTime.ToUniversalTime(),
            Category = evt.Category.ToLowerInvariant(),
            AccessType = evt.Visibility.Equals("group", StringComparison.OrdinalIgnoreCase) ? "group" : "public",
            SendCreationNotification = evt.SendNotification,
            Languages = languages,
            Platforms = platforms,
            Tags = evt.Tags
        };

        if (!string.IsNullOrWhiteSpace(evt.ThumbnailPath))
        {
            try
            {
                var imageId = evt.ExternalImageId;
                if (string.IsNullOrWhiteSpace(imageId))
                {
                    StatusMessage = "Uploading image to VRChat...";
                    imageId = await _apiService.UploadImageAsync(evt.ThumbnailPath);
                }

                if (!string.IsNullOrWhiteSpace(imageId))
                {
                    request.ImageId = imageId;
                    evt.ExternalImageId = imageId;
                }
                else
                {
                    Console.WriteLine("[EVENT] Image upload failed or unsupported; continuing without image.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EVENT] Image upload exception: {ex.Message}");
            }
        }

        try
        {
            var created = await _apiService.CreateGroupEventAsync(groupId, request);
            if (created != null)
            {
                evt.ExternalId = created.Id;
                await _eventService.AddOrUpdateEventAsync(evt);
                StatusMessage = "Event published to VRChat.";
            }
            else
            {
                StatusMessage = "Failed to publish to VRChat (no response).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Publish failed: {ex.Message}";
        }
    }

    private string NormalizeVisibility(string visibility)
    {
        return VisibilityOptions.Contains(visibility) ? visibility : "Public";
    }

    private string NormalizeCategory(string category)
    {
        return Categories.Contains(category) ? category : "Other";
    }

    private static List<string>? MapPlatforms(List<string>? platforms)
    {
        if (platforms == null || platforms.Count == 0) return null;
        var mapped = new List<string>();
        foreach (var p in platforms)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var key = p.Trim().ToLowerInvariant();
            switch (key)
            {
                case "windows":
                case "pc":
                case "standalonewindows":
                    mapped.Add("standalonewindows");
                    break;
                case "android":
                case "quest":
                    mapped.Add("android");
                    break;
                case "ios":
                    mapped.Add("ios");
                    break;
            }
        }
        return mapped.Count == 0 ? null : mapped.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "english", "eng" }, { "en", "eng" }, { "eng", "eng" },
        { "spanish", "spa" }, { "es", "spa" }, { "spa", "spa" },
        { "japanese", "jpn" }, { "jp", "jpn" }, { "jpn", "jpn" },
        { "chinese", "zho" }, { "zh", "zho" }, { "zho", "zho" },
        { "german", "deu" }, { "de", "deu" }, { "deu", "deu" },
        { "french", "fra" }, { "fr", "fra" }, { "fra", "fra" },
        { "russian", "rus" }, { "ru", "rus" }, { "rus", "rus" },
        { "portuguese", "por" }, { "pt", "por" }, { "por", "por" },
        { "korean", "kor" }, { "ko", "kor" }, { "kor", "kor" },
        { "polish", "pol" }, { "pl", "pol" }, { "pol", "pol" },
        { "italian", "ita" }, { "it", "ita" }, { "ita", "ita" },
        { "thai", "tha" }, { "th", "tha" }, { "tha", "tha" },
        { "dutch", "nld" }, { "nl", "nld" }, { "nld", "nld" },
        { "arabic", "ara" }, { "ar", "ara" }, { "ara", "ara" },
        { "swedish", "swe" }, { "sv", "swe" }, { "swe", "swe" },
        { "norwegian", "nor" }, { "no", "nor" }, { "nor", "nor" },
        { "turkish", "tur" }, { "tr", "tur" }, { "tur", "tur" },
        { "danish", "dan" }, { "da", "dan" }, { "dan", "dan" },
        { "ukrainian", "ukr" }, { "uk", "ukr" }, { "ukr", "ukr" },
        { "indonesian", "ind" }, { "id", "ind" }, { "ind", "ind" },
        { "vietnamese", "vie" }, { "vi", "vie" }, { "vie", "vie" },
        { "czech", "ces" }, { "cs", "ces" }, { "ces", "ces" },
        { "croatian", "hrv" }, { "hr", "hrv" }, { "hrv", "hrv" },
        { "finnish", "fin" }, { "fi", "fin" }, { "fin", "fin" },
        { "romanian", "ron" }, { "ro", "ron" }, { "ron", "ron" },
        { "hungarian", "hun" }, { "hu", "hun" }, { "hun", "hun" },
        { "hebrew", "heb" }, { "he", "heb" }, { "heb", "heb" },
        { "afrikaans", "afr" }, { "af", "afr" }, { "afr", "afr" },
        { "bengali", "ben" }, { "bn", "ben" }, { "ben", "ben" },
        { "bulgarian", "bul" }, { "bg", "bul" }, { "bul", "bul" },
        { "welsh", "cym" }, { "cy", "cym" }, { "cym", "cym" },
        { "greek", "ell" }, { "el", "ell" }, { "ell", "ell" },
        { "estonian", "est" }, { "et", "est" }, { "est", "est" },
        { "filipino", "fil" }, { "tl", "fil" }, { "fil", "fil" },
        { "hindi", "hin" }, { "hi", "hin" }, { "hin", "hin" },
        { "icelandic", "isl" }, { "is", "isl" }, { "isl", "isl" },
        { "latvian", "lav" }, { "lv", "lav" }, { "lav", "lav" },
        { "lithuanian", "lit" }, { "lt", "lit" }, { "lit", "lit" },
        { "luxembourgish", "ltz" }, { "lb", "ltz" }, { "ltz", "ltz" },
        { "marathi", "mar" }, { "mr", "mar" }, { "mar", "mar" },
        { "macedonian", "mkd" }, { "mk", "mkd" }, { "mkd", "mkd" },
        { "malay", "msa" }, { "ms", "msa" }, { "msa", "msa" },
        { "slovak", "slk" }, { "sk", "slk" }, { "slk", "slk" },
        { "slovenian", "slv" }, { "sl", "slv" }, { "slv", "slv" },
        { "telugu", "tel" }, { "te", "tel" }, { "tel", "tel" },
        { "maori", "mri" }, { "mi", "mri" }, { "mri", "mri" },
        { "esperanto", "epo" }, { "eo", "epo" }, { "epo", "epo" },
        { "toki pona", "tok" }, { "tok", "tok" }
    };

    private static List<string>? MapLanguages(List<string>? languages)
    {
        if (languages == null || languages.Count == 0) return null;
        var mapped = new List<string>();
        foreach (var lang in languages)
        {
            if (string.IsNullOrWhiteSpace(lang)) continue;
            var key = lang.Trim();
            if (LanguageMap.TryGetValue(key, out var code))
            {
                mapped.Add(code);
            }
        }
        return mapped.Count == 0 ? null : mapped.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void SeedLanguages(ObservableCollection<LanguageOption> target)
    {
        target.Clear();
        foreach (var kvp in LanguageMap)
        {
            if (kvp.Key.Length != 3) continue; // only canonical codes
            if (target.Any(o => string.Equals(o.Code, kvp.Key, StringComparison.OrdinalIgnoreCase))) continue;
            target.Add(new LanguageOption(kvp.Key, kvp.Key.ToUpperInvariant()));
        }
    }

    private static void SyncLanguageSelections(ObservableCollection<LanguageOption> options, List<string> selected)
    {
        var set = selected?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in options)
        {
            opt.IsSelected = set.Contains(opt.Code);
        }
    }

    private static List<string> SelectedLanguageCodes(ObservableCollection<LanguageOption> options)
    {
        return options.Where(o => o.IsSelected).Select(o => o.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static OpenFileDialog BuildImageDialog()
    {
        return new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.gif|All Files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
    }
}

public class LanguageOption : ObservableObject
{
    public string Code { get; }

    private string _display;
    public string Display
    {
        get => _display;
        set => SetProperty(ref _display, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public LanguageOption(string code, string display)
    {
        Code = code;
        _display = display;
    }
}
