using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class OpenInstanceAnnouncementsViewModel : ObservableObject
{
    private readonly OpenInstanceAnnouncementService _service;
    private readonly ISettingsService _settings;
    [ObservableProperty] private string _groupId = "";
    [ObservableProperty] private bool _optIn;
    [ObservableProperty] private string _status = "";
    public OpenInstanceAnnouncementsViewModel(OpenInstanceAnnouncementService service, ISettingsService settings)
    { _service = service; _settings = settings; RefreshGroup(); }
    public void RefreshGroup()
    {
        _service.Stop(); GroupId = _settings.CurrentGroupId ?? "";
        OnPropertyChanged(nameof(Ready));
        OptIn = !string.IsNullOrEmpty(GroupId) && _service.GetConfiguration(GroupId).Enabled;
        Status = "Paused for " + GroupId + ". Webhook is never inherited from another group or global audit settings.";
    }
    private bool Current() => !string.IsNullOrEmpty(GroupId) && GroupId == _settings.CurrentGroupId;
    // PasswordBox calls this directly; the webhook is never a bindable/exported/loggable property.
    public void SaveSecret(string secret)
    {
        if (!Current()) { RefreshGroup(); return; }
        try { _service.Configure(GroupId, secret, OptIn); Status = _service.Status; }
        catch (ArgumentException ex) { Status = ex.Message; }
        catch { Status = "Could not securely save configuration. No announcements started."; }
    }
    [RelayCommand] private void Start() { if (!Current()) { RefreshGroup(); return; } _service.Start(GroupId); Status = _service.Status; }
    [RelayCommand] private void Pause() { _service.Stop(); RefreshStatus(); }
    [RelayCommand] private void Disable() { if (Current()) _service.Disable(GroupId); OptIn = false; RefreshStatus(); }
    public IReadOnlyList<AnnouncementPreview> Ready => _service.Ready;
    [RelayCommand] private void RefreshStatus() { Status = _service.Status; OnPropertyChanged(nameof(Ready)); }
    [RelayCommand] private async Task ReviewReadyAsync()
    {
        if (!Current()) { RefreshGroup(); return; }
        await _service.PollOnceAsync(GroupId, approve: preview => Task.FromResult(VRCGroupTools.Views.PublicationApprovalWindow.ConfirmAnnouncement(preview)), explicitReview: true);
        RefreshStatus();
    }
    partial void OnOptInChanged(bool value) { if (!value && Current()) { _service.Disable(GroupId); Status = _service.Status; } }
}
