using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Models;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using VRCGroupTools.Views;

namespace VRCGroupTools.Helpers;

public static class ModerationDialogHelper
{
    public static Task<ModerationActionRequest?> ShowModerationDialogAsync(
        string actionType,
        string groupId,
        string targetUserId,
        string targetDisplayName)
    {
        try
        {
            var moderationService = App.Services.GetRequiredService<IModerationService>();
            var apiService = App.Services.GetRequiredService<IVRChatApiService>();
            
            var viewModel = new ModerationDialogViewModel(
                moderationService,
                groupId,
                actionType,
                targetUserId,
                targetDisplayName,
                apiService.CurrentUserId ?? "unknown",
                apiService.CurrentUserDisplayName ?? "Unknown"
            );
            
            var dialog = new ModerationDialogView
            {
                DataContext = viewModel,
                Owner = Application.Current.MainWindow
            };
            
            var result = dialog.ShowDialog();
            
            if (result == true && viewModel.DialogResult)
            {
                return Task.FromResult<ModerationActionRequest?>(viewModel.Request);
            }
            
            return Task.FromResult<ModerationActionRequest?>(null);
        }
        catch (Exception ex)
        {
            LoggingService.Error("MODERATION-HELPER", $"Failed to show moderation dialog: {ex.Message}");
            return Task.FromResult<ModerationActionRequest?>(null);
        }
    }
    
    public static async Task<bool> ExecuteModerationActionAsync(
        string groupId,
        ModerationActionRequest request,
        IVRChatApiService apiService,
        IModerationService moderationService)
    {
        try
        {
            // Log the action first
            await moderationService.LogModerationActionAsync(
                groupId,
                request,
                apiService.CurrentUserId ?? "unknown",
                apiService.CurrentUserDisplayName ?? "Unknown"
            );
            
            // Execute the VRChat API action
            bool success = request.ActionType.ToLower() switch
            {
                "kick" => await apiService.KickGroupMemberAsync(groupId, request.TargetUserId, request.Reason, request.Description),
                "ban" => await apiService.BanGroupMemberAsync(groupId, request.TargetUserId, request.Reason, request.Description),
                "warning" => await apiService.WarnUserAsync(groupId, request.TargetUserId, request.Reason, request.Description),
                _ => false
            };
            
            if (success)
            {
                LoggingService.Info("MODERATION", $"{request.ActionType} executed successfully for {request.TargetDisplayName}");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            LoggingService.Error("MODERATION", $"Failed to execute moderation action: {ex.Message}");
            return false;
        }
    }
}
