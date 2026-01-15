using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class BadgeScannerViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _groupId = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Ready to scan";

    [ObservableProperty]
    private int _totalMembers;

    [ObservableProperty]
    private int _scannedMembers;

    [ObservableProperty]
    private int _verifiedCount;

    [ObservableProperty]
    private int _unverifiedCount;

    [ObservableProperty]
    private int _kickedCount;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _filterMode = "All";

    [ObservableProperty]
    private bool _isKicking;

    [ObservableProperty]
    private ObservableCollection<MemberScanResult> _allResults = new();

    [ObservableProperty]
    private ObservableCollection<MemberScanResult> _filteredResults = new();

    public BadgeScannerViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
        GroupId = _settingsService.Settings.GroupId ?? "";
    }

    partial void OnFilterModeChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredResults.Clear();
        var filtered = FilterMode switch
        {
            "Verified" => AllResults.Where(r => r.IsAgeVerified == true),
            "Unverified" => AllResults.Where(r => r.IsAgeVerified == false),
            "Unknown" => AllResults.Where(r => r.IsAgeVerified == null),
            _ => AllResults
        };

        foreach (var item in filtered)
        {
            FilteredResults.Add(item);
        }
    }

    [RelayCommand]
    private async Task StartScanAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
        {
            StatusMessage = "Please enter a Group ID";
            return;
        }

        var cleanGroupId = NormalizeGroupId(GroupId);

        IsScanning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        AllResults.Clear();
        FilteredResults.Clear();
        VerifiedCount = 0;
        UnverifiedCount = 0;
        KickedCount = 0;
        ScannedMembers = 0;
        TotalMembers = 0;
        ProgressPercent = 0;

        StatusMessage = "Fetching group members...";

        try
        {
            var members = await _apiService.GetGroupMembersAsync(cleanGroupId, (count, _) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Found {count} members...";
                });
            });

            if (members.Count == 0)
            {
                StatusMessage = "No members found or invalid Group ID";
                IsScanning = false;
                return;
            }

            TotalMembers = members.Count;
            StatusMessage = $"Scanning {TotalMembers} members for 18+ badge...";

            int verified = 0, unverified = 0;

            foreach (var member in members)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                var userDetails = await _apiService.GetUserAsync(member.UserId);
                ScannedMembers++;
                ProgressPercent = (double)ScannedMembers / TotalMembers * 100;

                var result = new MemberScanResult
                {
                    UserId = member.UserId,
                    DisplayName = userDetails?.DisplayName ?? member.DisplayName,
                    IsAgeVerified = userDetails?.IsAgeVerified,
                    Badges = userDetails?.Badges ?? new List<string>()
                };

                if (result.IsAgeVerified == true)
                    verified++;
                else if (result.IsAgeVerified == false)
                    unverified++;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AllResults.Add(result);
                    VerifiedCount = verified;
                    UnverifiedCount = unverified;
                    StatusMessage = $"Scanned {ScannedMembers}/{TotalMembers} - {verified} verified, {unverified} unverified";
                    ApplyFilter();
                });
            }

            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await _cacheService.SaveAsync($"badge_scan_{cleanGroupId}", AllResults.ToList());
                StatusMessage = $"Scan complete! {verified} verified, {unverified} unverified out of {TotalMembers} members";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Scan cancelled";
    }

    [RelayCommand]
    private async Task LoadFromCacheAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
        {
            StatusMessage = "Please enter a Group ID";
            return;
        }

        var cleanGroupId = NormalizeGroupId(GroupId);
        StatusMessage = "Loading cached scan results...";
        var cached = await _cacheService.LoadAsync<List<MemberScanResult>>($"badge_scan_{cleanGroupId}");
        if (cached == null || cached.Count == 0)
        {
            StatusMessage = "No cached scan results found";
            return;
        }

        AllResults.Clear();
        FilteredResults.Clear();

        foreach (var item in cached)
        {
            AllResults.Add(item);
        }

        TotalMembers = AllResults.Count;
        ScannedMembers = AllResults.Count;
        VerifiedCount = AllResults.Count(r => r.IsAgeVerified == true);
        UnverifiedCount = AllResults.Count(r => r.IsAgeVerified == false);
        KickedCount = AllResults.Count(r => r.WasKicked);
        ProgressPercent = TotalMembers > 0 ? 100 : 0;
        ApplyFilter();

        StatusMessage = $"Loaded {AllResults.Count} cached results";
    }

    private static string NormalizeGroupId(string groupId)
    {
        var cleanGroupId = groupId;
        if (groupId.Contains("vrchat.com"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(groupId, @"grp_[a-f0-9-]+");
            if (match.Success)
                cleanGroupId = match.Value;
        }

        return cleanGroupId;
    }

    [RelayCommand]
    private void SelectAllUnverified()
    {
        foreach (var result in AllResults.Where(r => r.IsAgeVerified == false && !r.WasKicked))
        {
            result.IsSelected = true;
        }
        StatusMessage = $"Selected {AllResults.Count(r => r.IsSelected)} unverified members";
    }

    [RelayCommand]
    private void UnselectAll()
    {
        foreach (var result in AllResults)
        {
            result.IsSelected = false;
        }
        StatusMessage = "Cleared selection";
    }

    [RelayCommand]
    private void CopyUnverifiedToClipboard()
    {
        var unverified = AllResults.Where(r => r.IsAgeVerified == false && !r.WasKicked).ToList();
        if (unverified.Count == 0)
        {
            StatusMessage = "No unverified members to copy";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Unverified Members ({unverified.Count}):");
        sb.AppendLine(new string('-', 50));
        
        foreach (var member in unverified)
        {
            sb.AppendLine($"{member.DisplayName} | {member.UserId}");
        }

        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            StatusMessage = $"Copied {unverified.Count} unverified members to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy to clipboard: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyFilteredToClipboard()
    {
        if (FilteredResults.Count == 0)
        {
            StatusMessage = "No results to copy";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Filtered Results ({FilteredResults.Count}):");
        sb.AppendLine(new string('-', 50));
        
        foreach (var member in FilteredResults)
        {
            var status = member.IsAgeVerified switch
            {
                true => "Verified",
                false when member.WasKicked => "Kicked",
                false => "Not Verified",
                _ => "Unknown"
            };
            sb.AppendLine($"{member.DisplayName} | {member.UserId} | {status}");
        }

        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            StatusMessage = $"Copied {FilteredResults.Count} results to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy to clipboard: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task KickSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
        {
            StatusMessage = "Please enter a Group ID";
            return;
        }

        var selected = AllResults.Where(r => r.IsSelected && !r.WasKicked).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No members selected to kick";
            return;
        }

        var cleanGroupId = NormalizeGroupId(GroupId);

        IsKicking = true;
        StatusMessage = $"Kicking {selected.Count} selected members...";

        int kicked = 0;
        int failed = 0;

        foreach (var member in selected)
        {
            try
            {
                var success = await _apiService.KickGroupMemberAsync(cleanGroupId, member.UserId);
                if (success)
                {
                    member.WasKicked = true;
                    member.IsSelected = false;
                    kicked++;
                    KickedCount++;
                    UnverifiedCount--;
                    Console.WriteLine($"[BADGE-SCAN] Kicked: {member.DisplayName} ({member.UserId})");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"[BADGE-SCAN] Failed to kick: {member.DisplayName} ({member.UserId})");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[BADGE-SCAN] Kick error for {member.DisplayName}: {ex.Message}");
            }

            StatusMessage = $"Kicking... {kicked} kicked, {failed} failed";
            await Task.Delay(500); // Rate limiting
        }

        IsKicking = false;
        StatusMessage = $"Kick complete! {kicked} kicked, {failed} failed";
        ApplyFilter();
    }

    [RelayCommand]
    private async Task KickAllUnverifiedAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupId))
        {
            StatusMessage = "Please enter a Group ID";
            return;
        }

        var unverified = AllResults.Where(r => r.IsAgeVerified == false && !r.WasKicked).ToList();
        if (unverified.Count == 0)
        {
            StatusMessage = "No unverified members to kick";
            return;
        }

        var cleanGroupId = NormalizeGroupId(GroupId);

        IsKicking = true;
        StatusMessage = $"Kicking {unverified.Count} unverified members...";

        int kicked = 0;
        int failed = 0;

        foreach (var member in unverified)
        {
            try
            {
                var success = await _apiService.KickGroupMemberAsync(cleanGroupId, member.UserId);
                if (success)
                {
                    member.WasKicked = true;
                    kicked++;
                    KickedCount++;
                    UnverifiedCount--;
                    Console.WriteLine($"[BADGE-SCAN] Kicked: {member.DisplayName} ({member.UserId})");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"[BADGE-SCAN] Failed to kick: {member.DisplayName} ({member.UserId})");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[BADGE-SCAN] Kick error for {member.DisplayName}: {ex.Message}");
            }

            StatusMessage = $"Kicking... {kicked} kicked, {failed} failed";
            await Task.Delay(500); // Rate limiting
        }

        IsKicking = false;
        StatusMessage = $"Kick complete! {kicked} kicked, {failed} failed";
        ApplyFilter();
    }

    [RelayCommand]
    private void ExportToCSV()
    {
        if (AllResults.Count == 0)
        {
            StatusMessage = "No results to export";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"VRC_AgeVerification_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            InitialDirectory = _settingsService.Settings.LastExportPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("User ID,Display Name,Age Verified,Kicked,Badges");

                foreach (var result in AllResults)
                {
                    var verified = result.IsAgeVerified switch
                    {
                        true => "Yes",
                        false => "No",
                        _ => "Unknown"
                    };
                    var kicked = result.WasKicked ? "Yes" : "No";
                    var badges = string.Join("; ", result.Badges);
                    sb.AppendLine($"\"{result.UserId}\",\"{result.DisplayName}\",\"{verified}\",\"{kicked}\",\"{badges}\"");
                }

                File.WriteAllText(dialog.FileName, sb.ToString());
                _settingsService.Settings.LastExportPath = Path.GetDirectoryName(dialog.FileName);
                _settingsService.Save();

                StatusMessage = $"Exported {AllResults.Count} results to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }
    }
}

public partial class MemberScanResult : ObservableObject
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool? IsAgeVerified { get; set; }
    public List<string> Badges { get; set; } = new();
    
    [ObservableProperty]
    private bool _isSelected;
    
    [ObservableProperty]
    private bool _wasKicked;

    public string VerificationStatus => IsAgeVerified switch
    {
        true => "✓ Verified",
        false when WasKicked => "✗ Kicked",
        false => "✗ Not Verified",
        _ => "? Unknown"
    };

    public string StatusColor => IsAgeVerified switch
    {
        true => "#4CAF50",
        false when WasKicked => "#FF9800",
        false => "#F44336",
        _ => "#9E9E9E"
    };
}
