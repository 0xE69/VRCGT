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
    private double _progressPercent;

    [ObservableProperty]
    private string _filterMode = "All";

    [ObservableProperty]
    private ObservableCollection<MemberScanResult> _allResults = new();

    [ObservableProperty]
    private ObservableCollection<MemberScanResult> _filteredResults = new();

    public BadgeScannerViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
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

        // Clean up group ID (extract from URL if needed)
        var cleanGroupId = GroupId;
        if (GroupId.Contains("vrchat.com"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(GroupId, @"grp_[a-f0-9-]+");
            if (match.Success)
                cleanGroupId = match.Value;
        }

        IsScanning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        AllResults.Clear();
        FilteredResults.Clear();
        VerifiedCount = 0;
        UnverifiedCount = 0;
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

            StatusMessage = $"Scan complete! {verified} verified, {unverified} unverified out of {TotalMembers} members";
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
                sb.AppendLine("User ID,Display Name,Age Verified,Badges");

                foreach (var result in AllResults)
                {
                    var verified = result.IsAgeVerified switch
                    {
                        true => "Yes",
                        false => "No",
                        _ => "Unknown"
                    };
                    var badges = string.Join("; ", result.Badges);
                    sb.AppendLine($"\"{result.UserId}\",\"{result.DisplayName}\",\"{verified}\",\"{badges}\"");
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

public class MemberScanResult
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool? IsAgeVerified { get; set; }
    public List<string> Badges { get; set; } = new();

    public string VerificationStatus => IsAgeVerified switch
    {
        true => "✓ Verified",
        false => "✗ Not Verified",
        _ => "? Unknown"
    };

    public string StatusColor => IsAgeVerified switch
    {
        true => "#4CAF50",
        false => "#F44336",
        _ => "#9E9E9E"
    };
}
