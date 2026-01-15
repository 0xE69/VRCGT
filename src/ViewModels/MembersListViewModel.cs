using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class MembersListViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly MainViewModel _mainViewModel;
    private readonly ICacheService _cacheService;

    [ObservableProperty] private ObservableCollection<GroupMember> _members = new();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _roleFilters = new();
    [ObservableProperty] private string _selectedRole = "(All)";
    
    // Member editing panel
    [ObservableProperty] private bool _showMemberPanel;
    [ObservableProperty] private bool _isLoadingMember;
    [ObservableProperty] private GroupMember? _selectedMember;
    [ObservableProperty] private ObservableCollection<GroupRoleDisplay> _groupRoles = new();
    [ObservableProperty] private ObservableCollection<GroupRoleDisplay> _memberRoles = new();
    [ObservableProperty] private GroupRoleDisplay? _selectedRoleToAssign;

    public MembersListViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
    }

    public IEnumerable<GroupMember> FilteredMembers => Members.Where(m =>
        (string.IsNullOrWhiteSpace(Filter) ||
            (m.DisplayName ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
            (m.UserId ?? string.Empty).Contains(Filter, StringComparison.OrdinalIgnoreCase)) &&
        (SelectedRole == "(All)" || m.RoleIds?.Contains(SelectedRole) == true));

    [RelayCommand]
    private async Task LoadFromCacheAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading members from cache...";
        Members.Clear();
        RoleFilters.Clear();
        RoleFilters.Add("(All)");

        var cached = await _cacheService.LoadAsync<List<GroupMember>>($"group_members_{groupId}");
        if (cached == null || cached.Count == 0)
        {
            Status = "No cached members found. Use Refresh to fetch from API.";
            IsBusy = false;
            return;
        }

        foreach (var member in cached)
        {
            Members.Add(member);
            if (member.RoleIds != null)
            {
                foreach (var role in member.RoleIds)
                {
                    if (!string.IsNullOrWhiteSpace(role) && !RoleFilters.Contains(role))
                    {
                        RoleFilters.Add(role);
                    }
                }
            }
        }

        Status = $"Loaded {cached.Count} members from cache.";
        IsBusy = false;
        OnPropertyChanged(nameof(FilteredMembers));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            Status = "Set a Group ID first.";
            return;
        }

        IsBusy = true;
        Status = "Loading members...";
        Members.Clear();
        RoleFilters.Clear();
        RoleFilters.Add("(All)");

        var list = await _apiService.GetGroupMembersAsync(groupId, (count, _) =>
        {
            Status = $"Loaded {count} members...";
        });

        foreach (var member in list)
        {
            Members.Add(member);
            if (member.RoleIds != null)
            {
                foreach (var role in member.RoleIds)
                {
                    if (!string.IsNullOrWhiteSpace(role) && !RoleFilters.Contains(role))
                    {
                        RoleFilters.Add(role);
                    }
                }
            }
        }

        Status = list.Count == 0 ? "No members found." : $"Loaded {list.Count} members.";
        await _cacheService.SaveAsync($"group_members_{groupId}", list);
        IsBusy = false;
        OnPropertyChanged(nameof(FilteredMembers));
    }

    [RelayCommand]
    private async Task SelectMemberAsync(GroupMember? member)
    {
        if (member == null) return;
        
        SelectedMember = member;
        ShowMemberPanel = true;
        IsLoadingMember = true;
        MemberRoles.Clear();
        GroupRoles.Clear();
        
        try
        {
            var groupId = _mainViewModel.GroupId;
            if (string.IsNullOrWhiteSpace(groupId)) return;
            
            // Load group roles
            var roles = await _apiService.GetGroupRolesAsync(groupId);
            foreach (var role in roles)
            {
                var display = new GroupRoleDisplay { RoleId = role.RoleId, Name = role.Name };
                GroupRoles.Add(display);
                
                // Check if member has this role
                if (member.RoleIds?.Contains(role.RoleId) == true)
                {
                    MemberRoles.Add(display);
                }
            }
        }
        catch (Exception ex)
        {
            Status = $"Error loading member: {ex.Message}";
        }
        finally
        {
            IsLoadingMember = false;
        }
    }

    [RelayCommand]
    private void CloseMemberPanel()
    {
        ShowMemberPanel = false;
        SelectedMember = null;
        MemberRoles.Clear();
    }

    [RelayCommand]
    private async Task AssignRoleAsync()
    {
        if (SelectedMember == null || SelectedRoleToAssign == null) return;
        
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        
        IsBusy = true;
        Status = $"Assigning role {SelectedRoleToAssign.Name}...";
        
        try
        {
            var success = await _apiService.AssignGroupRoleAsync(groupId, SelectedMember.UserId, SelectedRoleToAssign.RoleId);
            if (success)
            {
                if (!MemberRoles.Any(r => r.RoleId == SelectedRoleToAssign.RoleId))
                {
                    MemberRoles.Add(SelectedRoleToAssign);
                }
                SelectedMember.RoleIds ??= new List<string>();
                if (!SelectedMember.RoleIds.Contains(SelectedRoleToAssign.RoleId))
                {
                    SelectedMember.RoleIds.Add(SelectedRoleToAssign.RoleId);
                }
                Status = $"Role {SelectedRoleToAssign.Name} assigned!";
            }
            else
            {
                Status = "Failed to assign role.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveRoleAsync(GroupRoleDisplay? role)
    {
        if (SelectedMember == null || role == null) return;
        
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        
        IsBusy = true;
        Status = $"Removing role {role.Name}...";
        
        try
        {
            var success = await _apiService.RemoveGroupRoleAsync(groupId, SelectedMember.UserId, role.RoleId);
            if (success)
            {
                MemberRoles.Remove(role);
                SelectedMember.RoleIds?.Remove(role.RoleId);
                Status = $"Role {role.Name} removed!";
            }
            else
            {
                Status = "Failed to remove role.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task KickMemberAsync()
    {
        if (SelectedMember == null) return;
        
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        
        // Show moderation dialog
        var request = await Helpers.ModerationDialogHelper.ShowModerationDialogAsync(
            "kick",
            groupId,
            SelectedMember.UserId,
            SelectedMember.DisplayName
        );
        
        if (request == null) return; // User cancelled
        
        IsBusy = true;
        Status = $"Kicking {SelectedMember.DisplayName}...";
        
        try
        {
            var moderationService = App.Services.GetRequiredService<IModerationService>();
            
            // Log the moderation action
            await moderationService.LogModerationActionAsync(
                groupId,
                request,
                _apiService.CurrentUserId ?? "unknown",
                _apiService.CurrentUserDisplayName ?? "Unknown"
            );
            
            // Execute the kick
            var success = await _apiService.KickGroupMemberAsync(groupId, SelectedMember.UserId, request.Reason, request.Description);
            if (success)
            {
                Members.Remove(SelectedMember);
                OnPropertyChanged(nameof(FilteredMembers));
                ShowMemberPanel = false;
                SelectedMember = null;
                Status = $"Member kicked: {request.Reason}";
            }
            else
            {
                Status = "Failed to kick member.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BanMemberAsync()
    {
        if (SelectedMember == null) return;
        
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        
        // Show moderation dialog
        var request = await Helpers.ModerationDialogHelper.ShowModerationDialogAsync(
            "ban",
            groupId,
            SelectedMember.UserId,
            SelectedMember.DisplayName
        );
        
        if (request == null) return; // User cancelled
        
        IsBusy = true;
        Status = $"Banning {SelectedMember.DisplayName}...";
        
        try
        {
            var moderationService = App.Services.GetRequiredService<IModerationService>();
            
            // Log the moderation action
            await moderationService.LogModerationActionAsync(
                groupId,
                request,
                _apiService.CurrentUserId ?? "unknown",
                _apiService.CurrentUserDisplayName ?? "Unknown"
            );
            
            // Execute the ban
            var success = await _apiService.BanGroupMemberAsync(groupId, SelectedMember.UserId, request.Reason, request.Description);
            if (success)
            {
                Members.Remove(SelectedMember);
                OnPropertyChanged(nameof(FilteredMembers));
                ShowMemberPanel = false;
                SelectedMember = null;
                Status = $"Member banned: {request.Reason}";
            }
            else
            {
                Status = "Failed to ban member.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task WarnMemberAsync()
    {
        if (SelectedMember == null) return;
        
        var groupId = _mainViewModel.GroupId;
        if (string.IsNullOrWhiteSpace(groupId)) return;
        
        // Show moderation dialog
        var request = await Helpers.ModerationDialogHelper.ShowModerationDialogAsync(
            "warning",
            groupId,
            SelectedMember.UserId,
            SelectedMember.DisplayName
        );
        
        if (request == null) return; // User cancelled
        
        IsBusy = true;
        Status = $"Warning {SelectedMember.DisplayName}...";
        
        try
        {
            var moderationService = App.Services.GetRequiredService<IModerationService>();
            
            // Log the warning
            await moderationService.LogModerationActionAsync(
                groupId,
                request,
                _apiService.CurrentUserId ?? "unknown",
                _apiService.CurrentUserDisplayName ?? "Unknown"
            );
            
            // Send warning notification via API (this just logs locally)
            var success = await _apiService.WarnUserAsync(groupId, SelectedMember.UserId, request.Reason, request.Description);
            if (success)
            {
                Status = $"Warning issued: {request.Reason}";
                
                // Optionally send Discord notification
                try
                {
                    var discordService = App.Services.GetService<IDiscordWebhookService>();
                    if (discordService != null && discordService.IsConfigured)
                    {
                        var history = await moderationService.GetInfractionHistoryAsync(groupId, SelectedMember.UserId);
                        await discordService.SendModerationActionAsync(
                            "warning",
                            SelectedMember.DisplayName,
                            _apiService.CurrentUserDisplayName ?? "Unknown",
                            request.Reason,
                            request.Description,
                            DateTime.UtcNow,
                            null,
                            history.TotalWarnings
                        );
                    }
                }
                catch { /* Discord notification failed, but warning was logged */ }
            }
            else
            {
                Status = "Failed to issue warning.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMembers));
    }

    partial void OnSelectedRoleChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMembers));
    }
}
