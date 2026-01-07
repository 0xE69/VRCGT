using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.Services;
using VRCGroupTools.Views;

namespace VRCGroupTools.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IVRChatApiService _apiService;
    private readonly ICacheService _cacheService;

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
    private bool _rememberMe = true;

    [ObservableProperty]
    private bool _isAutoLoggingIn;

    [ObservableProperty]
    private bool _showPassword;

    [ObservableProperty]
    private string _selectedAuthType = "totp";

    [ObservableProperty]
    private List<string> _availableAuthTypes = new() { "totp" };

    public event Action? LoginSuccessful;

    public LoginViewModel()
    {
        _apiService = App.Services.GetRequiredService<IVRChatApiService>();
        _cacheService = App.Services.GetRequiredService<ICacheService>();
        Console.WriteLine("[DEBUG] LoginViewModel initialized");
    }

    public async Task TryAutoLoginAsync()
    {
        try
        {
            Console.WriteLine("[AUTO-LOGIN] Checking for cached session...");
            
            // First try to restore from cached session (most seamless)
            var cachedSession = await _cacheService.LoadAsync<CachedSession>("session");
            if (cachedSession != null && !string.IsNullOrEmpty(cachedSession.AuthCookie))
            {
                Console.WriteLine("[AUTO-LOGIN] Found cached session, attempting restore...");
                IsAutoLoggingIn = true;
                SetStatus("Restoring session...", "Orange");

                var result = await _apiService.RestoreSessionAsync(cachedSession.AuthCookie, cachedSession.TwoFactorAuth);
                
                if (result.Success)
                {
                    Console.WriteLine("[AUTO-LOGIN] Session restored successfully!");
                    SetStatus($"Welcome back, {result.DisplayName}!", "Green");
                    await Task.Delay(1000);
                    LoginSuccessful?.Invoke();
                    return;
                }
                else
                {
                    Console.WriteLine($"[AUTO-LOGIN] Session restore failed: {result.Message}");
                    // Clear invalid session
                    await _cacheService.DeleteAsync("session");
                }
            }

            // Try to load saved credentials
            var credentialsJson = await _cacheService.LoadSecureAsync("credentials");
            if (!string.IsNullOrEmpty(credentialsJson))
            {
                var credentials = System.Text.Json.JsonSerializer.Deserialize<CachedCredentials>(credentialsJson);
                if (credentials != null && !string.IsNullOrEmpty(credentials.Username))
                {
                    Console.WriteLine("[AUTO-LOGIN] Found saved credentials");
                    Username = credentials.Username;
                    Password = credentials.Password;
                    RememberMe = true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-LOGIN] Error: {ex.Message}");
        }
        finally
        {
            IsAutoLoggingIn = false;
            if (!_apiService.IsLoggedIn)
            {
                SetStatus("", "White");
            }
        }
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
                await SaveCredentialsAndSessionAsync();
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
                await SaveCredentialsAndSessionAsync();
                await Task.Delay(500);
                Console.WriteLine("[DEBUG] About to invoke LoginSuccessful event...");
                Console.WriteLine($"[DEBUG] LoginSuccessful event has subscribers: {LoginSuccessful != null}");
                LoginSuccessful?.Invoke();
                Console.WriteLine("[DEBUG] LoginSuccessful event invoked");
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

    private async Task SaveCredentialsAndSessionAsync()
    {
        try
        {
            // Save credentials if Remember Me is checked
            if (RememberMe)
            {
                var credentials = new CachedCredentials
                {
                    Username = Username,
                    Password = Password
                };
                var json = System.Text.Json.JsonSerializer.Serialize(credentials);
                await _cacheService.SaveSecureAsync("credentials", json);
                Console.WriteLine("[CACHE] Credentials saved securely");
            }
            else
            {
                // Clear saved credentials
                await _cacheService.DeleteAsync("credentials");
            }

            // Always try to save session for seamless re-login
            var authCookie = _apiService.GetAuthCookie();
            var twoFactorCookie = _apiService.GetTwoFactorCookie();
            
            if (!string.IsNullOrEmpty(authCookie))
            {
                var session = new CachedSession
                {
                    AuthCookie = authCookie,
                    TwoFactorAuth = twoFactorCookie,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    UserId = _apiService.CurrentUserId ?? "",
                    DisplayName = _apiService.CurrentUserDisplayName ?? ""
                };
                await _cacheService.SaveAsync("session", session);
                Console.WriteLine("[CACHE] Session saved");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CACHE] Error saving credentials/session: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BackToLogin()
    {
        ShowTwoFactor = false;
        TwoFactorCode = "";
        SetStatus("", "White");
    }

    [RelayCommand]
    private void ToggleShowPassword()
    {
        ShowPassword = !ShowPassword;
    }

    [RelayCommand]
    private async Task ClearSavedDataAsync()
    {
        await _cacheService.DeleteAsync("session");
        await _cacheService.DeleteAsync("credentials");
        Username = "";
        Password = "";
        RememberMe = false;
        SetStatus("Saved login data cleared", "Green");
    }

    private void SetStatus(string message, string color)
    {
        Console.WriteLine($"[DEBUG] Status: {message} (color: {color})");
        StatusMessage = message;
        StatusColor = color;
    }
}
