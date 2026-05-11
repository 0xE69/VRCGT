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

namespace VRCGroupTools.ViewModels;

public partial class GameLogViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;

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
    private int _totalUsersFound;

    public GameLogViewModel(
        IVRChatApiService apiService,
        ISettingsService settingsService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _statusMessage = "Click 'Scan Game Log' to load recently seen users";
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
                        var match = searchResults.FirstOrDefault(u =>
                            u.DisplayName.Equals(userVM.User.DisplayName, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            userVM.User.UserId = match.UserId;
                            userVM.User.ProfilePicUrl = match.ProfilePicUrl;
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
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var user in FilteredUsers)
        {
            user.IsSelected = true;
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

    [RelayCommand]
    private async Task InviteToGroupAsync()
    {
        var selectedUsers = SeenUsers.Where(u => u.IsSelected && !string.IsNullOrEmpty(u.User.UserId)).ToList();
        if (selectedUsers.Count == 0)
        {
            MessageBox.Show("Please select at least one user to invite.\nNote: Users without a resolved User ID cannot be invited.", 
                "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var groupId = _settingsService.Settings.GroupId;
        if (string.IsNullOrEmpty(groupId))
        {
            MessageBox.Show("No group configured. Please set up a group first.", "No Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            var successCount = 0;
            var failCount = 0;

            foreach (var userVM in selectedUsers)
            {
                StatusMessage = $"Inviting {userVM.User.DisplayName} to group...";

                var success = await _apiService.SendGroupInviteAsync(groupId, userVM.User.UserId);

                if (success)
                {
                    successCount++;
                    userVM.IsSelected = false;
                }
                else
                {
                    failCount++;
                }

                // Random delay between 4 and 13 seconds
                var delay = Random.Shared.Next(4000, 13000);
                await Task.Delay(delay);
            }

            StatusMessage = $"Invites sent: {successCount} succeeded, {failCount} failed";
            MessageBox.Show($"Sent {successCount} group invites successfully.\n{failCount} failed.", 
                "Invites Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error sending invites: {ex.Message}";
            MessageBox.Show($"Failed to send invites: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            UpdateSelectedCount();
        }
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
        OnPropertyChanged(nameof(User));
    }
}
