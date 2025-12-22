using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public interface IVRChatApiService
{
    bool IsLoggedIn { get; }
    string? CurrentUserId { get; }
    string? CurrentUserDisplayName { get; }
    string? CurrentUserProfilePicUrl { get; }
    string? CurrentGroupId { get; set; }
    Task<LoginResult> LoginAsync(string username, string password);
    Task<LoginResult> Verify2FAAsync(string code, string authType);
    Task<LoginResult> RestoreSessionAsync(string authCookie, string? twoFactorCookie);
    Task<List<GroupMember>> GetGroupMembersAsync(string groupId, Action<int, int>? progressCallback = null);
    Task<List<GroupBanEntry>> GetGroupBansAsync(string groupId, Action<int, int>? progressCallback = null);
    Task<GroupInfo?> GetGroupAsync(string groupId);
    Task<bool> SendGroupInviteAsync(string groupId, string userId);
    Task<GroupPostResult?> CreateGroupPostAsync(string groupId, string title, string text, IEnumerable<string>? roleIds = null, string visibility = "public", bool sendNotification = true, string? imageId = null);
    Task<UserDetails?> GetUserAsync(string userId);
    Task<List<UserSearchResult>> SearchUsersAsync(string query);
    Task<WorldInfo?> GetWorldAsync(string worldId);
    Task<List<WorldInfo>> SearchWorldsAsync(string query, int n = 20, int offset = 0, string sort = "relevance");
    Task<bool> SelfInviteAsync(string worldId, string instanceId, string? shortName = null, string? userId = null);
    Task<InstanceCreateResult?> CreateInstanceAsync(
        string worldId,
        string region,
        string groupId,
        string groupAccessType,
        bool ageGate,
        bool queueEnabled,
        string? displayName = null,
        string? shortName = null,
        bool canRequestInvite = false,
        IEnumerable<string>? roleIds = null,
        string type = "group");
    Task<GroupMemberApiResult?> GetGroupMemberAsync(string groupId, string userId);
    Task<List<GroupRole>> GetGroupRolesAsync(string groupId);
    Task<bool> AssignGroupRoleAsync(string groupId, string userId, string roleId);
    Task<bool> RemoveGroupRoleAsync(string groupId, string userId, string roleId);
    Task<bool> KickGroupMemberAsync(string groupId, string userId);
    Task<bool> BanGroupMemberAsync(string groupId, string userId);
    Task<bool> UnbanGroupMemberAsync(string groupId, string oderId);
    Task<JsonElement?> GetGroupAuditLogsAsync(string groupId, int count = 100, int offset = 0);
    Task<string?> UploadImageAsync(string filePath);
    Task<GroupCalendarEvent?> CreateGroupEventAsync(string groupId, GroupEventCreateRequest request);
    Task<List<GroupCalendarEvent>> GetGroupEventsAsync(string groupId);
    Task<bool> DeleteGroupEventAsync(string groupId, string eventId);
    string? GetAuthCookie();
    string? GetTwoFactorCookie();
    void Logout();
}

public class InstanceCreateResult
{
    public string? Location { get; set; }
    public string? WorldId { get; set; }
    public string? InstanceId { get; set; }
    public string? ShortName { get; set; }
    public string? SecureName { get; set; }
    public string? DisplayName { get; set; }
}

public class GroupInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public int OnlineCount { get; set; }
    public string? IconUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? Privacy { get; set; }
    public string? OwnerId { get; set; }
}

public class GroupPostResult
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Visibility { get; set; }
    public string? ImageId { get; set; }
}

public class VRChatApiService : IVRChatApiService
{
    private const string BaseUrl = "https://api.vrchat.cloud/api/1";
    private const string ApiKey = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
    private const int RateLimitDelayMs = 100;

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string? _authToken;
    private string? _twoFactorToken;
    private DateTime _lastRequestTime = DateTime.MinValue;

    public bool IsLoggedIn { get; private set; }
    public string? CurrentUserId { get; private set; }
    public string? CurrentUserDisplayName { get; private set; }
    public string? CurrentUserProfilePicUrl { get; private set; }
    public string? CurrentGroupId { get; set; }

