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
    Task<List<string>> GetGroupMemberRolesAsync(string groupId, string userId);
    Task<bool> AssignGroupRoleAsync(string groupId, string userId, string roleId);
    Task<bool> RemoveGroupRoleAsync(string groupId, string userId, string roleId);
    Task<bool> RemoveGroupMemberRoleAsync(string groupId, string userId, string roleId);
    Task<bool> KickGroupMemberAsync(string groupId, string userId, string? reason = null, string? description = null);
    Task<bool> BanGroupMemberAsync(string groupId, string userId, string? reason = null, string? description = null);
    Task<bool> WarnUserAsync(string groupId, string userId, string reason, string? description = null);
    Task<bool> UnbanGroupMemberAsync(string groupId, string oderId);
    Task<JsonElement?> GetGroupAuditLogsAsync(string groupId, int count = 100, int offset = 0);
    Task<List<GroupInfo>> GetMyManageableGroupsAsync();
    Task<string?> UploadImageAsync(string filePath);
    Task<GroupCalendarEvent?> CreateGroupEventAsync(string groupId, GroupEventCreateRequest request);
    Task<List<GroupCalendarEvent>> GetGroupEventsAsync(string groupId);
    Task<bool> DeleteGroupEventAsync(string groupId, string eventId);
    Task<List<GroupPost>> GetGroupPostsAsync(string groupId, int n = 60, int offset = 0, bool publicOnly = false);
    Task<bool> DeleteGroupPostAsync(string groupId, string postId);
    Task<GroupPost?> UpdateGroupPostAsync(string groupId, string postId, string title, string text, string visibility = "public", bool sendNotification = false);
    Task<List<FriendInfo>> GetFriendsAsync(bool onlineOnly = false, int n = 100, int offset = 0);
    Task<InstanceDetails?> GetInstanceAsync(string worldId, string instanceId);
    Task<bool> InviteUserToInstanceAsync(string userId, string worldId, string instanceId);
    Task<List<GroupJoinRequest>> GetGroupJoinRequestsAsync(string groupId);
    Task<bool> RespondToGroupJoinRequestAsync(string groupId, string userId, string action);
    Task<List<GroupInstanceDetails>> GetGroupInstancesAsync(string groupId);
    Task<bool> DeleteGroupInstanceAsync(string groupId, string instanceId);
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

public class GroupInstanceDetails
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool AgeGated { get; set; }
    public int UserCount { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? AccessType { get; set; }
    public bool QueueEnabled { get; set; }
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
    public DateTime? CreatedAt { get; set; }
    public List<string> Links { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public List<string> GalleryImageUrls { get; set; } = new();
}

public class GroupPostResult
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Visibility { get; set; }
    public string? ImageId { get; set; }
}

