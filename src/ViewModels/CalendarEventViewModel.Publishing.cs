using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRCGroupTools.Services;
using VRCGroupTools.Views;

namespace VRCGroupTools.ViewModels;

public partial class CalendarEventViewModel
{
    [ObservableProperty] private ObservableCollection<PublicationHistoryItem> _publicationHistory = new();
    [RelayCommand]
    private async Task RefreshPublicationHistoryAsync()
    {
        try
        {
            var group = _currentGroup();
            var rows = await Publisher.ReadHistoryAsync(group);
            if (_currentGroup() == group) PublicationHistory = new(rows);
            else PublicationHistory.Clear();
        }
        catch { StatusMessage = "Could not read local publication history. No external actions performed."; }
    }
    // Optional injected seams keep UI verification away from production App, sessions and storage.
    public ApprovalPublisher? PublisherOverride { get; set; }
    public Func<PublicationPreview, Task<bool>>? ConfirmPublicationOverride { get; set; }
    [ObservableProperty] private string _newsTitle = "";
    [ObservableProperty] private string _newsBody = "";
    [ObservableProperty] private string _newsAudience = "group";
    [ObservableProperty] private bool _newsNotify;
    [ObservableProperty] private string? _newsImagePath;
    [ObservableProperty] private string? _newsArtworkReference;
    [ObservableProperty] private bool _omitReferencedArtwork;
    private string _newsKey = Guid.NewGuid().ToString();
    private string? _newsGroup;
    public IReadOnlyList<string> NewsAudienceOptions { get; } = new[] { "group", "public" };

    private ApprovalPublisher Publisher => PublisherOverride ?? (_apiService is IApprovalPublicationTransport transport
        ? new ApprovalPublisher(transport) : throw new InvalidOperationException("Approval publishing is unavailable for this session."));
    private Task<bool> ConfirmPublication(PublicationPreview preview) => ConfirmPublicationOverride?.Invoke(preview) ?? Task.FromResult(PublicationApprovalWindow.Confirm(preview));

    [RelayCommand]
    private void ReviewScheduledPost(ScheduledPostDraft? post)
    {
        if (post == null) return;
        _newsKey = post.Key; _newsGroup = post.GroupId;
        NewsTitle = post.Title; NewsBody = post.Body;
        NewsAudience = post.Audience; NewsNotify = post.Notify; NewsImagePath = post.LocalImagePath;
        NewsArtworkReference = post.ImageId; OmitReferencedArtwork = post.OmitReferencedArtwork;
        StatusMessage = "Draft loaded in the news editor below. Select exact audience/notifications/artwork, then Preview and approve news. Nothing sent.";
    }
    [RelayCommand]
    private void NewNewsDraft()
    {
        _newsKey = Guid.NewGuid().ToString(); _newsGroup = _currentGroup();
        NewsTitle = ""; NewsBody = ""; NewsAudience = "group"; NewsNotify = false;
        NewsImagePath = null; NewsArtworkReference = null; OmitReferencedArtwork = false;
        StatusMessage = "New local news draft. Nothing sent.";
    }
    [RelayCommand]
    private void BrowseNewsImage()
    {
        var dialog = new OpenFileDialog { Filter = "PNG artwork|*.png", CheckFileExists = true };
        if (dialog.ShowDialog() == true) NewsImagePath = dialog.FileName;
    }
    [RelayCommand]
    private void ClearNewsImage() => NewsImagePath = null;