    public VRChatApiService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl + "/")
        };

        // VRChat requires a proper User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VRCGT/1.0.0 (https://github.com/VRCGT)");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
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

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, object? payload)
    {
        await RateLimitAsync();
        var request = new HttpRequestMessage(method, url);
        if (payload != null)
        {
            var json = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return await _httpClient.SendAsync(request);
    }

    private static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        Console.WriteLine($"[API] LoginAsync called for user: {username}");
        
        try
        {
            await RateLimitAsync();

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            Console.WriteLine("[API] Credentials encoded");
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"auth/user?apiKey={ApiKey}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            Console.WriteLine($"[API] Sending request to: {_httpClient.BaseAddress}auth/user?apiKey={ApiKey}");

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
            // Prefer userIcon over avatar thumbnail
            CurrentUserProfilePicUrl = data?["userIcon"]?.ToString();
            if (string.IsNullOrEmpty(CurrentUserProfilePicUrl))
            {
                CurrentUserProfilePicUrl = data?["currentAvatarThumbnailImageUrl"]?.ToString()
                    ?? data?["currentAvatarImageUrl"]?.ToString();
            }
            IsLoggedIn = true;
            Console.WriteLine($"[API] Login successful! User: {CurrentUserDisplayName} ({CurrentUserId})");
            Console.WriteLine($"[API] Profile pic: {CurrentUserProfilePicUrl}");

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
                "totp" => "auth/twofactorauth/totp/verify",
                "otp" => "auth/twofactorauth/otp/verify",
                "emailotp" => "auth/twofactorauth/emailotp/verify",
                _ => "auth/twofactorauth/totp/verify"
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
                var userResponse = await _httpClient.GetAsync($"auth/user?apiKey={ApiKey}");
                var userContent = await userResponse.Content.ReadAsStringAsync();
                var userData = JsonConvert.DeserializeObject<JObject>(userContent);

                CurrentUserId = userData?["id"]?.ToString();
                CurrentUserDisplayName = userData?["displayName"]?.ToString();
                // Prefer userIcon over avatar thumbnail
                CurrentUserProfilePicUrl = userData?["userIcon"]?.ToString();
                if (string.IsNullOrEmpty(CurrentUserProfilePicUrl))
                {
                    CurrentUserProfilePicUrl = userData?["currentAvatarThumbnailImageUrl"]?.ToString()
                        ?? userData?["currentAvatarImageUrl"]?.ToString();
                }
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

                var response = await _httpClient.GetAsync($"groups/{groupId}/members?apiKey={ApiKey}&n={limit}&offset={offset}");
                
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

    public async Task<List<GroupBanEntry>> GetGroupBansAsync(string groupId, Action<int, int>? progressCallback = null)
    {
        var bans = new List<GroupBanEntry>();
        int offset = 0;
        const int limit = 100;

        try
        {
            while (true)
            {
                await RateLimitAsync();

                var response = await _httpClient.GetAsync($"groups/{groupId}/bans?apiKey={ApiKey}&n={limit}&offset={offset}");

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[BANS] Error: {response.StatusCode}");
                    break;
                }

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<List<JObject>>(content);

                if (data == null || data.Count == 0)
                {
                    break;
                }

                foreach (var banData in data)
                {
                    var ban = new GroupBanEntry
                    {
                        UserId = banData["userId"]?.ToString() ?? string.Empty,
                        DisplayName = banData["user"]?["displayName"]?.ToString() ?? "Unknown",
                        Reason = banData["reason"]?.ToString() ?? string.Empty,
                        BannedAt = banData["createdAt"]?.ToString(),
                        ExpiresAt = banData["expiresAt"]?.ToString()
                    };
                    bans.Add(ban);
                }

                progressCallback?.Invoke(bans.Count, -1);

                if (data.Count < limit)
                {
                    break;
                }

                offset += limit;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BANS] Exception: {ex.Message}");
        }

        return bans;
    }

    public async Task<GroupInfo?> GetGroupAsync(string groupId)
    {
        try
        {
            await RateLimitAsync();
            var response = await _httpClient.GetAsync($"groups/{groupId}?apiKey={ApiKey}");
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROUP] Status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP] Error: {content}");
                return null;
            }

            var json = JsonConvert.DeserializeObject<JObject>(content);
            if (json == null)
            {
                return null;
            }

            return new GroupInfo
            {
                Id = json["id"]?.ToString() ?? groupId,
                Name = json["name"]?.ToString() ?? string.Empty,
                ShortCode = json["shortCode"]?.ToString(),
                Description = json["description"]?.ToString(),
                MemberCount = json["memberCount"]?.Value<int?>() ?? 0,
                OnlineCount = json["onlineMemberCount"]?.Value<int?>() ?? 0,
                IconUrl = json["iconUrl"]?.ToString(),
                BannerUrl = json["bannerUrl"]?.ToString(),
                Privacy = json["privacy"]?.ToString(),
                OwnerId = json["ownerId"]?.ToString()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<UserDetails?> GetUserAsync(string userId)
    {
        try
        {
            Console.WriteLine($"[API] GetUserAsync called for: {userId}");
            await RateLimitAsync();

            Console.WriteLine($"[API] Making request: users/{userId}");
            var response = await _httpClient.GetAsync($"users/{userId}?apiKey={ApiKey}");
            Console.WriteLine($"[API] Response status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[API] GetUserAsync failed: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] Error content: {errorContent}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[API] Response length: {content.Length} chars");
            var data = JsonConvert.DeserializeObject<JObject>(content);

            if (data == null)
            {
                Console.WriteLine("[API] Failed to parse user data");
                return null;
            }

            Console.WriteLine($"[API] Parsed user: {data["displayName"]}");

            var badges = data["badges"]?.ToObject<List<JObject>>() ?? new List<JObject>();
            var badgeNames = badges.Select(b => b["badgeId"]?.ToString() ?? "").ToList();
            var tags = data["tags"]?.ToObject<List<string>>() ?? new List<string>();
            
            // Check for 18+ age verification
            // VRChat uses: ageVerificationStatus = "18+" and ageVerified = true
            var ageVerificationStatus = data["ageVerificationStatus"]?.ToString();
            var ageVerified = data["ageVerified"]?.Value<bool>() ?? false;
            
            bool isAgeVerified18Plus = 
                (ageVerificationStatus == "18+" && ageVerified) ||
                badgeNames.Any(b => b.Contains("age_verification", StringComparison.OrdinalIgnoreCase)) ||
                tags.Any(t => t.Contains("age_verified", StringComparison.OrdinalIgnoreCase));
            
            Console.WriteLine($"[USER] {data["displayName"]} - ageVerificationStatus: {ageVerificationStatus}, ageVerified: {ageVerified}, IsAgeVerified18+: {isAgeVerified18Plus}");

            // Get profile picture - prefer userIcon, fallback to avatar thumbnail
            var profilePicUrl = data["userIcon"]?.ToString();
            if (string.IsNullOrEmpty(profilePicUrl))
            {
                profilePicUrl = data["currentAvatarThumbnailImageUrl"]?.ToString();
            }
            Console.WriteLine($"[USER] Profile pic URL: {profilePicUrl}");

            return new UserDetails
            {
                UserId = data["id"]?.ToString() ?? userId,
                DisplayName = data["displayName"]?.ToString() ?? "Unknown",
                Bio = data["bio"]?.ToString(),
                ProfilePicUrl = profilePicUrl,
                Badges = badgeNames,
                Tags = tags,
                IsAgeVerified = isAgeVerified18Plus
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] GetUserAsync exception: {ex.Message}");
            Console.WriteLine($"[API] Stack: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<List<UserSearchResult>> SearchUsersAsync(string query)
    {
        var results = new List<UserSearchResult>();
        try
        {
            await RateLimitAsync();
            
            var encodedQuery = Uri.EscapeDataString(query);
            var response = await _httpClient.GetAsync($"users?apiKey={ApiKey}&search={encodedQuery}&n=25");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[SEARCH] Error: {response.StatusCode}");
                return results;
            }

            var content = await response.Content.ReadAsStringAsync();
            var users = JsonConvert.DeserializeObject<List<JObject>>(content) ?? new List<JObject>();

            foreach (var user in users)
            {
                // Prefer userIcon over avatar thumbnail
                var profilePic = user["userIcon"]?.ToString();
                if (string.IsNullOrEmpty(profilePic))
                {
                    profilePic = user["currentAvatarThumbnailImageUrl"]?.ToString();
                }
                
                results.Add(new UserSearchResult
                {
                    UserId = user["id"]?.ToString() ?? "",
                    DisplayName = user["displayName"]?.ToString() ?? "Unknown",
                    ProfilePicUrl = profilePic,
                    StatusDescription = user["statusDescription"]?.ToString()
                });
            }
            
            Console.WriteLine($"[SEARCH] Found {results.Count} users for query: {query}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEARCH] Exception: {ex.Message}");
        }
        return results;
    }

    public async Task<GroupMemberApiResult?> GetGroupMemberAsync(string groupId, string userId)
    {
        try
        {
            await RateLimitAsync();
            
            var response = await _httpClient.GetAsync($"groups/{groupId}/members/{userId}?apiKey={ApiKey}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP_MEMBER] User {userId} not found in group or error: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(content);

            if (data == null) return null;

            return new GroupMemberApiResult
            {
                UserId = data["userId"]?.ToString() ?? userId,
                DisplayName = data["user"]?["displayName"]?.ToString() ?? "Unknown",
                JoinedAt = data["joinedAt"]?.ToString(),
                RoleIds = data["roleIds"]?.ToObject<List<string>>() ?? new List<string>(),
                IsRepresenting = data["isRepresenting"]?.Value<bool>() ?? false
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP_MEMBER] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SendGroupInviteAsync(string groupId, string userId)
    {
        try
        {
            await RateLimitAsync();
            var payload = new { userId };
            var response = await SendJsonAsync(HttpMethod.Post, $"groups/{groupId}/invites", payload);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROUP-INVITE] Status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-INVITE] Error: {content}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-INVITE] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<GroupPostResult?> CreateGroupPostAsync(string groupId, string title, string text, IEnumerable<string>? roleIds = null, string visibility = "public", bool sendNotification = true, string? imageId = null)
    {
        try
        {
            await RateLimitAsync();

            var payload = new Dictionary<string, object?>
            {
                ["title"] = title,
                ["text"] = text,
                ["visibility"] = visibility,
                ["sendNotification"] = sendNotification,
                ["roleIds"] = roleIds?.ToArray()
            };

            if (!string.IsNullOrWhiteSpace(imageId))
            {
                payload["imageId"] = imageId;
            }

            var response = await SendJsonAsync(HttpMethod.Post, $"groups/{groupId}/posts", payload);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROUP-POST] Status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-POST] Error: {content}");
                return null;
            }

            try
            {
                var json = JsonConvert.DeserializeObject<JObject>(content);
                return new GroupPostResult
                {
                    Id = json?["id"]?.ToString(),
                    Title = json?["title"]?.ToString(),
                    Text = json?["text"]?.ToString(),
                    Visibility = json?["visibility"]?.ToString(),
                    ImageId = json?["imageId"]?.ToString()
                };
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"[GROUP-POST] Parse error: {parseEx.Message}");
                return new GroupPostResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-POST] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<List<GroupRole>> GetGroupRolesAsync(string groupId)
    {
        var roles = new List<GroupRole>();
        try
        {
            await RateLimitAsync();
            var response = await _httpClient.GetAsync($"groups/{groupId}/roles?apiKey={ApiKey}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ROLES] Error: {response.StatusCode}");
                return roles;
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<List<JObject>>(content);

            if (data != null)
            {
                foreach (var roleData in data)
                {
                    roles.Add(new GroupRole
                    {
                        RoleId = roleData["id"]?.ToString() ?? "",
                        Name = roleData["name"]?.ToString() ?? "Unknown",
                        Description = roleData["description"]?.ToString(),
                        Permissions = roleData["permissions"]?.ToObject<List<string>>() ?? new List<string>()
                    });
                }
            }
            Console.WriteLine($"[ROLES] Loaded {roles.Count} roles");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ROLES] Exception: {ex.Message}");
        }
        return roles;
    }

    public async Task<bool> AssignGroupRoleAsync(string groupId, string userId, string roleId)
    {
        try
        {
            await RateLimitAsync();
            var request = new HttpRequestMessage(HttpMethod.Put, $"groups/{groupId}/members/{userId}/roles/{roleId}?apiKey={ApiKey}");
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[ASSIGN_ROLE] User: {userId}, Role: {roleId}, Status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ASSIGN_ROLE] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveGroupRoleAsync(string groupId, string userId, string roleId)
    {
        try
        {
            await RateLimitAsync();
            var request = new HttpRequestMessage(HttpMethod.Delete, $"groups/{groupId}/members/{userId}/roles/{roleId}?apiKey={ApiKey}");
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[REMOVE_ROLE] User: {userId}, Role: {roleId}, Status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REMOVE_ROLE] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> KickGroupMemberAsync(string groupId, string userId)
    {
        try
        {
            await RateLimitAsync();
            var request = new HttpRequestMessage(HttpMethod.Delete, $"groups/{groupId}/members/{userId}?apiKey={ApiKey}");
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[KICK] User: {userId}, Status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KICK] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> BanGroupMemberAsync(string groupId, string userId)
    {
        try
        {
            await RateLimitAsync();
            var payload = JsonConvert.SerializeObject(new { userId });
            var request = new HttpRequestMessage(HttpMethod.Post, $"groups/{groupId}/bans?apiKey={ApiKey}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[BAN] User: {userId}, Status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BAN] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UnbanGroupMemberAsync(string groupId, string userId)
    {
        try
        {
            await RateLimitAsync();
            var request = new HttpRequestMessage(HttpMethod.Delete, $"groups/{groupId}/bans/{userId}?apiKey={ApiKey}");
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[UNBAN] User: {userId}, Status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UNBAN] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<JsonElement?> GetGroupAuditLogsAsync(string groupId, int count = 100, int offset = 0)
    {
        try
        {
            Console.WriteLine($"[AUDIT-API] ========== FETCHING AUDIT LOGS ==========");
            Console.WriteLine($"[AUDIT-API] Group: {groupId}");
            Console.WriteLine($"[AUDIT-API] Count: {count}, Offset: {offset}");
            
            await RateLimitAsync();
            var url = $"groups/{groupId}/auditLogs?apiKey={ApiKey}&n={count}&offset={offset}";
            Console.WriteLine($"[AUDIT-API] Request URL: {url}");
            
            var response = await _httpClient.GetAsync(url);
            Console.WriteLine($"[AUDIT-API] Response Status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[AUDIT-API] Error response: {errorContent}");
                return null;
            }
            
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[AUDIT-API] Response length: {content.Length} chars");
            
            var json = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);
            
            // Log summary of results
            if (json.TryGetProperty("results", out var results))
            {
                var resultArray = results.EnumerateArray().ToList();
                Console.WriteLine($"[AUDIT-API] Results count: {resultArray.Count}");
                
                // Show first few entries as samples
                foreach (var entry in resultArray.Take(3))
                {
                    var eventType = entry.TryGetProperty("eventType", out var et) ? et.GetString() : "unknown";
                    var actorName = entry.TryGetProperty("actorDisplayName", out var an) ? an.GetString() : "unknown";
                    var targetName = entry.TryGetProperty("targetDisplayName", out var tn) ? tn.GetString() : "";
                    Console.WriteLine($"[AUDIT-API]   - {eventType}: {actorName} → {targetName}");
                }
                if (resultArray.Count > 3)
                {
                    Console.WriteLine($"[AUDIT-API]   ... and {resultArray.Count - 3} more entries");
                }
            }
            Console.WriteLine($"[AUDIT-API] ========================================");
            
            return json;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIT-API] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[AUDIT-API] Stack: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<string?> UploadImageAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine($"[EVENT-API] Image file missing: {filePath}");
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(filePath);
            var mime = GetMimeType(filePath);
            var endpoints = new[] { "images", "image" }; // try both in case VRChat expects singular/plural

            foreach (var endpoint in endpoints)
            {
                try
                {
                    await RateLimitAsync();
                    using var content = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(bytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
                    content.Add(fileContent, "file", Path.GetFileName(filePath));

                    var response = await _httpClient.PostAsync($"{endpoint}?apiKey={ApiKey}", content);
                    var respText = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[EVENT-API] Upload {endpoint} status: {response.StatusCode}");
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[EVENT-API] Upload {endpoint} error: {respText}");
                        continue;
                    }

                    var obj = JsonConvert.DeserializeObject<JObject>(respText);
                    var imageId = obj?["id"]?.ToString() ?? obj?["imageId"]?.ToString() ?? obj?["file"]?["id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(imageId))
                    {
                        Console.WriteLine($"[EVENT-API] Uploaded imageId: {imageId}");
                        return imageId;
                    }
                }
                catch (Exception inner)
                {
                    Console.WriteLine($"[EVENT-API] Upload endpoint {endpoint} exception: {inner.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENT-API] Upload failed: {ex.Message}");
        }

        return null;
    }

    public async Task<GroupCalendarEvent?> CreateGroupEventAsync(string groupId, GroupEventCreateRequest request)
    {
        try
        {
            var url = $"calendar/{groupId}/event?apiKey={ApiKey}";
            var response = await SendJsonAsync(HttpMethod.Post, url, request);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[EVENT-API] Create status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EVENT-API] Error: {content}");
                throw new InvalidOperationException($"VRChat create failed ({response.StatusCode}): {content}");
            }
            return System.Text.Json.JsonSerializer.Deserialize<GroupCalendarEvent>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENT-API] Create exception: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DeleteGroupEventAsync(string groupId, string eventId)
    {
        try
        {
            var url = $"calendar/{groupId}/event/{eventId}?apiKey={ApiKey}";
            var response = await SendJsonAsync(HttpMethod.Delete, url, null);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[EVENT-API] Delete status: {response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine("[EVENT-API] Delete: event already missing on VRChat.");
                // Try alternate endpoint by event id alone (some APIs allow this form)
                var altUrl = $"calendar/event/{eventId}?apiKey={ApiKey}";
                var altResp = await SendJsonAsync(HttpMethod.Delete, altUrl, null);
                var altContent = await altResp.Content.ReadAsStringAsync();
                Console.WriteLine($"[EVENT-API] Alt delete status: {altResp.StatusCode}");
                if (altResp.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.WriteLine("[EVENT-API] Alt delete also reports missing; treating as deleted.");
                    return true;
                }
                if (!altResp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[EVENT-API] Alt delete error: {altContent}");
                    return false;
                }
                return true;
            }
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EVENT-API] Delete error: {content}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENT-API] Delete exception: {ex.Message}");
            return false;
        }
    }

    public async Task<List<GroupCalendarEvent>> GetGroupEventsAsync(string groupId)
    {
        var results = new List<GroupCalendarEvent>();
        const int pageSize = 100;
        var offset = 0;
        var date = DateTime.UtcNow.ToString("o");

        try
        {
            while (true)
            {
                var url = $"calendar/{groupId}?apiKey={ApiKey}&n={pageSize}&offset={offset}&date={Uri.EscapeDataString(date)}";
                var response = await SendJsonAsync(HttpMethod.Get, url, null);
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[EVENT-API] List status: {response.StatusCode} (offset={offset})");
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[EVENT-API] Error: {content}");
                    break;
                }

                // VRChat may return either a bare array or a wrapped object with "results" and "hasNext".
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var parsedList = System.Text.Json.JsonSerializer.Deserialize<List<GroupCalendarEvent>>(content, _jsonOptions) ?? new List<GroupCalendarEvent>();
                        results.AddRange(parsedList);
                        if (parsedList.Count < pageSize)
                        {
                            break;
                        }
                        offset += pageSize;
                        continue;
                    }

                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var parsedWrapped = System.Text.Json.JsonSerializer.Deserialize<GroupEventListResponse>(content, _jsonOptions);
                        if (parsedWrapped != null)
                        {
                            results.AddRange(parsedWrapped.Results ?? new List<GroupCalendarEvent>());
                            if (!parsedWrapped.HasNext || (parsedWrapped.Results?.Count ?? 0) < pageSize)
                            {
                                break;
                            }
                            offset += pageSize;
                            continue;
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"[EVENT-API] Parse error: {parseEx.Message}");
                }

                // Unknown shape
                Console.WriteLine("[EVENT-API] Unexpected list response shape; stopping.");
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EVENT-API] List exception: {ex.Message}");
        }

        return results;
    }

    public async Task<WorldInfo?> GetWorldAsync(string worldId)
    {
        try
        {
            await RateLimitAsync();
            var response = await _httpClient.GetAsync($"worlds/{worldId}?apiKey={ApiKey}");
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[WORLD-API] GetWorld status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[WORLD-API] Error: {content}");
                return null;
            }
            return JsonConvert.DeserializeObject<WorldInfo>(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WORLD-API] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<List<WorldInfo>> SearchWorldsAsync(string query, int n = 20, int offset = 0, string sort = "relevance")
    {
        var results = new List<WorldInfo>();
        try
        {
            await RateLimitAsync();
            var url = $"worlds?apiKey={ApiKey}&n={n}&offset={offset}&sort={sort}&search={Uri.EscapeDataString(query ?? string.Empty)}&tag=system_approved";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[WORLD-API] Search status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[WORLD-API] Error: {content}");
                return results;
            }
            var parsed = JsonConvert.DeserializeObject<List<WorldInfo>>(content);
            if (parsed != null) results = parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WORLD-API] Search exception: {ex.Message}");
        }
        return results;
    }

    public async Task<bool> SelfInviteAsync(string worldId, string instanceId, string? shortName = null, string? userId = null)
    {
        try
        {
            await RateLimitAsync();

            // VRCX posts the raw worldId:instanceId path; keep it unencoded and pass shortName as a query param only.
            var url = $"invite/myself/to/{worldId}:{instanceId}";
            if (!string.IsNullOrWhiteSpace(shortName))
            {
                url += $"?shortName={Uri.EscapeDataString(shortName)}";
            }

            var response = await _httpClient.PostAsync(url, null);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[INVITE-API] SelfInvite status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[INVITE-API] Error: {content}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INVITE-API] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<InstanceCreateResult?> CreateInstanceAsync(
        string worldId,
        string region,
        string groupId,
        string groupAccessType,
        bool ageGate,
        bool queueEnabled,
        string? displayName = null,
        string? shortName = null,
        bool canRequestInvite = false,
        IEnumerable<string>? roleIds = null,
        string type = "group")
    {
        try
        {
            await RateLimitAsync();

            // Mirror VRCX: let the API build the instanceId; send only the access/ownership fields.
            var payload = new Dictionary<string, object?>
            {
                ["worldId"] = worldId,
                ["type"] = type,
                ["region"] = region,
                ["ownerId"] = groupId,
                ["groupAccessType"] = groupAccessType,
                ["queueEnabled"] = queueEnabled
            };

            if (ageGate)
            {
                payload["ageGate"] = true;
            }
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                payload["displayName"] = displayName;
            }
            if (!string.IsNullOrWhiteSpace(shortName))
            {
                payload["shortName"] = shortName;
            }
            if (canRequestInvite)
            {
                payload["canRequestInvite"] = true;
            }
            if (roleIds != null && roleIds.Any())
            {
                payload["roleIds"] = roleIds.ToArray();
            }

            var response = await SendJsonAsync(HttpMethod.Post, "instances", payload);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[INSTANCE-API] Create status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[INSTANCE-API] Error: {content}");
                return null;
            }

            try
            {
                var json = JsonConvert.DeserializeObject<JObject>(content);
                return new InstanceCreateResult
                {
                    Location = json?["location"]?.ToString(),
                    WorldId = json?["worldId"]?.ToString(),
                    InstanceId = json?["instanceId"]?.ToString(),
                    ShortName = json?["shortName"]?.ToString(),
                    SecureName = json?["secureName"]?.ToString(),
                    DisplayName = json?["displayName"]?.ToString()
                };
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"[INSTANCE-API] Parse error: {parseEx.Message}");
                return new InstanceCreateResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-API] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<LoginResult> RestoreSessionAsync(string authCookie, string? twoFactorCookie)
    {
        try
        {
            Console.WriteLine("[API] Attempting to restore session from cached cookies");
            
            // Set cookies
            var baseUri = new Uri(BaseUrl);
            _cookieContainer.Add(baseUri, new Cookie("auth", authCookie, "/", ".vrchat.cloud"));
            if (!string.IsNullOrEmpty(twoFactorCookie))
            {
                _cookieContainer.Add(baseUri, new Cookie("twoFactorAuth", twoFactorCookie, "/", ".vrchat.cloud"));
            }
            
            _authToken = authCookie;
            _twoFactorToken = twoFactorCookie;
            
            // Try to get current user to validate session
            await RateLimitAsync();
            var response = await _httpClient.GetAsync($"auth/user?apiKey={ApiKey}");
            var content = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[API] Session restore status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("[API] Session restore failed - session expired");
                Logout();
                return new LoginResult
                {
                    Success = false,
                    Message = "Session expired"
                };
            }
            
            var data = JsonConvert.DeserializeObject<JObject>(content);
            
            // Check if 2FA is still required (session partially valid)
            if (data?["requiresTwoFactorAuth"] != null)
            {
                Console.WriteLine("[API] Session requires re-authentication");
                Logout();
                return new LoginResult
                {
                    Success = false,
                    Requires2FA = true,
                    Message = "Session requires re-authentication"
                };
            }
            
            CurrentUserId = data?["id"]?.ToString();
            CurrentUserDisplayName = data?["displayName"]?.ToString();
            // Prefer userIcon over avatar thumbnail
            CurrentUserProfilePicUrl = data?["userIcon"]?.ToString();
            if (string.IsNullOrEmpty(CurrentUserProfilePicUrl))
            {
                CurrentUserProfilePicUrl = data?["currentAvatarThumbnailImageUrl"]?.ToString()
                    ?? data?["currentAvatarImageUrl"]?.ToString();
            }
            IsLoggedIn = true;
            
            Console.WriteLine($"[API] Session restored for: {CurrentUserDisplayName}");
            Console.WriteLine($"[API] Profile pic: {CurrentUserProfilePicUrl}");
            
            return new LoginResult
            {
                Success = true,
                UserId = CurrentUserId,
                DisplayName = CurrentUserDisplayName,
                Message = "Session restored"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] Session restore exception: {ex.Message}");
            Logout();
            return new LoginResult
            {
                Success = false,
                Message = $"Session restore failed: {ex.Message}"
            };
        }
    }

    public string? GetAuthCookie()
    {
        try
        {
            var cookies = _cookieContainer.GetCookies(new Uri(BaseUrl));
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "auth")
                    return cookie.Value;
            }
        }
        catch { }
        return _authToken;
    }

    public string? GetTwoFactorCookie()
    {
        try
        {
            var cookies = _cookieContainer.GetCookies(new Uri(BaseUrl));
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "twoFactorAuth")
                    return cookie.Value;
            }
        }
        catch { }
        return _twoFactorToken;
    }

    public void Logout()
    {
        IsLoggedIn = false;
        CurrentUserId = null;
        CurrentUserDisplayName = null;
        CurrentUserProfilePicUrl = null;
        CurrentGroupId = null;
        _authToken = null;
        _twoFactorToken = null;
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

    public string RoleSummary => RoleIds == null || RoleIds.Count == 0
        ? string.Empty
        : string.Join(", ", RoleIds);
}

