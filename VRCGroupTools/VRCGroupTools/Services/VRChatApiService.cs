using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public interface IVRChatApiService
{
    bool IsLoggedIn { get; }
    string? CurrentUserId { get; }
    string? CurrentUserDisplayName { get; }
    Task<LoginResult> LoginAsync(string username, string password);
    Task<LoginResult> Verify2FAAsync(string code, string authType);
    Task<List<GroupMember>> GetGroupMembersAsync(string groupId, Action<int, int>? progressCallback = null);
    Task<UserDetails?> GetUserAsync(string userId);
    void Logout();
}

public class VRChatApiService : IVRChatApiService
{
    private const string BaseUrl = "https://api.vrchat.cloud/api/1";
    private const string ApiKey = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
    private const int RateLimitDelayMs = 100;

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private string? _authToken;
    private DateTime _lastRequestTime = DateTime.MinValue;

    public bool IsLoggedIn { get; private set; }
    public string? CurrentUserId { get; private set; }
    public string? CurrentUserDisplayName { get; private set; }

    public VRChatApiService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VRCGroupTools/1.0");
    }

    private async Task RateLimitAsync()
    {
        var elapsed = DateTime.Now - _lastRequestTime;
        if (elapsed.TotalMilliseconds < RateLimitDelayMs)
        {
            await Task.Delay(RateLimitDelayMs - (int)elapsed.TotalMilliseconds);
        }
        _lastRequestTime = DateTime.Now;
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        Console.WriteLine($"[API] LoginAsync called for user: {username}");
        
        try
        {
            await RateLimitAsync();

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            Console.WriteLine("[API] Credentials encoded");
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"/auth/user?apiKey={ApiKey}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            Console.WriteLine($"[API] Sending request to: {_httpClient.BaseAddress}/auth/user");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[API] Response status: {response.StatusCode}");
            Console.WriteLine($"[API] Response body: {content}");

            if (!response.IsSuccessStatusCode)
            {
                var errorObj = JsonConvert.DeserializeObject<JObject>(content);
                return new LoginResult
                {
                    Success = false,
                    Message = errorObj?["error"]?["message"]?.ToString() ?? "Login failed"
                };
            }

            var data = JsonConvert.DeserializeObject<JObject>(content);

            // Check for 2FA requirement
            if (data?["requiresTwoFactorAuth"] != null)
            {
                var types = data["requiresTwoFactorAuth"]!.ToObject<List<string>>() ?? new List<string>();
                Console.WriteLine($"[API] 2FA required, types: {string.Join(", ", types)}");
                return new LoginResult
                {
                    Success = false,
                    Requires2FA = true,
                    TwoFactorTypes = types,
                    Message = "2FA required"
                };
            }

            // Login successful
            CurrentUserId = data?["id"]?.ToString();
            CurrentUserDisplayName = data?["displayName"]?.ToString();
            IsLoggedIn = true;
            Console.WriteLine($"[API] Login successful! User: {CurrentUserDisplayName} ({CurrentUserId})");

            // Store auth cookie
            var cookies = _cookieContainer.GetCookies(new Uri(BaseUrl));
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "auth")
                {
                    _authToken = cookie.Value;
                    Console.WriteLine("[API] Auth cookie stored");
                    break;
                }
            }

            return new LoginResult
            {
                Success = true,
                UserId = CurrentUserId,
                DisplayName = CurrentUserDisplayName,
                Message = "Login successful"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] Login exception: {ex}");
            return new LoginResult
            {
                Success = false,
                Message = $"Connection error: {ex.Message}"
            };
        }
    }

    public async Task<LoginResult> Verify2FAAsync(string code, string authType)
    {
        try
        {
            await RateLimitAsync();

            var endpoint = authType switch
            {
                "totp" => "/auth/twofactorauth/totp/verify",
                "otp" => "/auth/twofactorauth/otp/verify",
                "emailotp" => "/auth/twofactorauth/emailotp/verify",
                _ => "/auth/twofactorauth/totp/verify"
            };

            var payload = JsonConvert.SerializeObject(new { code });
            var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}?apiKey={ApiKey}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"[2FA] Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"[2FA] Response: {content}");

            if (!response.IsSuccessStatusCode)
            {
                return new LoginResult
                {
                    Success = false,
                    Message = "Invalid 2FA code"
                };
            }

            var data = JsonConvert.DeserializeObject<JObject>(content);
            if (data?["verified"]?.Value<bool>() == true)
            {
                // Now get user info
                await RateLimitAsync();
                var userResponse = await _httpClient.GetAsync($"/auth/user?apiKey={ApiKey}");
                var userContent = await userResponse.Content.ReadAsStringAsync();
                var userData = JsonConvert.DeserializeObject<JObject>(userContent);

                CurrentUserId = userData?["id"]?.ToString();
                CurrentUserDisplayName = userData?["displayName"]?.ToString();
                IsLoggedIn = true;

                return new LoginResult
                {
                    Success = true,
                    UserId = CurrentUserId,
                    DisplayName = CurrentUserDisplayName,
                    Message = "2FA verified"
                };
            }

            return new LoginResult
            {
                Success = false,
                Message = "Verification failed"
            };
        }
        catch (Exception ex)
        {
            return new LoginResult
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<List<GroupMember>> GetGroupMembersAsync(string groupId, Action<int, int>? progressCallback = null)
    {
        var members = new List<GroupMember>();
        int offset = 0;
        const int limit = 100;
        int total = 0;

        try
        {
            while (true)
            {
                await RateLimitAsync();

                var response = await _httpClient.GetAsync($"/groups/{groupId}/members?apiKey={ApiKey}&n={limit}&offset={offset}");
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[MEMBERS] Error: {response.StatusCode}");
                    break;
                }

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<List<JObject>>(content);

                if (data == null || data.Count == 0)
                    break;

                foreach (var memberData in data)
                {
                    var member = new GroupMember
                    {
                        UserId = memberData["userId"]?.ToString() ?? "",
                        DisplayName = memberData["user"]?["displayName"]?.ToString() ?? "Unknown",
                        JoinedAt = memberData["joinedAt"]?.ToString(),
                        RoleIds = memberData["roleIds"]?.ToObject<List<string>>() ?? new List<string>()
                    };
                    members.Add(member);
                }

                total = members.Count;
                progressCallback?.Invoke(total, -1); // -1 means unknown total

                if (data.Count < limit)
                    break;

                offset += limit;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MEMBERS] Exception: {ex.Message}");
        }

        return members;
    }

    public async Task<UserDetails?> GetUserAsync(string userId)
    {
        try
        {
            await RateLimitAsync();

            var response = await _httpClient.GetAsync($"/users/{userId}?apiKey={ApiKey}");
            
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(content);

            if (data == null)
                return null;

            var badges = data["badges"]?.ToObject<List<JObject>>() ?? new List<JObject>();
            var badgeNames = badges.Select(b => b["badgeId"]?.ToString() ?? "").ToList();

            return new UserDetails
            {
                UserId = data["id"]?.ToString() ?? userId,
                DisplayName = data["displayName"]?.ToString() ?? "Unknown",
                Bio = data["bio"]?.ToString(),
                ProfilePicUrl = data["currentAvatarThumbnailImageUrl"]?.ToString(),
                Badges = badgeNames,
                Tags = data["tags"]?.ToObject<List<string>>() ?? new List<string>(),
                IsAgeVerified = badgeNames.Any(b => b.Contains("age_verification", StringComparison.OrdinalIgnoreCase)) ||
                               (data["tags"]?.ToObject<List<string>>()?.Any(t => t.Contains("age_verified", StringComparison.OrdinalIgnoreCase)) ?? false)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[USER] Exception: {ex.Message}");
            return null;
        }
    }

    public void Logout()
    {
        IsLoggedIn = false;
        CurrentUserId = null;
        CurrentUserDisplayName = null;
        _authToken = null;
        _cookieContainer.GetCookies(new Uri(BaseUrl)).Cast<Cookie>().ToList().ForEach(c => c.Expired = true);
    }
}

public class LoginResult
{
    public bool Success { get; set; }
    public bool Requires2FA { get; set; }
    public List<string> TwoFactorTypes { get; set; } = new();
    public string? UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? Message { get; set; }
}

public class GroupMember
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? JoinedAt { get; set; }
    public List<string> RoleIds { get; set; } = new();
    public bool? IsAgeVerified { get; set; }
}

public class UserDetails
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? ProfilePicUrl { get; set; }
    public List<string> Badges { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool IsAgeVerified { get; set; }
}
