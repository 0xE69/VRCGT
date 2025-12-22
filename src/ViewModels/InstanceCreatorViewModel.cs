using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class InstanceCreatorViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly IVRChatApiService _apiService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private string _worldId = string.Empty;
    [ObservableProperty] private string _worldName = string.Empty;
    [ObservableProperty] private string _worldAuthor = string.Empty;
    [ObservableProperty] private int _worldCapacity;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _region = "US West";
    [ObservableProperty] private DateTime? _scheduledDate = null;
    [ObservableProperty] private string _scheduledTime = "20:00";
    [ObservableProperty] private bool _schedulingEnabled = false;
    [ObservableProperty] private string _groupAccess = "Members";
    [ObservableProperty] private bool _queueEnabled;
    [ObservableProperty] private bool _ageGateEnabled;
    [ObservableProperty] private string _shortName = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _searchWorldId = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private WorldInfo? _selectedWorld;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _timeZoneId = TimeZoneInfo.Local.Id;
    public string TimeZoneDisplay => GetTimeZoneDisplay();

    public ObservableCollection<string> Regions { get; } = new(new[] { "US West", "US East", "Europe", "Japan" });
    public ObservableCollection<string> GroupAccessModes { get; } = new(new[] { "Members", "Plus", "Public" });
    public ObservableCollection<InstancePlan> Plans { get; } = new();
    public ObservableCollection<WorldInfo> SearchResults { get; } = new();

    public InstanceCreatorViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        // Apply defaults from settings
        Region = _settingsService.Settings.DefaultRegion ?? Region;
        TimeZoneId = _settingsService.Settings.TimeZoneId;
    }

    [RelayCommand]
    private async Task FetchWorldAsync()
    {
        Status = string.Empty;
        if (string.IsNullOrWhiteSpace(SearchWorldId))
        {
            Status = "Enter a world ID (wrld_xxx).";
            return;
        }
        IsBusy = true;
        var world = await _apiService.GetWorldAsync(SearchWorldId.Trim());
        IsBusy = false;
        if (world == null)
        {
            Status = "World not found.";
            return;
        }

        ApplyWorld(world);
        Status = "World loaded. Configure and generate.";
    }

    [RelayCommand]
    private async Task SearchWorldsAsync()
    {
        Status = string.Empty;
        IsBusy = true;
        SearchResults.Clear();
        var list = await _apiService.SearchWorldsAsync(SearchText ?? string.Empty, n: 20, offset: 0, sort: "relevance");
        foreach (var w in list)
        {
            SearchResults.Add(w);
        }
        IsBusy = false;
        Status = SearchResults.Count == 0 ? "No worlds found." : $"{SearchResults.Count} worlds loaded. Click one to fill.";
    }

    [RelayCommand]
    private void UseWorld(WorldInfo? world)
    {
        if (world == null) return;
        ApplyWorld(world);
        SearchResults.Clear();
        SearchText = string.Empty;
        Status = $"Selected {world.Name}. Generate an instance.";
    }

    [RelayCommand]
    private void GenerateInstance()
    {
        Status = string.Empty;
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first (sidebar).";
            return;
        }
        if (string.IsNullOrWhiteSpace(WorldId))
        {
            Status = "Enter a world ID (wrld_xxx).";
            return;
        }

        var regionCode = RegionToCode(Region);
        var sn = EnsureShortName();

        var instanceId = $"{sn}~group({groupId})";

        if (!string.IsNullOrWhiteSpace(GroupAccess))
        {
            instanceId += $"~groupAccessType({GroupAccessToCode(GroupAccess)})";
        }
        if (AgeGateEnabled)
        {
            instanceId += "~ageGate";
        }
        if (!string.IsNullOrWhiteSpace(regionCode))
        {
            instanceId += $"~region({regionCode})";
        }
        if (QueueEnabled)
        {
            instanceId += "~queue";
        }
        if (!string.IsNullOrWhiteSpace(DisplayName))
        {
            instanceId += $"~displayName({DisplayName})";
        }

        var launchUrl = $"https://vrchat.com/home/launch?worldId={Uri.EscapeDataString(WorldId)}&instanceId={Uri.EscapeDataString(instanceId)}";

        var scheduled = GetScheduledDateTime(out var scheduleError);
        if (scheduleError)
        {
            return;
        }

        var plan = new InstancePlan
        {
            WorldId = WorldId,
            WorldName = string.IsNullOrWhiteSpace(WorldName) ? "(untitled)" : WorldName,
            Region = Region,
            GroupAccess = GroupAccess,
            QueueEnabled = QueueEnabled,
            AgeGateEnabled = AgeGateEnabled,
            ShortName = sn,
            InstanceId = instanceId,
            LaunchUrl = launchUrl,
            DisplayName = DisplayName,
            ScheduledFor = scheduled,
            TimeZoneId = TimeZoneId
        };
        Plans.Insert(0, plan);
        Status = "Instance link generated. Copy and launch in VRChat.";
    }

    [RelayCommand]
    private void CopyLaunchUrl(InstancePlan? plan)
    {
        if (plan == null) return;
        Clipboard.SetText(plan.LaunchUrl);
        Status = "Launch URL copied.";
    }

    [RelayCommand]
    private void CopyInstanceId(InstancePlan? plan)
    {
        if (plan == null) return;
        Clipboard.SetText(plan.InstanceId);
        Status = "Instance ID copied.";
    }

    [RelayCommand]
    private async Task InviteSelf(InstancePlan? plan)
    {
        if (plan == null) return;
        if (string.IsNullOrWhiteSpace(_apiService.CurrentUserId))
        {
            Status = "Not logged in. Please log in again.";
            return;
        }
        try
        {
            Status = "Creating instance...";
            var groupAccessCode = GroupAccessToCode(plan.GroupAccess);
            var regionCode = RegionToCode(plan.Region);
            var created = await _apiService.CreateInstanceAsync(
                plan.WorldId,
                regionCode,
                _mainViewModel.GroupId,
                groupAccessCode,
                plan.AgeGateEnabled,
                plan.QueueEnabled,
                plan.DisplayName,
                plan.ShortName,
                canRequestInvite: false,
                roleIds: null,
                type: "group");
            if (created == null)
            {
                Status = "Instance creation failed.";
                return;
            }

            var location = created.Location;
            var resolvedWorldId = plan.WorldId;
            var resolvedInstanceId = plan.InstanceId;

            if (!string.IsNullOrWhiteSpace(location) && location.Contains(':'))
            {
                var split = location.Split(':');
                if (split.Length >= 2)
                {
                    resolvedWorldId = split[0];
                    resolvedInstanceId = string.Join(':', split.Skip(1));
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(created.WorldId))
                {
                    resolvedWorldId = created.WorldId;
                }
                if (!string.IsNullOrWhiteSpace(created.InstanceId))
                {
                    resolvedInstanceId = created.InstanceId;
                }
            }

            var resolvedShortName = !string.IsNullOrWhiteSpace(created.ShortName)
                ? created.ShortName
                : (!string.IsNullOrWhiteSpace(created.SecureName) ? created.SecureName : plan.ShortName);

            // Update the plan with the server-issued identifiers so subsequent actions use the real values.
            plan.InstanceId = resolvedInstanceId;
            plan.ShortName = resolvedShortName ?? plan.ShortName;
            plan.LaunchUrl = $"https://vrchat.com/home/launch?worldId={Uri.EscapeDataString(resolvedWorldId)}&instanceId={Uri.EscapeDataString(resolvedInstanceId)}";

            Status = "Sending self-invite...";
            var success = await _apiService.SelfInviteAsync(resolvedWorldId, resolvedInstanceId, resolvedShortName, _apiService.CurrentUserId);
            Status = success ? "Self-invite sent. Check your notifications in VRChat." : "Self-invite failed.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to open invite: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Launch(InstancePlan? plan)
    {
        if (plan == null) return;
        try
        {
            var uri = $"vrchat://launch?worldId={Uri.EscapeDataString(plan.WorldId)}&instanceId={Uri.EscapeDataString(plan.InstanceId)}";
            var psi = new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            };
            Process.Start(psi);
            Status = "Launching instance...";
        }
        catch (Exception ex)
        {
            Status = $"Failed to launch: {ex.Message}";
        }
    }

    private static string RegionToCode(string region)
    {
        return region switch
        {
            "US East" => "use",
            "Europe" => "eu",
            "Japan" => "jp",
            _ => "us"
        };
    }

    private void ApplyWorld(WorldInfo world)
    {
        WorldId = world.Id;
        WorldName = world.Name;
        WorldAuthor = world.AuthorName;
        WorldCapacity = world.Capacity;
        SelectedWorld = world;
        SearchWorldId = world.Id;
    }

    private DateTime? GetScheduledDateTime(out bool hasError)
    {
        hasError = false;
        if (!SchedulingEnabled)
            return null;
        if (ScheduledDate == null)
            return null;

        if (string.IsNullOrWhiteSpace(ScheduledTime))
            return ScheduledDate;

        if (TimeSpan.TryParse(ScheduledTime, out var time))
        {
            var date = ScheduledDate.Value.Date + time;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                return TimeZoneInfo.ConvertTimeToUtc(date, tz);
            }
            catch
            {
                return date;
            }
        }

        Status = "Invalid time format. Use HH:MM (24h).";
        hasError = true;
        return null;
    }

    partial void OnTimeZoneIdChanged(string value)
    {
        OnPropertyChanged(nameof(TimeZoneDisplay));
    }

    private string GetTimeZoneDisplay()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId).DisplayName;
        }
        catch
        {
            return TimeZoneId;
        }
    }

    private string EnsureShortName()
    {
        if (!string.IsNullOrWhiteSpace(ShortName))
            return ShortName;

        var rng = Random.Shared;
        var num = rng.Next(10000, 99999);
        ShortName = num.ToString();
        return ShortName;
    }

    private static string GroupAccessToCode(string access)
    {
        return access.ToLowerInvariant() switch
        {
            "members" => "members",
            "plus" => "plus",
            "public" => "public",
            _ => "members"
        };
    }

}

public class InstancePlan
{
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string GroupAccess { get; set; } = string.Empty;
    public bool QueueEnabled { get; set; }
    public bool AgeGateEnabled { get; set; }
    public string ShortName { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string LaunchUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime? ScheduledFor { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
}
