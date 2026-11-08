using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;
using VRCGroupTools.Helpers;
using VRCGroupTools.Models;
using Microsoft.Extensions.DependencyInjection;

namespace VRCGroupTools.ViewModels;

public partial class GameLogViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly GroupInviteLedger _ledger;
    private readonly ICacheService? _cache;
    private readonly VRCGroupTools.Data.IDatabaseService? _database;
    [ObservableProperty] private string _groupContext = "No group loaded";
    [ObservableProperty] private ObservableCollection<GroupInviteRecord> _inviteHistory = new();
    private string? _contextGroupId;
    public string? SelectedGroupId => _settingsService.CurrentGroupId ?? _settingsService.Settings.GroupId;


    [ObservableProperty]
    private ObservableCollection<GameLogUserViewModel> _seenUsers = new();

    [ObservableProperty]
    private ObservableCollection<GameLogUserViewModel> _filteredUsers = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _only18Plus;

    [ObservableProperty]
    private bool _hideAlreadyInGroup;

    [ObservableProperty]
    private bool _hidePriorInvites;
    partial void OnHidePriorInvitesChanged(bool value) => ApplyFilters();

    [ObservableProperty]
    private int _totalUsersFound;

    public GameLogViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService,
        GroupInviteLedger? ledger = null,
        ICacheService? cache = null,
        VRCGroupTools.Data.IDatabaseService? database = null)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _ledger = ledger ?? new GroupInviteLedger();
        _cache = cache;
        _database = database;
        _statusMessage = "Click 'Scan Game Log' to load recently seen users";
    }

    [RelayCommand]
    private async Task RefreshGroupAsync() => await RefreshGroupContextAsync();

    public async Task RefreshGroupContextAsync()
    {
        var groupId = SelectedGroupId;
        if (groupId != _contextGroupId) { DeselectAll(); foreach (var row in SeenUsers) { row.Membership = GroupMembershipState.Unknown; row.InviteState = GroupInviteState.None; } }
        _contextGroupId = groupId;
        InviteHistory.Clear();
        if (string.IsNullOrWhiteSpace(groupId)) { GroupContext = "Select a group first"; ApplyFilters(); return; }
        try
        {
            var snapshot = await _ledger.ReadMembershipAsync(groupId);
            // Legacy caches prove presence only; never infer non-membership from missing cache rows.
            if (snapshot == null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (_cache != null) foreach (var member in await _cache.LoadAsync<List<GroupMember>>($"group_members_{groupId}") ?? new()) ids.Add(member.UserId);
                if (_database != null) foreach (var member in await _database.GetGroupMembersAsync(groupId)) ids.Add(member.UserId);
                snapshot = new(groupId, ids, DateTime.MinValue, false);
            }
            var history = await _ledger.ReadAsync(groupId);
            if (SelectedGroupId != groupId) return;
            foreach (var record in history) InviteHistory.Add(record);
            var states = history.ToDictionary(r => r.UserId, r => r.State, StringComparer.Ordinal);
            foreach (var row in SeenUsers)
            {
                row.Membership = GroupInviteEligibility.Membership(row.User.UserId, snapshot);
                row.InviteState = states.GetValueOrDefault(row.User.UserId, GroupInviteState.None);
            }
            GroupContext = $"Group: {groupId} · Membership cache: {(snapshot.IsComplete ? snapshot.CapturedUtc.ToLocalTime().ToString("g") : "incomplete/unknown — Sync members")} · History: {history.Count}";
            ApplyFilters();
        }
        catch (Exception ex) { foreach (var row in SeenUsers) row.Membership = GroupMembershipState.Unknown; GroupContext = $"Local data unavailable: {ex.Message}"; ApplyFilters(); }
    }

    [RelayCommand]
    private async Task SyncMembersAsync()
    {
        var groupId = SelectedGroupId;
        if (IsLoading || string.IsNullOrWhiteSpace(groupId)) return;
        try { IsLoading = true; await _apiService.GetGroupMembersAsync(groupId); await RefreshGroupContextAsync(); }
        catch (Exception ex) { StatusMessage = $"Membership sync failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnOnly18PlusChanged(bool value)
    {
        ApplyFilters();
    }

    partial void OnHideAlreadyInGroupChanged(bool value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ScanGameLogAsync();
    }

    [RelayCommand]
    private async Task ScanGameLogAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Scanning VRChat game log...";
            SeenUsers.Clear();
            FilteredUsers.Clear();

            var users = await Task.Run(() => ParseGameLog());
            TotalUsersFound = users.Count;

            if (users.Count == 0)
            {
                StatusMessage = "No users found in game log. Make sure VRChat has been running.";
                return;
            }

            StatusMessage = $"Found {users.Count} users. Fetching details...";

            // Add users to collection
            foreach (var user in users)
            {
                SeenUsers.Add(new GameLogUserViewModel(user, this));
            }

            ApplyFilters();

            // Fetch additional details from API
            await FetchUserDetailsAsync();
            await RefreshGroupContextAsync();

            StatusMessage = $"Loaded {SeenUsers.Count} recently seen users";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning game log: {ex.Message}";
            MessageBox.Show($"Failed to scan game log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private List<SeenUser> ParseGameLog()
    {
        var users = new List<SeenUser>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
            var vrchatLogPath = Path.Combine(appDataLocal, "VRChat", "VRChat");

            if (!Directory.Exists(vrchatLogPath))
            {
                Console.WriteLine("[GAME-LOG] VRChat log directory not found");
                return users;
            }

            // Get all recent log files (last 7 days worth)
            var logFiles = Directory.GetFiles(vrchatLogPath, "output_log_*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(10) // Check last 10 log files
                .ToList();

            if (logFiles.Count == 0)
            {
                Console.WriteLine("[GAME-LOG] No VRChat log files found");
                return users;
            }

            Console.WriteLine($"[GAME-LOG] Found {logFiles.Count} log files to scan");

            // Regex patterns
            var onPlayerJoinedPattern = new Regex(@"OnPlayerJoined\s+(.+?)(?:\s*$)", RegexOptions.Compiled);
            var playerJoinedPattern = new Regex(@"\[Player\]\s+(.+?)\s+joined", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var userIdPattern = new Regex(@"usr_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.Compiled);
            var timestampRegex = new Regex(@"^(\d{4}\.\d{2}\.\d{2}\s\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);
            var worldJoinPattern = new Regex(@"Joining\s+wrld_[a-f0-9-]+", RegexOptions.Compiled);

            string? currentWorldId = null;

            foreach (var logFile in logFiles)
            {
                try
                {
                    Console.WriteLine($"[GAME-LOG] Scanning: {Path.GetFileName(logFile)}");

                    using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Track world changes
                        if (line.Contains("[Behaviour]") && line.Contains("Joining"))
                        {
                            var worldMatch = worldJoinPattern.Match(line);
                            if (worldMatch.Success)
                            {
                                currentWorldId = worldMatch.Value.Replace("Joining ", "");
                            }
                        }

                        // Look for OnPlayerJoined events
                        if (line.Contains("OnPlayerJoined"))
                        {
                            var match = onPlayerJoinedPattern.Match(line);
                            if (match.Success && match.Groups.Count > 1)
                            {
                                var displayName = match.Groups[1].Value.Trim();

                                if (!string.IsNullOrWhiteSpace(displayName) && !seenNames.Contains(displayName))
                                {
                                    seenNames.Add(displayName);

                                    var userIdMatch = userIdPattern.Match(line);
                                    var userId = userIdMatch.Success ? userIdMatch.Value : string.Empty;

                                    var timestampMatch = timestampRegex.Match(line);
                                    var seenTime = DateTime.MinValue;
                                    if (timestampMatch.Success)
                                    {
                                        DateTime.TryParseExact(timestampMatch.Groups[1].Value, "yyyy.MM.dd HH:mm:ss",
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out seenTime);
                                    }

                                    users.Add(new SeenUser
                                    {
                                        DisplayName = displayName,
                                        UserId = userId,
                                        LastSeenAt = seenTime,
                                        WorldId = currentWorldId
                                    });
                                }
                            }
                        }

                        // Also look for alternate "[Player] X joined" format
                        if (line.Contains("[Player]") && line.Contains("joined"))
                        {
                            var match = playerJoinedPattern.Match(line);
                            if (match.Success && match.Groups.Count > 1)
                            {
                                var displayName = match.Groups[1].Value.Trim();

                                if (!string.IsNullOrWhiteSpace(displayName) && !seenNames.Contains(displayName))
                                {
                                    seenNames.Add(displayName);

                                    var timestampMatch = timestampRegex.Match(line);
                                    var seenTime = DateTime.MinValue;
                                    if (timestampMatch.Success)
                                    {
                                        DateTime.TryParseExact(timestampMatch.Groups[1].Value, "yyyy.MM.dd HH:mm:ss",
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out seenTime);
                                    }

                                    users.Add(new SeenUser
                                    {
                                        DisplayName = displayName,
                                        UserId = string.Empty,
                                        LastSeenAt = seenTime,
                                        WorldId = currentWorldId
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GAME-LOG] Error reading {Path.GetFileName(logFile)}: {ex.Message}");
                }
            }

            Console.WriteLine($"[GAME-LOG] Total unique users found: {users.Count}");

            // Sort by last seen time (most recent first)
            return users.OrderByDescending(u => u.LastSeenAt).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GAME-LOG] Error parsing game log: {ex.Message}");
            return users;
        }
    }

    private async Task FetchUserDetailsAsync()
    {
        if (!_apiService.IsLoggedIn || SeenUsers.Count == 0) return;

        var usersToFetch = SeenUsers.ToList();
        var completed = 0;

        using var semaphore = new System.Threading.SemaphoreSlim(5);

        var tasks = usersToFetch.Select(async userVM =>
        {
            await semaphore.WaitAsync();
            try
            {
                // If we don't have a UserId, search by name
                if (string.IsNullOrEmpty(userVM.User.UserId))
                {
                    try
                    {
                        var searchResults = await _apiService.SearchUsersAsync(userVM.User.DisplayName);
                        var matches = searchResults.Where(u =>
                            u.DisplayName.Equals(userVM.User.DisplayName, StringComparison.OrdinalIgnoreCase)).ToList();
                        // Display names are not unique identities. Never select an arbitrary ban/invite target.
                        var match = matches.Count == 1 ? matches[0] : null;

                        if (match != null)
                        {
                            userVM.User.UserId = match.UserId;
                            userVM.User.ProfilePicUrl = match.ProfilePicUrl ?? string.Empty;
                            userVM.User.IsAgeVerified = match.IsAgeVerified;
                            userVM.User.Tags = match.Tags ?? new List<string>();
                        }
                    }
                    catch (Exception searchEx)
                    {
                        Console.WriteLine($"[GAME-LOG] Search failed for {userVM.User.DisplayName}: {searchEx.Message}");
                    }
                }
                else
                {
                    // Fetch full details
                    var details = await _apiService.GetUserAsync(userVM.User.UserId);
                    if (details != null)
                    {
                        userVM.User.ProfilePicUrl = details.ProfilePicUrl ?? string.Empty;
                        userVM.User.IsAgeVerified = details.IsAgeVerified;
                        userVM.User.Tags = details.Tags ?? new List<string>();
                    }
                }

                completed++;
                if (completed % 10 == 0)
                {
                    StatusMessage = $"Fetching user details... {completed}/{usersToFetch.Count}";
                }

                // Notify property changes
                userVM.OnPropertiesChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GAME-LOG] Failed to fetch details for {userVM.User.DisplayName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        FilteredUsers.Clear();

        var query = SearchQuery?.ToLowerInvariant() ?? string.Empty;

        var filtered = SeenUsers.Where(u =>
        {
            if (HideAlreadyInGroup && u.Membership != GroupMembershipState.CachedNonMember) return false;
            if (HidePriorInvites && u.InviteState is GroupInviteState.Sent or GroupInviteState.Unknown or GroupInviteState.InFlight) return false;
            // Apply 18+ filter
            if (Only18Plus && !u.User.IsAgeVerified)
                return false;

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(query))
            {
                var matchesName = u.User.DisplayName.ToLowerInvariant().Contains(query);
                if (!matchesName)
                    return false;
            }

            return true;
        });

        foreach (var user in filtered)
        {
            FilteredUsers.Add(user);
        }
        foreach (var user in SeenUsers.Where(u => !FilteredUsers.Contains(u) || !u.CanInvite)) user.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        var visible = FilteredUsers.ToHashSet();
        foreach (var user in SeenUsers)
        {
            user.IsSelected = visible.Contains(user) && user.CanInvite;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var user in SeenUsers)
        {
            user.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = SeenUsers.Count(u => u.IsSelected);
    }

    public Func<string, IReadOnlyList<string>, Task<bool>> ConfirmGroupInvites { get; set; } = (group, users) => Task.FromResult(
        MessageBox.Show($"Send {users.Count} group invitations to {group}?\n\n{string.Join("\n", users)}\n\nPrior sent/pending/unknown invitations are blocked locally.", "Confirm group invitations", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    public Func<Task> InvitePacing { get; set; } = () => Task.Delay(500);

    [ObservableProperty] private GroupBanTarget? _actionTarget;
    public bool HasActionTarget => ActionTarget != null;
    partial void OnActionTargetChanged(GroupBanTarget? value) => OnPropertyChanged(nameof(HasActionTarget));
    [RelayCommand]
    private void OpenUserActions(GameLogUserViewModel? row)
    {
        if (row == null || !row.HasUserId || string.IsNullOrWhiteSpace(SelectedGroupId)) { StatusMessage = "Select a group and a resolved user identity first."; return; }
        ActionTarget = new(SelectedGroupId!, row.User.UserId, row.User.DisplayName, _apiService.CurrentUserId ?? "");
        StatusMessage = "User actions opened. No moderation action has been sent.";
    }
    [RelayCommand] private void CloseUserActions() => ActionTarget = null;
    public Func<GroupBanTarget, Task<bool>>? CheckBanPermission { get; set; }
    public Func<GroupBanTarget, Task<ModerationActionRequest?>> ConfirmGroupBan { get; set; } = async target =>
    {
        if (MessageBox.Show($"Ban {target.DisplayName} ({target.UserId}) from group {target.GroupId}?\n\nThis also applies to non-members. Continue to the moderation reason dialog?", "Confirm group ban", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return null;
        return await ModerationDialogHelper.ShowModerationDialogAsync("ban", target.GroupId, target.UserId, target.DisplayName);
    };
    public Func<GroupBanTarget, ModerationActionRequest, Task<bool>>? ExecuteGroupBan { get; set; }
    [RelayCommand]
    private async Task BanLogUserAsync()
    {
        var target = ActionTarget;
        if (IsLoading || target == null) return;
        IsLoading = true;
        try
        {
            StatusMessage = await GroupBanWorkflow.RunAsync(target,
                () => ActionTarget == null ? null : ActionTarget with { GroupId = SelectedGroupId ?? "", ActingUserId = _apiService.CurrentUserId ?? "" },
                CheckBanPermission ?? (t => GroupBanWorkflow.CanBanAsync(_apiService, t)),
                ConfirmGroupBan,
                ExecuteGroupBan ?? ((t, request) => ModerationDialogHelper.ExecuteModerationActionAsync(t.GroupId, request, _apiService, App.Services.GetRequiredService<IModerationService>())));
        }
        catch (Exception ex) { StatusMessage = $"Ban not confirmed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task InviteToGroupAsync()
    {
        if (IsLoading) return;
        var groupId = SelectedGroupId;
        if (string.IsNullOrWhiteSpace(groupId)) { StatusMessage = "Select a group first"; return; }
        IsLoading = true;
        try
        {
            await RefreshGroupContextAsync();
            var selected = FilteredUsers.Where(u => u.IsSelected && u.CanInvite).GroupBy(u => u.User.UserId, StringComparer.Ordinal).Select(g => g.First()).ToList();
            if (selected.Count == 0) { StatusMessage = "Select eligible visible non-members. Unknown membership requires a member sync."; return; }
            if (!await ConfirmGroupInvites(groupId, selected.Select(u => $"{u.User.DisplayName} ({u.User.UserId})").ToList())) { StatusMessage = "Invitations cancelled"; return; }
            int sent = 0, failed = 0, suppressed = 0;
            foreach (var row in selected)
            {
                await InvitePacing();
                if (SelectedGroupId != groupId) { StatusMessage = "Selected group changed. Remaining invitations not sent."; return; }
                if (!FilteredUsers.Contains(row) || !row.IsSelected || !row.CanInvite) continue;
                try
                {
                    var success = await _apiService.SendGroupInviteGuardedAsync(groupId, row.User.UserId, () => SelectedGroupId == groupId);
                    row.InviteState = success ? GroupInviteState.Sent : GroupInviteState.Failed;
                    if (success) sent++; else failed++;
                }
                catch (GroupInviteSuppressedException) { suppressed++; row.InviteState = await _ledger.GetStateAsync(groupId, row.User.UserId); }
                row.IsSelected = false;
            }
            StatusMessage = $"Group invitations: {sent} sent, {failed} rejected, {suppressed} suppressed (see history for Sent/InFlight/Unknown). No automatic retries.";
        }
        catch (Exception ex) { StatusMessage = $"Stopped safely: {ex.Message}"; }
        finally { IsLoading = false; await RefreshGroupContextAsync(); UpdateSelectedCount(); }
    }
}

public class SeenUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime LastSeenAt { get; set; }
    public string? WorldId { get; set; }
}

public partial class GameLogUserViewModel : ObservableObject
{
    private readonly GameLogViewModel? _parent;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private GroupMembershipState _membership = GroupMembershipState.Unknown;
    [ObservableProperty]
    private GroupInviteState _inviteState = GroupInviteState.None;
    public bool CanInvite => GroupInviteEligibility.CanSelect(User.UserId, Membership, InviteState);
    public string GroupStatus => $"{Membership} · Invite: {InviteState}";
    partial void OnMembershipChanged(GroupMembershipState value) { OnPropertyChanged(nameof(CanInvite)); OnPropertyChanged(nameof(GroupStatus)); if (!CanInvite) IsSelected = false; }
    partial void OnInviteStateChanged(GroupInviteState value) { OnPropertyChanged(nameof(CanInvite)); OnPropertyChanged(nameof(GroupStatus)); if (!CanInvite) IsSelected = false; }
    public SeenUser User { get; }

    public GameLogUserViewModel(SeenUser user, GameLogViewModel? parent)
    {
        User = user;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent?.UpdateSelectedCount();
    }

    public string AgeVerificationDisplay => User.IsAgeVerified ? "✓ 18+" : "";
    public string LastSeenDisplay => User.LastSeenAt == DateTime.MinValue 
        ? "Unknown" 
        : User.LastSeenAt.ToString("MMM dd, HH:mm");

    public string TrustLevel
    {
        get
        {
            if (User.Tags == null || User.Tags.Count == 0) return "Unknown";
            if (User.Tags.Contains("system_trust_legend")) return "Trusted+";
            if (User.Tags.Contains("system_trust_veteran")) return "Trusted";
            if (User.Tags.Contains("system_trust_trusted")) return "Known";
            if (User.Tags.Contains("system_trust_known")) return "User";
            if (User.Tags.Contains("system_trust_basic")) return "New User";
            return "Visitor";
        }
    }

    public Brush TrustLevelBrush
    {
        get
        {
            return TrustLevel switch
            {
                "Trusted+" => new SolidColorBrush(Color.FromRgb(255, 215, 0)),  // Gold
                "Trusted" => new SolidColorBrush(Color.FromRgb(138, 43, 226)), // Purple
                "Known" => new SolidColorBrush(Color.FromRgb(255, 123, 0)),    // Orange
                "User" => new SolidColorBrush(Color.FromRgb(43, 181, 43)),     // Green
                "New User" => new SolidColorBrush(Color.FromRgb(30, 144, 255)), // Blue
                "Visitor" => new SolidColorBrush(Color.FromRgb(128, 128, 128)), // Gray
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
        }
    }

    public bool HasUserId => !string.IsNullOrEmpty(User.UserId);

    public void OnPropertiesChanged()
    {
        OnPropertyChanged(nameof(AgeVerificationDisplay));
        OnPropertyChanged(nameof(TrustLevel));
        OnPropertyChanged(nameof(TrustLevelBrush));
        OnPropertyChanged(nameof(HasUserId));
        OnPropertyChanged(nameof(CanInvite));
        OnPropertyChanged(nameof(User));
    }
}