    private PublicationDraft CurrentNews()
    {
        var group = _currentGroup();
        if (_newsGroup != null && group != _newsGroup) throw new ArgumentException("Select the scheduled draft's exact group first.");
        if (!string.IsNullOrEmpty(NewsArtworkReference) && string.IsNullOrEmpty(NewsImagePath) && !OmitReferencedArtwork) throw new ArgumentException("Choose a local PNG for exact preview, or explicitly omit the old referenced artwork.");
        return new(_newsKey, _apiService.CurrentUserId ?? "", group, "news", NewsTitle, NewsBody, NewsAudience, NewsNotify, NewsImagePath);
    }
    [RelayCommand]
    private async Task SaveNewsDraftAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var group = _currentGroup();
            if (_newsGroup != null && _newsGroup != group) throw new ArgumentException("Select the draft's exact group first.");
            await _eventService.SavePostDraftAsync(new(_newsKey, group, NewsTitle, NewsBody, NewsArtworkReference, DateTime.UtcNow,
                NewsAudience, NewsNotify, NewsImagePath, OmitReferencedArtwork));
            _newsGroup = group;
            PendingPosts = new(await _eventService.GetPendingPostsAsync());
            StatusMessage = "News draft saved locally; nothing uploaded or published. Re-open from the ready draft list.";
        }
        catch { StatusMessage = "Local news save failed; nothing published. Check selected group and storage."; }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private async Task PublishNewsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { StatusMessage = (await Publisher.PublishAsync(CurrentNews, ConfirmPublication)).ToString(); }
        catch (ArgumentException ex) { StatusMessage = ex.Message; }
        catch { StatusMessage = "Publication stopped. No automatic retry. Inspect durable history / VRChat before another attempt; outcome may be uncertain."; }
        finally { IsBusy = false; }
    }
    private PublicationDraft CurrentEvent()
    {
        if (!TryApplyScheduleFields()) throw new ArgumentException("Choose start/end dates and valid 24-hour HH:mm clocks.");
        ApplyDraftUiToModel();
        var group = _currentGroup();
        if (!string.IsNullOrEmpty(Draft.GroupId) && Draft.GroupId != group) throw new ArgumentException("Select this event's exact group before publication.");
        if (!string.IsNullOrEmpty(Draft.ExternalId)) throw new ArgumentException("Already linked to an external event. Creating a duplicate/update is not authorized here.");
        if (!ScheduleTime.TryUtc(Draft.StartTime, Draft.TimeZoneId, out var start) || !ScheduleTime.TryUtc(Draft.EndTime, Draft.TimeZoneId, out var end)) throw new ArgumentException("Select a valid timezone and unambiguous dates (correct DST gaps/folds).");
        // Reuse the editor's existing platform/language mapping, but never guess the timezone or category.
        var category = Draft.Category switch { "Film & Media" => "film_media", "Exploring" => "exploration", _ => Draft.Category.ToLowerInvariant() };
        var payload = new JObject { ["title"] = Draft.Name, ["description"] = Draft.Description,
            ["startsAt"] = start, ["endsAt"] = end, ["category"] = category,
            ["accessType"] = Draft.Visibility.Equals("Group", StringComparison.OrdinalIgnoreCase) ? "group" : Draft.Visibility.Equals("Public", StringComparison.OrdinalIgnoreCase) ? "public" : "invalid",
            ["sendCreationNotification"] = Draft.SendNotification, ["isDraft"] = false,
            ["platforms"] = new JArray(MapPlatforms(Draft.Platforms) ?? new()),
            ["languages"] = new JArray(Draft.Languages), ["tags"] = new JArray(Draft.Tags), ["roleIds"] = new JArray() };
        return new(Draft.Id.ToString(), _apiService.CurrentUserId ?? "", group, "event", Draft.Name, Draft.Description,
            payload["accessType"]!.Value<string>()!, Draft.SendNotification, Draft.ThumbnailPath, payload.ToString(Formatting.None), TimeZoneId: Draft.TimeZoneId);
    }
    [RelayCommand]
    private async Task PublishEventAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var source = Draft;
            CalendarEvent? approved = null;
            string? approvedGroup = null;
            async Task<bool> ConfirmEvent(PublicationPreview preview)
            {
                approved = System.Text.Json.JsonSerializer.Deserialize<CalendarEvent>(System.Text.Json.JsonSerializer.Serialize(source));
                approvedGroup = preview.Draft.GroupId;
                return await ConfirmPublication(preview);
            }
            var report = await Publisher.PublishAsync(CurrentEvent, ConfirmEvent);
            StatusMessage = report.ToString();
            // Persist the approved snapshot, never a different editor object selected during HTTP.
            if (report.Publication.State == "Confirmed" && approved != null)
            {
                approved.GroupId = approvedGroup!; approved.ExternalId = report.Publication.TargetId;
                approved.ExternalImageId = report.Upload.TargetId;
                source.ExternalId = approved.ExternalId; source.ExternalImageId = approved.ExternalImageId;
                await _eventService.AddOrUpdateEventAsync(approved);
                Events = new ObservableCollection<CalendarEvent>(await _eventService.GetEventsAsync());
            }
        }
        catch (ArgumentException ex) { StatusMessage = ex.Message; }
        catch { StatusMessage = "Publication stopped / local save failed. Retained durable effect history blocks repeats; check VRChat. No automatic retry."; }
        finally { IsBusy = false; }
    }
}