public class GroupPost
{
    public string Id { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string? EditorId { get; set; }
    public string Visibility { get; set; } = "public";
    public List<string> RoleIds { get; set; } = new();
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ImageId { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class VRChatApiService : IVRChatApiService
{
    private const string BaseUrl = "https://api.vrchat.cloud/api/1";
    private const string ApiKey = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
    private const int RateLimitDelayMs = 100;

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private const int LogBodyLimit = 2000; // avoid flooding logs
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
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VRCGT/1.0.1 (https://github.com/VRCGT)");
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

    private void LogHttp(string tag, string url, HttpResponseMessage response, string content, string? extra = null)
    {
        var safeUrl = SanitizeUrl(url);
        var status = $"{(int)response.StatusCode} {response.StatusCode}";
        var prefix = extra == null
            ? $"HTTP {response.RequestMessage?.Method} {safeUrl} {status}"
            : $"HTTP {response.RequestMessage?.Method} {safeUrl} {status} ({extra})";

        LoggingService.Debug(tag, prefix);

        if (content != null)
        {
            var truncated = content.Length > LogBodyLimit
                ? content.Substring(0, LogBodyLimit) + " ... [truncated]"
                : content;
            LoggingService.Debug(tag, $"Body {Math.Min(content.Length, LogBodyLimit)}/{content.Length} chars: {truncated}");
        }
    }

    private static string SanitizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return url.Replace(ApiKey, "***");
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
        int transientFailures = 0;

        try
        {
            while (true)
            {
                await RateLimitAsync();

                var url = $"groups/{groupId}/members?apiKey={ApiKey}&n={limit}&offset={offset}";
                var response = await _httpClient.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                    var delaySeconds = retryAfter.HasValue ? Math.Max(1, (int)retryAfter.Value) : 30;
                    Console.WriteLine($"[MEMBERS] Rate limited. Waiting {delaySeconds}s before retry...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue; // retry same offset
                }

                if ((int)response.StatusCode >= 500)
                {
                    transientFailures++;
                    Console.WriteLine($"[MEMBERS] Server error {response.StatusCode} at offset {offset} (attempt {transientFailures}/3)");
                    if (transientFailures >= 3)
                    {
                        Console.WriteLine("[MEMBERS] Too many server errors, stopping member fetch.");
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    continue; // retry same offset
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[MEMBERS] Error: {response.StatusCode} at offset {offset}");
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
                Console.WriteLine($"[MEMBERS] Fetched {data.Count} members (total so far: {total})");

                if (data.Count < limit)
                    break;

                offset += limit;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEMBERS] Exception: {ex.Message}");
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
                    // Determine if it's an instance ban by checking for instanceId
                    var isInstanceBan = banData["instanceId"] != null && !string.IsNullOrEmpty(banData["instanceId"]?.ToString());
                    
                    // Check if user was a member by looking at the membershipStatus field
                    // If membershipStatus exists and isn't "notMember", they were a member
                    var membershipStatus = banData["membershipStatus"]?.ToString();
                    var wasMember = !string.IsNullOrEmpty(membershipStatus) && membershipStatus != "notMember";
                    
                    var ban = new GroupBanEntry
                    {
                        UserId = banData["userId"]?.ToString() ?? string.Empty,
                        DisplayName = banData["user"]?["displayName"]?.ToString() ?? "Unknown",
                        Reason = banData["reason"]?.ToString() ?? string.Empty,
                        BannedAt = banData["createdAt"]?.ToString(),
                        ExpiresAt = banData["expiresAt"]?.ToString(),
                        IsInstanceBan = isInstanceBan,
                        WasMember = wasMember
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
            var url = $"groups/{groupId}?apiKey={ApiKey}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP", url, response, content);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = JsonConvert.DeserializeObject<JObject>(content);
            if (json == null)
            {
                return null;
            }

            var links = new List<string>();
            if (json["links"] is JArray linkArr)
            {
                foreach (var l in linkArr)
                {
                    if (l.Type == JTokenType.String)
                        links.Add(l.ToString());
                    else if (l.Type == JTokenType.Object)
                    {
                        var linkUrl = l["url"]?.ToString() ?? l["linkUrl"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(linkUrl)) links.Add(linkUrl);
                    }
                }
            }

            var rules = new List<string>();
            if (json["rules"] is JArray ruleArr)
            {
                foreach (var r in ruleArr)
                {
                    if (r.Type == JTokenType.String)
                        rules.Add(r.ToString());
                    else if (r.Type == JTokenType.Object)
                    {
                        var text = r["text"]?.ToString() ?? r["description"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(text)) rules.Add(text);
                    }
                }
            }

            var gallery = new List<string>();
            if (json["gallery"] is JArray galArr)
            {
                foreach (var g in galArr)
                {
                    if (g.Type == JTokenType.String)
                        gallery.Add(g.ToString());
                    else if (g.Type == JTokenType.Object)
                    {
                        var img = g["imageUrl"]?.ToString() ?? g["url"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(img)) gallery.Add(img);
                    }
                }
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
                OwnerId = json["ownerId"]?.ToString(),
                CreatedAt = json["createdAt"]?.Value<DateTime?>(),
                Links = links,
                Rules = rules,
                GalleryImageUrls = gallery
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<List<GroupInfo>> GetMyManageableGroupsAsync()
    {
        var manageableGroups = new List<GroupInfo>();
        var processedGroupIds = new HashSet<string>(); // Track processed groups to avoid duplicates
        
        try
        {
            // Get the current user's ID
            await RateLimitAsync();
            var userResponse = await _httpClient.GetAsync($"auth/user?apiKey={ApiKey}");
            
            if (!userResponse.IsSuccessStatusCode)
            {
                LoggingService.Debug("API", $"Failed to get user info: {userResponse.StatusCode}");
                return manageableGroups;
            }

            var userContent = await userResponse.Content.ReadAsStringAsync();
            var userData = JsonConvert.DeserializeObject<JObject>(userContent);
            
            if (userData == null)
            {
                return manageableGroups;
            }

            var userId = userData["id"]?.ToString();
            if (string.IsNullOrEmpty(userId))
            {
                return manageableGroups;
            }

            // Fetch user's groups - basic list includes ownerId
            int offset = 0;
            const int limit = 100;
            
            while (true)
            {
                await RateLimitAsync();
                var groupsResponse = await _httpClient.GetAsync($"users/{userId}/groups?apiKey={ApiKey}&n={limit}&offset={offset}");
                
                if (!groupsResponse.IsSuccessStatusCode)
                {
                    LoggingService.Debug("API", $"Failed to get user groups: {groupsResponse.StatusCode}");
                    break;
                }

                var groupsContent = await groupsResponse.Content.ReadAsStringAsync();
                var groupsData = JsonConvert.DeserializeObject<List<JObject>>(groupsContent);
                
                if (groupsData == null || groupsData.Count == 0)
                {
                    break;
                }
                
                // Process groups - check ownership directly from this response
                foreach (var groupData in groupsData)
                {
                    try
                    {
                        // In users/{userId}/groups response:
                        // - "id" is the membership ID (gmem_*)
                        // - "groupId" is the actual group ID (grp_*)
                        var membershipId = groupData["id"]?.ToString();
                        var groupId = groupData["groupId"]?.ToString(); // This is the actual group ID
                        var groupName = groupData["name"]?.ToString();
                        var ownerId = groupData["ownerId"]?.ToString();
                        
                        // Skip if we've already processed this group or missing group ID
                        if (string.IsNullOrEmpty(groupId) || processedGroupIds.Contains(groupId))
                        {
                            continue;
                        }
                        
                        // Only add groups where user is the owner
                        if (ownerId == userId)
                        {
                            processedGroupIds.Add(groupId); // Mark as processed
                            LoggingService.Info("API", $"✓ {groupName}: User is OWNER");
                            
                            // Parse group info
                            var links = new List<string>();
                            if (groupData["links"] is JArray linkArr)
                            {
                                foreach (var l in linkArr)
                                {
                                    if (l.Type == JTokenType.String)
                                        links.Add(l.ToString());
                                    else if (l.Type == JTokenType.Object)
                                    {
                                        var linkUrl = l["url"]?.ToString() ?? l["linkUrl"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(linkUrl)) links.Add(linkUrl);
                                    }
                                }
                            }

                            var rules = new List<string>();
                            if (groupData["rules"] is JArray ruleArr)
                            {
                                foreach (var r in ruleArr)
                                {
                                    if (r.Type == JTokenType.String)
                                        rules.Add(r.ToString());
                                    else if (r.Type == JTokenType.Object)
                                    {
                                        var text = r["text"]?.ToString() ?? r["description"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(text)) rules.Add(text);
                                    }
                                }
                            }

                            var gallery = new List<string>();
                            if (groupData["gallery"] is JArray galArr)
                            {
                                foreach (var g in galArr)
                                {
                                    if (g.Type == JTokenType.String)
                                        gallery.Add(g.ToString());
                                    else if (g.Type == JTokenType.Object)
                                    {
                                        var img = g["imageUrl"]?.ToString() ?? g["url"]?.ToString();
                                        if (!string.IsNullOrWhiteSpace(img)) gallery.Add(img);
                                    }
                                }
                            }

                            var groupInfo = new GroupInfo
                            {
                                Id = groupId, // Use groupId, not membershipId
                                Name = groupData["name"]?.ToString() ?? string.Empty,
                                ShortCode = groupData["shortCode"]?.ToString(),
                                Description = groupData["description"]?.ToString(),
                                MemberCount = groupData["memberCount"]?.Value<int?>() ?? 0,
                                OnlineCount = groupData["onlineMemberCount"]?.Value<int?>() ?? 0,
                                IconUrl = groupData["iconUrl"]?.ToString(),
                                BannerUrl = groupData["bannerUrl"]?.ToString(),
                                Privacy = groupData["privacy"]?.ToString(),
                                OwnerId = groupData["ownerId"]?.ToString(),
                                CreatedAt = groupData["createdAt"]?.Value<DateTime?>(),
                                Links = links,
                                Rules = rules,
                                GalleryImageUrls = gallery
                            };

                            manageableGroups.Add(groupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Error("API", $"Error processing group: {ex.Message}");
                    }
                }

                if (groupsData.Count < limit)
                {
                    break;
                }

                offset += limit;
            }

            LoggingService.Info("API", $"Found {manageableGroups.Count} manageable groups");
        }
        catch (Exception ex)
        {
            LoggingService.Error("API", $"Exception in GetMyManageableGroupsAsync: {ex.Message}");
        }

        return manageableGroups;
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
            var url = $"groups/{groupId}/members/{userId}?apiKey={ApiKey}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP_MEMBER", url, response, content);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

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
            var payload = new { userId };

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await RateLimitAsync();
                var response = await SendJsonAsync(HttpMethod.Post, $"groups/{groupId}/invites", payload);
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GROUP-INVITE] Status: {response.StatusCode}");

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                    var delaySeconds = retryAfter.HasValue ? Math.Max(1, (int)retryAfter.Value) : 30;
                    Console.WriteLine($"[GROUP-INVITE] Rate limited. Waiting {delaySeconds}s before retry (attempt {attempt}/3)...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[GROUP-INVITE] Error: {content}");
                    return false;
                }

                return true;
            }

            return false;
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
            var url = $"groups/{groupId}/roles?apiKey={ApiKey}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("ROLES", url, response, content);
            if (!response.IsSuccessStatusCode)
            {
                return roles;
            }

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
            // This endpoint removes a role FROM a user (unassigns the role), NOT deletes the role itself
            var url = $"groups/{groupId}/members/{userId}/roles/{roleId}?apiKey={ApiKey}";
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            var response = await _httpClient.SendAsync(request);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[REMOVE_ROLE] URL: {url}");
            Console.WriteLine($"[REMOVE_ROLE] User: {userId}, Role: {roleId}, Status: {response.StatusCode}");
            Console.WriteLine($"[REMOVE_ROLE] Response: {responseBody}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REMOVE_ROLE] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<List<string>> GetGroupMemberRolesAsync(string groupId, string userId)
    {
        try
        {
            var member = await GetGroupMemberAsync(groupId, userId);
            return member?.RoleIds ?? new List<string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GET_MEMBER_ROLES] Exception: {ex.Message}");
            return new List<string>();
        }
    }

    public async Task<bool> RemoveGroupMemberRoleAsync(string groupId, string userId, string roleId)
    {
        // Alias to RemoveGroupRoleAsync for clarity in security context
        return await RemoveGroupRoleAsync(groupId, userId, roleId);
    }

    public async Task<bool> KickGroupMemberAsync(string groupId, string userId, string? reason = null, string? description = null)
    {
        try
        {
            await RateLimitAsync();
            var request = new HttpRequestMessage(HttpMethod.Delete, $"groups/{groupId}/members/{userId}?apiKey={ApiKey}");
            var response = await _httpClient.SendAsync(request);
            
            Console.WriteLine($"[KICK] User: {userId}, Reason: {reason ?? "N/A"}, Status: {response.StatusCode}");
            
            // Log to moderation database if reason provided
            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(reason))
            {
                try
                {
                    var moderationService = App.Services.GetService(typeof(IModerationService)) as IModerationService;
                    if (moderationService != null)
                    {
                        var moderationRequest = new Models.ModerationActionRequest
                        {
                            ActionType = "kick",
                            TargetUserId = userId,
                            TargetDisplayName = userId, // Will be updated by caller
                            Reason = reason,
                            Description = description,
                            DurationDays = -1
                        };
                        await moderationService.LogModerationActionAsync(groupId, moderationRequest, CurrentUserId ?? "unknown", CurrentUserDisplayName ?? "Unknown");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[KICK] Failed to log moderation action: {ex.Message}");
                }
            }
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KICK] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> BanGroupMemberAsync(string groupId, string userId, string? reason = null, string? description = null)
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
            
            Console.WriteLine($"[BAN] User: {userId}, Reason: {reason ?? "N/A"}, Status: {response.StatusCode}");
            
            // Log to moderation database if reason provided
            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(reason))
            {
                try
                {
                    var moderationService = App.Services.GetService(typeof(IModerationService)) as IModerationService;
                    if (moderationService != null)
                    {
                        var moderationRequest = new Models.ModerationActionRequest
                        {
                            ActionType = "ban",
                            TargetUserId = userId,
                            TargetDisplayName = userId, // Will be updated by caller
                            Reason = reason,
                            Description = description,
                            DurationDays = 0, // Permanent by default
                            AllowsAppeal = true
                        };
                        await moderationService.LogModerationActionAsync(groupId, moderationRequest, CurrentUserId ?? "unknown", CurrentUserDisplayName ?? "Unknown");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BAN] Failed to log moderation action: {ex.Message}");
                }
            }
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BAN] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> WarnUserAsync(string groupId, string userId, string reason, string? description = null)
    {
        try
        {
            // Warnings are not a VRChat API action, only local logging
            var moderationService = App.Services.GetService(typeof(IModerationService)) as IModerationService;
            if (moderationService != null)
            {
                var moderationRequest = new Models.ModerationActionRequest
                {
                    ActionType = "warning",
                    TargetUserId = userId,
                    TargetDisplayName = userId, // Will be updated by caller
                    Reason = reason,
                    Description = description,
                    DurationDays = -1
                };
                await moderationService.LogModerationActionAsync(groupId, moderationRequest, CurrentUserId ?? "unknown", CurrentUserDisplayName ?? "Unknown");
                LoggingService.Info("API", $"Warning issued to {userId}: {reason}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Exception: {ex.Message}");
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

    public async Task<List<GroupPost>> GetGroupPostsAsync(string groupId, int n = 60, int offset = 0, bool publicOnly = false)
    {
        var posts = new List<GroupPost>();
        try
        {
            await RateLimitAsync();
            var url = $"groups/{groupId}/posts?apiKey={ApiKey}&n={n}&offset={offset}&publicOnly={publicOnly.ToString().ToLower()}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP-POSTS", url, response, content);
            if (!response.IsSuccessStatusCode)
            {
                return posts;
            }

            var json = JObject.Parse(content);
            var postsArray = json["posts"] as JArray;
            if (postsArray == null)
            {
                return posts;
            }

            foreach (var p in postsArray)
            {
                var post = new GroupPost
                {
                    Id = p["id"]?.ToString() ?? string.Empty,
                    GroupId = p["groupId"]?.ToString() ?? string.Empty,
                    AuthorId = p["authorId"]?.ToString() ?? string.Empty,
                    EditorId = p["editorId"]?.ToString(),
                    Visibility = p["visibility"]?.ToString() ?? "public",
                    Title = p["title"]?.ToString() ?? string.Empty,
                    Text = p["text"]?.ToString() ?? string.Empty,
                    ImageId = p["imageId"]?.ToString(),
                    ImageUrl = p["imageUrl"]?.ToString()
                };

                if (p["roleId"] is JArray rolesArr)
                {
                    post.RoleIds = rolesArr.Select(r => r.ToString()).ToList();
                }

                if (DateTime.TryParse(p["createdAt"]?.ToString(), out var created))
                {
                    post.CreatedAt = created;
                }
                if (DateTime.TryParse(p["updatedAt"]?.ToString(), out var updated))
                {
                    post.UpdatedAt = updated;
                }

                posts.Add(post);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-POSTS] Exception: {ex.Message}");
        }
        return posts;
    }

    public async Task<bool> DeleteGroupPostAsync(string groupId, string postId)
    {
        try
        {
            await RateLimitAsync();
            var url = $"groups/{groupId}/posts/{postId}?apiKey={ApiKey}";
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP-POST-DELETE", url, response, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-POST-DELETE] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<GroupPost?> UpdateGroupPostAsync(string groupId, string postId, string title, string text, string visibility = "public", bool sendNotification = false)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["title"] = title,
                ["text"] = text,
                ["visibility"] = visibility,
                ["sendNotification"] = sendNotification
            };

            var response = await SendJsonAsync(HttpMethod.Put, $"groups/{groupId}/posts/{postId}", payload);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GROUP-POST-UPDATE] Status: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-POST-UPDATE] Error: {content}");
                return null;
            }

            var json = JObject.Parse(content);
            return new GroupPost
            {
                Id = json["id"]?.ToString() ?? string.Empty,
                GroupId = json["groupId"]?.ToString() ?? string.Empty,
                AuthorId = json["authorId"]?.ToString() ?? string.Empty,
                EditorId = json["editorId"]?.ToString(),
                Visibility = json["visibility"]?.ToString() ?? "public",
                Title = json["title"]?.ToString() ?? string.Empty,
                Text = json["text"]?.ToString() ?? string.Empty,
                ImageId = json["imageId"]?.ToString(),
                ImageUrl = json["imageUrl"]?.ToString(),
                CreatedAt = DateTime.TryParse(json["createdAt"]?.ToString(), out var created) ? created : DateTime.UtcNow,
                UpdatedAt = DateTime.TryParse(json["updatedAt"]?.ToString(), out var updated) ? updated : null
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-POST-UPDATE] Exception: {ex.Message}");
            return null;
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
                LogHttp("EVENT-API", url, response, content, $"offset={offset}");
                if (!response.IsSuccessStatusCode)
                {
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

    public async Task<List<FriendInfo>> GetFriendsAsync(bool onlineOnly = false, int n = 100, int offset = 0)
    {
        var friends = new List<FriendInfo>();
        try
        {
            await RateLimitAsync();
            
            var url = onlineOnly
                ? $"auth/user/friends?apiKey={ApiKey}&offline=false&n={n}&offset={offset}"
                : $"auth/user/friends?apiKey={ApiKey}&n={n}&offset={offset}";
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[FRIENDS-API] Error: {response.StatusCode}");
                return friends;
            }

            var content = await response.Content.ReadAsStringAsync();
            var users = JsonConvert.DeserializeObject<List<JObject>>(content) ?? new List<JObject>();

            foreach (var user in users)
            {
                var profilePic = user["userIcon"]?.ToString();
                if (string.IsNullOrEmpty(profilePic))
                {
                    profilePic = user["currentAvatarThumbnailImageUrl"]?.ToString();
                }

                var tags = user["tags"]?.ToObject<List<string>>() ?? new List<string>();
                var isAgeVerified = tags.Contains("system_age_verified") || tags.Contains("system_age_verification_approved");
                
                friends.Add(new FriendInfo
                {
                    UserId = user["id"]?.ToString() ?? string.Empty,
                    DisplayName = user["displayName"]?.ToString() ?? string.Empty,
                    ProfilePicUrl = profilePic,
                    StatusDescription = user["statusDescription"]?.ToString(),
                    Status = user["status"]?.ToString() ?? "offline",
                    Location = user["location"]?.ToString() ?? string.Empty,
                    IsAgeVerified = isAgeVerified,
                    Tags = tags
                });
            }

            Console.WriteLine($"[FRIENDS-API] Retrieved {friends.Count} friends");
            return friends;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FRIENDS-API] Exception: {ex.Message}");
            return friends;
        }
    }

    public async Task<InstanceDetails?> GetInstanceAsync(string worldId, string instanceId)
    {
        try
        {
            await RateLimitAsync();
            
            var url = $"instances/{worldId}:{instanceId}?apiKey={ApiKey}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[INSTANCE-API] Error: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<JObject>(content);

            if (data == null)
                return null;

            return new InstanceDetails
            {
                InstanceId = data["id"]?.ToString() ?? string.Empty,
                WorldId = data["worldId"]?.ToString() ?? string.Empty,
                Name = data["name"]?.ToString() ?? string.Empty,
                Region = data["region"]?.ToString() ?? string.Empty,
                Capacity = data["capacity"]?.Value<int>() ?? 0,
                UserCount = data["n_users"]?.Value<int>() ?? 0,
                OwnerId = data["ownerId"]?.ToString(),
                Tags = data["tags"]?.ToObject<List<string>>() ?? new List<string>()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INSTANCE-API] Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> InviteUserToInstanceAsync(string userId, string worldId, string instanceId)
    {
        try
        {
            await RateLimitAsync();

            var payload = new
            {
                instanceId = $"{worldId}:{instanceId}"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"invite/{userId}?apiKey={ApiKey}")
            {
                Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"[INVITE-API] Invite status: {response.StatusCode}");
            
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

    public async Task<List<GroupJoinRequest>> GetGroupJoinRequestsAsync(string groupId)
    {
        try
        {
            await RateLimitAsync();
            var url = $"groups/{groupId}/requests?apiKey={ApiKey}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            
            LogHttp("GROUP-REQUESTS", url, response, content);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-REQUESTS] Failed to get join requests: {response.StatusCode}");
                return new List<GroupJoinRequest>();
            }

            var json = JsonConvert.DeserializeObject<JArray>(content);
            if (json == null)
            {
                return new List<GroupJoinRequest>();
            }

            var requests = new List<GroupJoinRequest>();
            foreach (var item in json)
            {
                var userObj = item["user"] as JObject;
                if (userObj == null) continue;

                var tags = userObj["tags"]?.ToObject<List<string>>() ?? new List<string>();
                var isAgeVerified = tags.Any(t => t.Contains("system_age_verified") || t.Contains("age_verified"));

                requests.Add(new GroupJoinRequest
                {
                    UserId = userObj["id"]?.ToString() ?? "",
                    DisplayName = userObj["displayName"]?.ToString() ?? "",
                    ProfilePicUrl = userObj["currentAvatarImageUrl"]?.ToString() ?? userObj["profilePicOverride"]?.ToString(),
                    CreatedAt = item["createdAt"]?.ToString(),
                    IsAgeVerified = isAgeVerified,
                    Tags = tags
                });
            }

            return requests;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-REQUESTS] Exception: {ex.Message}");
            return new List<GroupJoinRequest>();
        }
    }

    public async Task<bool> RespondToGroupJoinRequestAsync(string groupId, string userId, string action)
    {
        try
        {
            await RateLimitAsync();

            var payload = new
            {
                action = action // "accept" or "reject"
            };

            var url = $"groups/{groupId}/requests/{userId}?apiKey={ApiKey}";
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP-REQUEST-RESPOND", url, response, content);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-REQUEST-RESPOND] Failed to {action} user {userId}: {response.StatusCode}");
                return false;
            }

            Console.WriteLine($"[GROUP-REQUEST-RESPOND] Successfully {action}ed user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-REQUEST-RESPOND] Exception: {ex.Message}");
            return false;
        }
    }

    public async Task<List<GroupInstanceDetails>> GetGroupInstancesAsync(string groupId)
    {
        var instances = new List<GroupInstanceDetails>();
        
        try
        {
            await RateLimitAsync();
            
            var url = $"groups/{groupId}/instances?apiKey={ApiKey}";
            Console.WriteLine($"[GROUP-INSTANCES] Fetching instances for group: {groupId}");
            
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP-INSTANCES", url, response, content);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-INSTANCES] Failed to get instances: {response.StatusCode}");
                return instances;
            }
            
            var json = JArray.Parse(content);
            
            foreach (var item in json)
            {
                try
                {
                    var instance = new GroupInstanceDetails
                    {
                        InstanceId = item["instanceId"]?.ToString() ?? item["id"]?.ToString() ?? "",
                        WorldId = item["worldId"]?.ToString() ?? item["location"]?.ToString()?.Split(':').FirstOrDefault() ?? "",
                        Region = item["region"]?.ToString() ?? "unknown",
                        UserCount = item["userCount"]?.Value<int>() ?? item["n_users"]?.Value<int>() ?? 0,
                        OwnerId = item["ownerId"]?.ToString() ?? "",
                        QueueEnabled = item["queueEnabled"]?.Value<bool>() ?? false
                    };
                    
                    // Try to get age gate info
                    if (item["ageGate"] != null)
                    {
                        instance.AgeGated = item["ageGate"].Value<bool>();
                    }
                    else if (item["ageVerified"] != null)
                    {
                        instance.AgeGated = item["ageVerified"].Value<bool>();
                    }
                    
                    // Try to get world name from nested world object or separate field
                    if (item["world"] != null)
                    {
                        instance.WorldName = item["world"]["name"]?.ToString() ?? "";
                    }
                    else if (item["worldName"] != null)
                    {
                        instance.WorldName = item["worldName"].ToString();
                    }
                    
                    // Try to get owner name
                    if (item["owner"] != null)
                    {
                        instance.OwnerName = item["owner"]["displayName"]?.ToString() ?? "";
                    }
                    else if (item["ownerDisplayName"] != null)
                    {
                        instance.OwnerName = item["ownerDisplayName"].ToString();
                    }
                    
                    // Try to get creation time
                    if (item["createdAt"] != null || item["created_at"] != null)
                    {
                        var createdAtStr = item["createdAt"]?.ToString() ?? item["created_at"]?.ToString();
                        if (DateTime.TryParse(createdAtStr, out var createdAt))
                        {
                            instance.CreatedAt = createdAt;
                        }
                    }
                    
                    // Get access type
                    instance.AccessType = item["groupAccessType"]?.ToString() ?? item["accessType"]?.ToString();
                    
                    instances.Add(instance);
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"[GROUP-INSTANCES] Failed to parse instance: {parseEx.Message}");
                }
            }
            
            Console.WriteLine($"[GROUP-INSTANCES] Found {instances.Count} instances");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-INSTANCES] Exception: {ex.Message}");
        }
        
        return instances;
    }