public class GroupBanEntry
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? BannedAt { get; set; }
    public string? ExpiresAt { get; set; }
}

public class GroupRole
{
    public string RoleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
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

public class UserSearchResult
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? ProfilePicUrl { get; set; }
    public string? StatusDescription { get; set; }
}

public class GroupMemberApiResult
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? JoinedAt { get; set; }
    public List<string> RoleIds { get; set; } = new();
    public bool IsRepresenting { get; set; }
}

public class GroupEventCreateRequest
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("startsAt")]
    public DateTime StartsAt { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("endsAt")]
    public DateTime EndsAt { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("sendCreationNotification")]
    public bool SendCreationNotification { get; set; }

    [JsonProperty("accessType")]
    public string AccessType { get; set; } = "public";

    [JsonProperty("languages")]
    public List<string>? Languages { get; set; }

    [JsonProperty("platforms")]
    public List<string>? Platforms { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("imageId")]
    public string? ImageId { get; set; }
}

public class GroupCalendarEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string AccessType { get; set; } = "public";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string? ImageId { get; set; }
    public string? ImageUrl { get; set; }
    public bool SendCreationNotification { get; set; }
    public bool IsDraft { get; set; }
    public List<string> Languages { get; set; } = new();
    public List<string> Platforms { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> RoleIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

internal class GroupEventListResponse
{
    public List<GroupCalendarEvent> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public bool HasNext { get; set; }
}

public class WorldInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonProperty("capacity")]
    public int Capacity { get; set; }

    [JsonProperty("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonProperty("thumbnailImageUrl")]
    public string? ThumbnailImageUrl { get; set; }
}
