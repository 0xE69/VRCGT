using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCGroupTools.Services;

namespace VRCGroupTools.ViewModels;

public partial class UserProfileViewModel : ObservableObject
{
    private readonly IVRChatApiService? _apiService;

    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private UserDetails? _user;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _trustRank = "Loading...";

    [ObservableProperty]
    private Brush _trustRankBrush = new SolidColorBrush(Colors.Gray);

    public UserProfileViewModel(UserDetails user)
    {
        User = user;
        UserId = user.UserId;
        UpdateDisplays();
    }

    public UserProfileViewModel(string userId, IVRChatApiService apiService)
    {
        UserId = userId;
        _apiService = apiService;
        _ = LoadUserAsync();
    }

    private async Task LoadUserAsync()
    {
        if (_apiService == null || IsLoading) return;
        
        try 
        {
            IsLoading = true;
            TrustRank = "Loading...";
            
            var user = await _apiService.GetUserAsync(UserId);
            if (user != null)
            {
                User = user;
                UpdateDisplays();
            }
            else
            {
                TrustRank = "Failed to load";
            }
        }
        catch (Exception)
        {
            TrustRank = "Error";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateDisplays()
    {
        if (User == null) return;

        // Calculate Trust Rank
        CalculateTrustRank();
    }

    private void CalculateTrustRank()
    {
        if (User == null || User.Tags == null)
        {
            TrustRank = "Visitor";
            TrustRankBrush = new SolidColorBrush(Color.FromRgb(149, 165, 166));
            return;
        }

        var tags = User.Tags;

        if (tags.Contains("system_trust_legend")) { TrustRank = "Trusted User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(138, 43, 226)); return; }
        if (tags.Contains("system_trust_veteran")) { TrustRank = "Trusted User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(138, 43, 226)); return; }
        if (tags.Contains("system_trust_trusted")) { TrustRank = "Known User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(255, 123, 0)); return; }
        if (tags.Contains("system_trust_known")) { TrustRank = "User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)); return; }
        if (tags.Contains("system_trust_basic")) { TrustRank = "New User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(52, 152, 219)); return; }

        var tagsLower = tags.Select(t => t.ToLowerInvariant()).ToList();
        if (tagsLower.Any(t => t.Contains("legend") || t.Contains("veteran"))) { TrustRank = "Trusted User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(138, 43, 226)); return; }
        if (tagsLower.Any(t => t.Contains("trusted"))) { TrustRank = "Known User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(255, 123, 0)); return; }
        if (tagsLower.Any(t => t.Contains("known"))) { TrustRank = "User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)); return; }
        if (tagsLower.Any(t => t.Contains("basic"))) { TrustRank = "New User"; TrustRankBrush = new SolidColorBrush(Color.FromRgb(52, 152, 219)); return; }

        TrustRank = "Visitor";
        TrustRankBrush = new SolidColorBrush(Color.FromRgb(149, 165, 166));
    }

    [RelayCommand]
    private void OpenWebProfile()
    {
        if (string.IsNullOrEmpty(UserId)) return;
        var url = $"https://vrchat.com/home/user/{UserId}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open profile: {ex.Message}");
        }
    }
}
