using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;
using VRCGroupTools.Views;

namespace VRCGroupTools.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _twoFactorCode = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _statusColor = "White";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showTwoFactor;

    [ObservableProperty]
    private string _selectedAuthType = "totp";

    [ObservableProperty]
    private List<string> _availableAuthTypes = new() { "totp" };

    public event Action? LoginSuccessful;

    public LoginViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        Console.WriteLine("[DEBUG] LoginViewModel initialized");
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        Console.WriteLine($"[DEBUG] LoginAsync called - Username: '{Username}', Password length: {Password?.Length ?? 0}");
        
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Console.WriteLine("[DEBUG] Empty username or password");
            SetStatus("Please enter username and password", "Red");
            return;
        }

        IsLoading = true;
        SetStatus("Logging in...", "Orange");
        Console.WriteLine("[DEBUG] Calling API login...");

        try
        {
            var result = await _apiService.LoginAsync(Username, Password);
            Console.WriteLine($"[DEBUG] Login result - Success: {result.Success}, Requires2FA: {result.Requires2FA}, Message: {result.Message}");

            IsLoading = false;

            if (result.Success)
            {
                SetStatus("Login successful!", "Green");
                await Task.Delay(500);
                Console.WriteLine("[DEBUG] Invoking LoginSuccessful event");
                LoginSuccessful?.Invoke();
            }
            else if (result.Requires2FA)
            {
                Console.WriteLine($"[DEBUG] 2FA required, types: {string.Join(", ", result.TwoFactorTypes)}");
                AvailableAuthTypes = result.TwoFactorTypes;
                SelectedAuthType = result.TwoFactorTypes.Contains("totp") ? "totp" : result.TwoFactorTypes.First();
                ShowTwoFactor = true;
                SetStatus("Enter your 2FA code", "White");
            }
            else
            {
                SetStatus(result.Message ?? "Login failed", "Red");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Login exception: {ex}");
            IsLoading = false;
            SetStatus($"Error: {ex.Message}", "Red");
        }
    }

    [RelayCommand]
    private async Task Verify2FAAsync()
    {
        Console.WriteLine($"[DEBUG] Verify2FAAsync called - Code: '{TwoFactorCode}'");
        
        if (string.IsNullOrWhiteSpace(TwoFactorCode))
        {
            SetStatus("Please enter your 2FA code", "Red");
            return;
        }

        if (TwoFactorCode.Length != 6 || !TwoFactorCode.All(char.IsDigit))
        {
            SetStatus("Code must be 6 digits", "Red");
            return;
        }

        IsLoading = true;
        SetStatus("Verifying...", "Orange");

        try
        {
            var result = await _apiService.Verify2FAAsync(TwoFactorCode, SelectedAuthType);
            Console.WriteLine($"[DEBUG] 2FA result - Success: {result.Success}, Message: {result.Message}");

            IsLoading = false;

            if (result.Success)
            {
                SetStatus("2FA verified!", "Green");
                await Task.Delay(500);
                LoginSuccessful?.Invoke();
            }
            else
            {
                SetStatus(result.Message ?? "Invalid code", "Red");
                TwoFactorCode = "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] 2FA exception: {ex}");
            IsLoading = false;
            SetStatus($"Error: {ex.Message}", "Red");
        }
    }

    [RelayCommand]
    private void BackToLogin()
    {
        ShowTwoFactor = false;
        TwoFactorCode = "";
        SetStatus("", "White");
    }

    private void SetStatus(string message, string color)
    {
        Console.WriteLine($"[DEBUG] Status: {message} (color: {color})");
        StatusMessage = message;
        StatusColor = color;
    }
}