    public async Task<bool> DeleteGroupInstanceAsync(string groupId, string instanceId)
    {
        try
        {
            await RateLimitAsync();
            
            var url = $"groups/{groupId}/instances/{instanceId}?apiKey={ApiKey}";
            Console.WriteLine($"[GROUP-INSTANCE-DELETE] Deleting instance: {instanceId}");
            
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            LogHttp("GROUP-INSTANCE-DELETE", url, response, content);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[GROUP-INSTANCE-DELETE] Failed to delete instance: {response.StatusCode}");
                return false;
            }
            
            Console.WriteLine($"[GROUP-INSTANCE-DELETE] Successfully deleted instance: {instanceId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP-INSTANCE-DELETE] Exception: {ex.Message}");
            return false;
        }
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
    public bool IsInstanceBan { get; set; }
    public bool WasMember { get; set; }
    
    public string BanTypeDisplay => IsInstanceBan ? "Instance Ban" : "Group Ban";
    public string MembershipDisplay => WasMember ? "Was Member" : "Not Member";
}

public class GroupJoinRequest
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePicUrl { get; set; }
    public string? CreatedAt { get; set; }
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
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
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
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

public class FriendInfo
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePicUrl { get; set; }
    public string? StatusDescription { get; set; }
    public string Status { get; set; } = "offline";
    public string Location { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
    
    public bool IsOnline => Status == "active" || Status == "join me" || Status == "ask me";
    public bool IsInInstance => !string.IsNullOrEmpty(Location) && Location != "offline" && Location != "private";
}

public class InstanceDetails
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int UserCount { get; set; }
    public string? OwnerId { get; set; }
    public List<string> Tags { get; set; } = new();
}
