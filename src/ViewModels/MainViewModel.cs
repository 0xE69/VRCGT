using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Models;
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
    private ObservableCollection<GroupConfiguration> _managedGroups = new();

    [ObservableProperty]
    private GroupConfiguration? _selectedGroup;

    [ObservableProperty]
    private bool _isAddingNewGroup;

    [ObservableProperty]
    private string _newGroupId = "";

    [ObservableProperty]
    private string _newGroupName = "";

    [ObservableProperty]
    private bool _isDiscoveringGroups;

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
    private SecuritySettingsViewModel? _securitySettingsVM;

    [ObservableProperty]
    private InstanceCreatorViewModel? _instanceCreatorVM;

    [ObservableProperty]
    private CalendarEventViewModel? _calendarEventVM;

    [ObservableProperty]
    private AppSettingsViewModel? _appSettingsVM;

    [ObservableProperty]
    private GroupInfoViewModel? _groupInfoVM;

    [ObservableProperty]
    private GroupPostsViewModel? _groupPostsVM;

    [ObservableProperty]
    private InviteToGroupViewModel? _inviteToGroupVM;

    [ObservableProperty]
    private MembersListViewModel? _membersListVM;

    [ObservableProperty]
    private BansListViewModel? _bansListVM;

    [ObservableProperty]
    private MemberBackupViewModel? _memberBackupVM;

    [ObservableProperty]
    private KillSwitchViewModel? _killSwitchVM;

    [ObservableProperty]
    private InstanceInviterViewModel? _instanceInviterVM;

    [ObservableProperty]
    private InviterHubViewModel? _inviterHubVM;

    [ObservableProperty]
    private GroupJoinRequestsViewModel? _groupJoinRequestsVM;

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
        
        // Load managed groups
        foreach (var group in _settingsService.ManagedGroups)
        {
            ManagedGroups.Add(group);
        }
        
        // Set current group
        if (!string.IsNullOrEmpty(_settingsService.CurrentGroupId))
        {
            SelectedGroup = ManagedGroups.FirstOrDefault(g => g.GroupId == _settingsService.CurrentGroupId);
            GroupId = _settingsService.CurrentGroupId;
        }
        
        Console.WriteLine("[DEBUG] MainViewModel constructor completed");
    }

    public void Initialize()
    {
        Console.WriteLine("[DEBUG] MainViewModel.Initialize() starting...");
        BadgeScannerVM = App.Services.GetRequiredService<BadgeScannerViewModel>();
        UserSearchVM = App.Services.GetRequiredService<UserSearchViewModel>();
        AuditLogVM = App.Services.GetRequiredService<AuditLogViewModel>();
        DiscordSettingsVM = App.Services.GetRequiredService<DiscordSettingsViewModel>();
        SecuritySettingsVM = App.Services.GetRequiredService<SecuritySettingsViewModel>();
        CalendarEventVM = App.Services.GetRequiredService<CalendarEventViewModel>();
        InstanceCreatorVM = App.Services.GetRequiredService<InstanceCreatorViewModel>();
        GroupInfoVM = App.Services.GetRequiredService<GroupInfoViewModel>();
        GroupPostsVM = App.Services.GetRequiredService<GroupPostsViewModel>();
        InviteToGroupVM = App.Services.GetRequiredService<InviteToGroupViewModel>();
        MembersListVM = App.Services.GetRequiredService<MembersListViewModel>();
        BansListVM = App.Services.GetRequiredService<BansListViewModel>();
        MemberBackupVM = App.Services.GetRequiredService<MemberBackupViewModel>();
        KillSwitchVM = App.Services.GetRequiredService<KillSwitchViewModel>();
        InstanceInviterVM = App.Services.GetRequiredService<InstanceInviterViewModel>();
        AppSettingsVM = App.Services.GetRequiredService<AppSettingsViewModel>();
        InviterHubVM = App.Services.GetRequiredService<InviterHubViewModel>();
        GroupJoinRequestsVM = App.Services.GetRequiredService<GroupJoinRequestsViewModel>();
        
        // Sync group ID to badge scanner and API service
        BadgeScannerVM!.GroupId = GroupId;
        _apiService.CurrentGroupId = GroupId;
        
        // Auto-refresh group info on load
        if (!string.IsNullOrWhiteSpace(GroupId))
        {
            _ = GroupInfoVM!.RefreshCommand.ExecuteAsync(null);
            
            // Start audit log polling automatically (for Discord webhooks)
            Console.WriteLine("[DEBUG] Auto-starting audit log polling for Discord webhooks...");
            _ = AuditLogVM!.InitializeAsync();
        }
        
        Console.WriteLine("[DEBUG] MainViewModel.Initialize() completed");
    }

    partial void OnSelectedGroupChanged(GroupConfiguration? value)
    {
        if (value != null)
        {
            GroupId = value.GroupId;
            _settingsService.CurrentGroupId = value.GroupId;
            _settingsService.Settings.GroupId = value.GroupId; // Sync legacy setting
            _settingsService.Save();
            
            // Update API service
            _apiService.CurrentGroupId = GroupId;
            
            // Update badge scanner if initialized
            if (BadgeScannerVM != null)
            {
                BadgeScannerVM.GroupId = GroupId;
            }
            
            // Auto-refresh group info
            if (GroupInfoVM != null)
            {
                _ = GroupInfoVM.RefreshCommand.ExecuteAsync(null);
            }
            
            // Restart audit log polling for new group
            if (AuditLogVM != null)
            {
                _ = AuditLogVM.InitializeAsync();
            }

            // Refresh join requests
            if (GroupJoinRequestsVM != null)
            {
                _ = GroupJoinRequestsVM.RefreshCommand.ExecuteAsync(null);
            }
            
            Console.WriteLine($"[DEBUG] Switched to group: {value.GroupName} ({value.GroupId})");
        }
    }

    [RelayCommand]
    private void ShowAddGroupDialog()
    {
        IsAddingNewGroup = true;
        NewGroupId = "";
        NewGroupName = "";
    }

    [RelayCommand]
    private void CancelAddGroup()
    {
        IsAddingNewGroup = false;
        NewGroupId = "";
        NewGroupName = "";
    }

    [RelayCommand]
    private async Task AddNewGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupId))
        {
            return;
        }

        // Check if group already exists
        if (ManagedGroups.Any(g => g.GroupId == NewGroupId))
        {
            Console.WriteLine($"[DEBUG] Group {NewGroupId} already exists");
            return;
        }

        // Try to fetch group info from VRChat to validate and get name
        var groupInfo = await _apiService.GetGroupAsync(NewGroupId);
        
        var config = new GroupConfiguration
        {
            GroupId = NewGroupId,
            GroupName = groupInfo != null ? groupInfo.Name : (string.IsNullOrWhiteSpace(NewGroupName) ? "Unknown Group" : NewGroupName),
            GroupIconUrl = groupInfo?.IconUrl,
            AddedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow
        };

        _settingsService.AddOrUpdateGroup(config);
        ManagedGroups.Add(config);
        SelectedGroup = config;
        
        IsAddingNewGroup = false;
        Console.WriteLine($"[DEBUG] Added new group: {config.GroupName} ({config.GroupId})");
    }

    [RelayCommand]
    private void RemoveSelectedGroup()
    {
        if (SelectedGroup == null)
            return;

        var groupToRemove = SelectedGroup;
        _settingsService.RemoveGroup(groupToRemove.GroupId);
        ManagedGroups.Remove(groupToRemove);
        
        // Select first group if available
        SelectedGroup = ManagedGroups.FirstOrDefault();
        
        Console.WriteLine($"[DEBUG] Removed group: {groupToRemove.GroupName} ({groupToRemove.GroupId})");
    }

    [RelayCommand]
    private async Task DiscoverMyGroupsAsync()
    {
        IsDiscoveringGroups = true;
        
        try
        {
            Console.WriteLine("[MainVM] Starting group discovery...");
            LoggingService.Info("MainVM", "Discovering manageable groups...");
            
            var manageableGroups = await _apiService.GetMyManageableGroupsAsync();
            Console.WriteLine($"[MainVM] API returned {manageableGroups.Count} groups");
            
            if (manageableGroups.Count == 0)
            {
                Console.WriteLine("[MainVM] No manageable groups found - discovery complete");
                LoggingService.Info("MainVM", "No manageable groups found");
                return;
            }

            var addedCount = 0;
            foreach (var groupInfo in manageableGroups)
            {
                Console.WriteLine($"[MainVM] Processing group: {groupInfo.Name} ({groupInfo.Id})");
                
                // Skip if already exists
                if (ManagedGroups.Any(g => g.GroupId == groupInfo.Id))
                {
                    Console.WriteLine($"[MainVM]   Skipping - already in list");
                    continue;
                }

                var config = new GroupConfiguration
                {
                    GroupId = groupInfo.Id,
                    GroupName = groupInfo.Name,
                    GroupIconUrl = groupInfo.IconUrl,
                    AddedAt = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow
                };

                _settingsService.AddOrUpdateGroup(config);
                ManagedGroups.Add(config);
                addedCount++;
                Console.WriteLine($"[MainVM]   Added to list");
            }

            Console.WriteLine($"[MainVM] Discovery complete: added {addedCount} new groups (total found: {manageableGroups.Count})");
            LoggingService.Info("MainVM", $"Discovered and added {addedCount} new manageable groups (total found: {manageableGroups.Count})");
            
            // If no group selected, select the first one
            if (SelectedGroup == null && ManagedGroups.Count > 0)
            {
                Console.WriteLine($"[MainVM] Auto-selecting first group");
                SelectedGroup = ManagedGroups.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainVM] ERROR in group discovery: {ex.Message}");
            Console.WriteLine($"[MainVM] Stack trace: {ex.StackTrace}");
            LoggingService.Error("MainVM", $"Error discovering groups: {ex.Message}");
        }
        finally
        {
            IsDiscoveringGroups = false;
            Console.WriteLine("[MainVM] IsDiscoveringGroups set to false");
        }
    }

    [RelayCommand]
    private void SaveGroupId()
    {
        // No longer needed - handled by OnSelectedGroupChanged
        // Keeping for compatibility
    }

    [RelayCommand]
    private void SelectModule(string module)
    {
        Console.WriteLine($"[DEBUG] SelectModule called: {module}");
        Console.WriteLine($"[DEBUG] Current CurrentModule value: {CurrentModule}");
        Console.WriteLine($"[DEBUG] AppSettingsVM null? {AppSettingsVM == null}");
        
        CurrentModule = module;
        
        Console.WriteLine($"[DEBUG] CurrentModule now set to: {CurrentModule}");
        
        // Initialize the Audit Log VM when switching to that module
        if (module == "AuditLogs" && AuditLogVM != null)
        {
            Console.WriteLine("[DEBUG] Initializing AuditLogVM...");
            _ = AuditLogVM.InitializeAsync();  // Fire and forget
        }

        // Initialize GroupJoinRequests when switching to it
        if (module == "GroupJoinRequests" && GroupJoinRequestsVM != null)
        {
            Console.WriteLine("[DEBUG] Refreshing GroupJoinRequestsVM...");
            _ = GroupJoinRequestsVM.RefreshCommand.ExecuteAsync(null);
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
