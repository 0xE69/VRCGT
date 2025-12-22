using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;

    [ObservableProperty]
    private string _welcomeMessage = "Welcome!";

    [ObservableProperty]
    private string? _userProfilePicUrl;

    [ObservableProperty]
    private string _groupId = "";

    [ObservableProperty]
    private string _currentModule = "GroupInfo";

    [ObservableProperty]
    private BadgeScannerViewModel? _badgeScannerVM;

    [ObservableProperty]
    private UserSearchViewModel? _userSearchVM;

    [ObservableProperty]
    private AuditLogViewModel? _auditLogVM;

    [ObservableProperty]
    private DiscordSettingsViewModel? _discordSettingsVM;

    [ObservableProperty]
    private InstanceCreatorViewModel? _instanceCreatorVM;

    [ObservableProperty]
    private CalendarEventViewModel? _calendarEventVM;

    [ObservableProperty]
    private AppSettingsViewModel? _appSettingsVM;

    [ObservableProperty]
    private GroupInfoViewModel? _groupInfoVM;

    [ObservableProperty]
    private CreatePostViewModel? _createPostVM;

    [ObservableProperty]
    private InviteToGroupViewModel? _inviteToGroupVM;

    [ObservableProperty]
    private MembersListViewModel? _membersListVM;

    [ObservableProperty]
    private BansListViewModel? _bansListVM;

    public string AppVersion => $"v{App.Version}";

    public event Action? LogoutRequested;

    public MainViewModel()
    {
        Console.WriteLine("[DEBUG] MainViewModel constructor starting...");
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();

        WelcomeMessage = $"Welcome, {_apiService.CurrentUserDisplayName}!";
        UserProfilePicUrl = _apiService.CurrentUserProfilePicUrl;
        GroupId = _settingsService.Settings.GroupId ?? "";
        Console.WriteLine("[DEBUG] MainViewModel constructor completed");
    }

    public void Initialize()
    {
        Console.WriteLine("[DEBUG] MainViewModel.Initialize() starting...");
        BadgeScannerVM = App.Services.GetRequiredService<BadgeScannerViewModel>();
        UserSearchVM = App.Services.GetRequiredService<UserSearchViewModel>();
        AuditLogVM = App.Services.GetRequiredService<AuditLogViewModel>();
        DiscordSettingsVM = App.Services.GetRequiredService<DiscordSettingsViewModel>();
        CalendarEventVM = App.Services.GetRequiredService<CalendarEventViewModel>();
        InstanceCreatorVM = App.Services.GetRequiredService<InstanceCreatorViewModel>();
        GroupInfoVM = App.Services.GetRequiredService<GroupInfoViewModel>();
        CreatePostVM = App.Services.GetRequiredService<CreatePostViewModel>();
        InviteToGroupVM = App.Services.GetRequiredService<InviteToGroupViewModel>();
        MembersListVM = App.Services.GetRequiredService<MembersListViewModel>();
        BansListVM = App.Services.GetRequiredService<BansListViewModel>();
        AppSettingsVM = App.Services.GetRequiredService<AppSettingsViewModel>();
        
        // Sync group ID to badge scanner and API service
        BadgeScannerVM!.GroupId = GroupId;
        _apiService.CurrentGroupId = GroupId;
        Console.WriteLine("[DEBUG] MainViewModel.Initialize() completed");
    }

    [RelayCommand]
    private void SaveGroupId()
    {
        _settingsService.Settings.GroupId = GroupId;
        _settingsService.Save();
        
        // Sync to badge scanner and API service
        BadgeScannerVM!.GroupId = GroupId;
        _apiService.CurrentGroupId = GroupId;
    }

    [RelayCommand]
    private void SelectModule(string module)
    {
        Console.WriteLine($"[DEBUG] SelectModule called: {module}");
        CurrentModule = module;
        
        // Initialize the Audit Log VM when switching to that module
        if (module == "AuditLogs" && AuditLogVM != null)
        {
            Console.WriteLine("[DEBUG] Initializing AuditLogVM...");
            _ = AuditLogVM.InitializeAsync();  // Fire and forget
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // Clear cached session on logout
        await _cacheService.DeleteAsync("session");
        _apiService.Logout();
        LogoutRequested?.Invoke();
    }
}
