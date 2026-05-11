using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// v2 VRChat API client.
/// <para>
/// Design rules (see Stage 7 spec):<br/>
/// • No WPF references, no App.Services, no MessageBox.<br/>
/// • All errors are typed exceptions or nullable returns.<br/>
/// • 429 responses are detected and either retried (paged calls) or thrown as
///   <see cref="VrchatRateLimitException"/>.<br/>
/// • Cookies/tokens are never logged or returned in public API objects.
/// </para>
/// </summary>
public sealed class VrchatApiClient : IVrchatApiClient
{
    // VRChat's public client API key — present in every official VRChat client build.
    // Not a secret; included here so we can append it as required by the API.
    private const string ApiKey  = "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26";
    private const int    MaxRetries = 3;

    private readonly VrchatSessionStore   _store;
    private readonly ILogger<VrchatApiClient> _log;

    public VrchatApiClient(VrchatSessionStore store, ILogger<VrchatApiClient> log)
    {
        _store = store;
        _log   = log;
    }

    // ── Session ───────────────────────────────────────────────────────────────

    public VrchatSessionDto GetSession() => _store.GetSession();

    public async Task<VrchatLoginResultDto> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}"));

        await _store.ThrottleAsync(ct).ConfigureAwait(false);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"auth/user?apiKey={ApiKey}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var resp = await _store.Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        _log.LogDebug("VRChat login: {status}", resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
        {
            var msg = TryExtractErrorMessage(body) ?? "Login failed.";
            return Fail(msg);
        }

        return ParseLoginResponse(body);
    }

    public async Task<VrchatLoginResultDto> Verify2FAAsync(
        string code, string authType, CancellationToken ct = default)
    {
        var endpoint = authType switch
        {
            "otp"      => "auth/twofactorauth/otp/verify",
            "emailotp" => "auth/twofactorauth/emailotp/verify",
            _          => "auth/twofactorauth/totp/verify",
        };

        var (resp, body) = await SendAsync(
            HttpMethod.Post, $"{endpoint}?apiKey={ApiKey}",
            payload: new { code }, paged: false, ct);

        _log.LogDebug("VRChat 2FA verify: {status}", resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
            return Fail("Invalid 2FA code.");

        using var doc   = JsonDocument.Parse(body);
        var verified = doc.RootElement.TryGetProperty("verified", out var v) && v.GetBoolean();

        if (!verified)
            return Fail("2FA verification returned verified = false.");

        // Fetch full user after successful 2FA
        var (ur, ub) = await SendAsync(HttpMethod.Get, $"auth/user?apiKey={ApiKey}", ct: ct);
        if (!ur.IsSuccessStatusCode)
            return Fail("2FA passed but fetching user info failed.");

        return ParseLoginResponse(ub);
    }

    public async Task<VrchatLoginResultDto> RestoreSessionAsync(
        string authCookie, string? twoFactorCookie, CancellationToken ct = default)
    {
        _store.InjectCookies(authCookie, twoFactorCookie);

        var (resp, body) = await SendAsync(HttpMethod.Get, $"auth/user?apiKey={ApiKey}", ct: ct);
        _log.LogDebug("VRChat session restore: {status}", resp.StatusCode);

        if (!resp.IsSuccessStatusCode)
        {
            _store.ClearSession();
            return Fail("Stored session has expired.");
        }

        return ParseLoginResponse(body);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            // Best-effort server-side logout — VRChat DELETE /auth/session
            await SendAsync(HttpMethod.Delete, $"auth/session?apiKey={ApiKey}", ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "VRChat server-side logout request failed (non-fatal).");
        }
        finally
        {
            _store.ClearSession();
        }
    }

    // ── Current user ──────────────────────────────────────────────────────────

    public async Task<VrchatCurrentUserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(HttpMethod.Get, $"auth/user?apiKey={ApiKey}", ct: ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var tags = ParseStringArray(root, "tags");
        var ageVerificationStatus = Str(root, "ageVerificationStatus");
        var ageVerified = root.TryGetProperty("ageVerified", out var av) && av.GetBoolean();
        bool is18Plus = (ageVerificationStatus == "18+" && ageVerified)
                     || tags.Any(t => t.Contains("age_verified", StringComparison.OrdinalIgnoreCase));

        return new VrchatCurrentUserDto(
            Id:            Str(root, "id")            ?? string.Empty,
            DisplayName:   Str(root, "displayName")   ?? string.Empty,
            Bio:           Str(root, "bio"),
            ProfilePicUrl: Str(root, "userIcon")
                        ?? Str(root, "currentAvatarThumbnailImageUrl"),
            Tags:          tags,
            IsAgeVerified: is18Plus
        );
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<VrchatUserSummaryDto?> GetUserAsync(
        string userId, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(
            HttpMethod.Get, $"users/{userId}?apiKey={ApiKey}", ct: ct);

        if (!resp.IsSuccessStatusCode) return null;

        using var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var tags = ParseStringArray(root, "tags");
        var ageVerificationStatus = Str(root, "ageVerificationStatus");
        var ageVerified = root.TryGetProperty("ageVerified", out var av) && av.GetBoolean();
        bool is18Plus = (ageVerificationStatus == "18+" && ageVerified)
                     || tags.Any(t => t.Contains("age_verified",        StringComparison.OrdinalIgnoreCase)
                                   || t.Contains("system_age_verified", StringComparison.OrdinalIgnoreCase));

        return new VrchatUserSummaryDto(
            UserId:       Str(root, "id")          ?? userId,
            DisplayName:  Str(root, "displayName")  ?? string.Empty,
            IsAgeVerified: is18Plus);
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    public async Task<VrchatGroupDto?> GetGroupAsync(string groupId, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(HttpMethod.Get, $"groups/{groupId}?apiKey={ApiKey}", ct: ct);
        _log.LogDebug("GetGroup {id}: {status}", groupId, resp.StatusCode);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(body);
        return ParseGroup(doc.RootElement, groupId);
    }

    public async Task<IReadOnlyList<VrchatGroupDto>> GetMyManageableGroupsAsync(CancellationToken ct = default)
    {
        var session = _store.GetSession();
        if (!session.IsLoggedIn || session.UserId is null)
            return Array.Empty<VrchatGroupDto>();

        var userId  = session.UserId;
        var result  = new List<VrchatGroupDto>();
        var seen    = new HashSet<string>(StringComparer.Ordinal);
        int offset  = 0;
        const int limit = 100;

        while (true)
        {
            var (resp, body) = await SendAsync(
                HttpMethod.Get,
                $"users/{userId}/groups?apiKey={ApiKey}&n={limit}&offset={offset}",
                ct: ct);

            if (!resp.IsSuccessStatusCode) break;

            using var doc    = JsonDocument.Parse(body);
            var arr    = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array) break;

            int count = 0;
            foreach (var item in arr.EnumerateArray())
            {
                count++;
                var gid     = Str(item, "groupId"); // actual grp_* id
                var ownerId = Str(item, "ownerId");

                if (string.IsNullOrEmpty(gid) || !seen.Add(gid)) continue;
                if (ownerId != userId) continue;

                result.Add(ParseGroup(item, gid));
            }

            if (count < limit) break;
            offset += limit;
        }

        return result;
    }

    // ── Members ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<VrchatGroupMemberDto>> GetGroupMembersAsync(
        string groupId,
        IProgress<(int fetched, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var members = new List<VrchatGroupMemberDto>();
        int offset  = 0;
        const int limit = 100;
        int failures = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (resp, body) = await SendPagedAsync(
                $"groups/{groupId}/members?apiKey={ApiKey}&n={limit}&offset={offset}", ct);

            if (resp.StatusCode is HttpStatusCode.InternalServerError
                               or HttpStatusCode.BadGateway
                               or HttpStatusCode.ServiceUnavailable)
            {
                failures++;
                _log.LogWarning("GetGroupMembers server error {code} at offset {o} (attempt {n}/3)",
                    resp.StatusCode, offset, failures);
                if (failures >= 3) break;
                await Task.Delay(5_000, ct).ConfigureAwait(false);
                continue;
            }

            if (!resp.IsSuccessStatusCode) break;
            failures = 0;

            using var doc  = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) break;

            int batch = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                batch++;
                members.Add(new VrchatGroupMemberDto(
                    UserId:         Str(item, "userId")       ?? string.Empty,
                    DisplayName:    NestedStr(item, "user", "displayName") ?? "Unknown",
                    JoinedAt:       Str(item, "joinedAt"),
                    RoleIds:        ParseStringArray(item, "roleIds"),
                    IsRepresenting: Bool(item, "isRepresenting")));
            }

            progress?.Report((members.Count, -1));

            if (batch < limit) break;
            offset += limit;
        }

        return members;
    }

    public async Task<VrchatGroupMemberDto?> GetGroupMemberAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(
            HttpMethod.Get,
            $"groups/{groupId}/members/{userId}?apiKey={ApiKey}", ct: ct);

        if (!resp.IsSuccessStatusCode) return null;

        using var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new VrchatGroupMemberDto(
            UserId:         Str(root, "userId")     ?? userId,
            DisplayName:    NestedStr(root, "user", "displayName") ?? "Unknown",
            JoinedAt:       Str(root, "joinedAt"),
            RoleIds:        ParseStringArray(root, "roleIds"),
            IsRepresenting: Bool(root, "isRepresenting"));
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<VrchatGroupRoleDto>> GetGroupRolesAsync(
        string groupId, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(
            HttpMethod.Get, $"groups/{groupId}/roles?apiKey={ApiKey}", ct: ct);

        if (!resp.IsSuccessStatusCode) return Array.Empty<VrchatGroupRoleDto>();

        using var doc  = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<VrchatGroupRoleDto>();

        var roles = new List<VrchatGroupRoleDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            roles.Add(new VrchatGroupRoleDto(
                RoleId:      Str(item, "id")          ?? string.Empty,
                Name:        Str(item, "name")         ?? "Unknown",
                Description: Str(item, "description"),
                Permissions: ParseStringArray(item, "permissions")));
        }
        return roles;
    }

    public async Task<bool> RemoveRoleFromGroupMemberAsync(
        string groupId, string userId, string roleId, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Delete,
            $"groups/{groupId}/members/{userId}/roles/{roleId}?apiKey={ApiKey}", ct: ct);

        _log.LogDebug("RemoveRole {r} from {u} in {g}: {s}", roleId, userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> AssignRoleToGroupMemberAsync(
        string groupId, string userId, string roleId, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Put,
            $"groups/{groupId}/members/{userId}/roles/{roleId}?apiKey={ApiKey}", ct: ct);

        _log.LogDebug("AssignRole {r} to {u} in {g}: {s}", roleId, userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    // ── Bans ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<VrchatBanDto>> GetGroupBansAsync(
        string groupId,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var bans   = new List<VrchatBanDto>();
        int offset = 0;
        const int limit = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (resp, body) = await SendPagedAsync(
                $"groups/{groupId}/bans?apiKey={ApiKey}&n={limit}&offset={offset}", ct);

            if (!resp.IsSuccessStatusCode) break;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) break;

            int batch = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                batch++;
                bans.Add(new VrchatBanDto(
                    UserId:      Str(item, "userId")                   ?? string.Empty,
                    DisplayName: NestedStr(item, "user", "displayName") ?? "Unknown",
                    Reason:      Str(item, "reason"),
                    BannedAt:    Str(item, "createdAt"),
                    ExpiresAt:   Str(item, "expiresAt")));
            }

            progress?.Report(bans.Count);

            if (batch < limit) break;
            offset += limit;
        }

        return bans;
    }

    public async Task<bool> BanGroupMemberAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Post,
            $"groups/{groupId}/bans?apiKey={ApiKey}",
            payload: new { userId }, ct: ct);

        _log.LogDebug("Ban {u} in {g}: {s}", userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> UnbanGroupMemberAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Delete,
            $"groups/{groupId}/bans/{userId}?apiKey={ApiKey}", ct: ct);

        _log.LogDebug("Unban {u} in {g}: {s}", userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> KickGroupMemberAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Delete,
            $"groups/{groupId}/members/{userId}?apiKey={ApiKey}", ct: ct);

        _log.LogDebug("Kick {u} from {g}: {s}", userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    // ── Audit logs ────────────────────────────────────────────────────────────

    public async Task<VrchatAuditLogPageDto> GetAuditLogsAsync(
        string groupId, int count = 100, int offset = 0, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(
            HttpMethod.Get,
            $"groups/{groupId}/auditLogs?apiKey={ApiKey}&n={count}&offset={offset}",
            ct: ct);

        if (!resp.IsSuccessStatusCode)
            return new VrchatAuditLogPageDto(Array.Empty<VrchatAuditLogEntryDto>(), 0);

        using var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        int total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;

        var entries = new List<VrchatAuditLogEntryDto>();
        if (root.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                entries.Add(new VrchatAuditLogEntryDto(
                    EventType:        Str(item, "eventType"),
                    ActorId:          Str(item, "actorId"),
                    ActorDisplayName: Str(item, "actorDisplayName"),
                    TargetId:         Str(item, "targetId"),
                    TargetDisplayName:Str(item, "targetDisplayName"),
                    CreatedAt:        Str(item, "createdAt"),
                    Description:      Str(item, "description")));
            }
        }

        return new VrchatAuditLogPageDto(entries, total);
    }

    // ── Join requests ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<VrchatJoinRequestDto>> GetJoinRequestsAsync(
        string groupId, CancellationToken ct = default)
    {
        var (resp, body) = await SendAsync(
            HttpMethod.Get, $"groups/{groupId}/requests?apiKey={ApiKey}", ct: ct);

        if (!resp.IsSuccessStatusCode) return Array.Empty<VrchatJoinRequestDto>();

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<VrchatJoinRequestDto>();

        var list = new List<VrchatJoinRequestDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("user", out var user)) continue;

            var tags         = ParseStringArray(user, "tags");
            bool ageVerified = tags.Any(t =>
                t.Contains("system_age_verified", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("age_verified",        StringComparison.OrdinalIgnoreCase));

            list.Add(new VrchatJoinRequestDto(
                UserId:       Str(user, "id")             ?? string.Empty,
                DisplayName:  Str(user, "displayName")    ?? "Unknown",
                ProfilePicUrl:Str(user, "currentAvatarImageUrl")
                           ?? Str(user, "profilePicOverride"),
                CreatedAt:    Str(item, "createdAt"),
                IsAgeVerified:ageVerified));
        }
        return list;
    }

    public async Task<bool> RespondToJoinRequestAsync(
        string groupId, string userId, string action, CancellationToken ct = default)
    {
        var (resp, _) = await SendAsync(
            HttpMethod.Put,
            $"groups/{groupId}/requests/{userId}?apiKey={ApiKey}",
            payload: new { action }, ct: ct);

        _log.LogDebug("JoinRequest {action} for {u} in {g}: {s}", action, userId, groupId, resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    // ── Invites ───────────────────────────────────────────────────────────────

    public async Task<bool> SendGroupInviteAsync(
        string groupId, string userId, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var (resp, _) = await SendAsync(
                HttpMethod.Post,
                $"groups/{groupId}/invites?apiKey={ApiKey}",
                payload: new { userId }, paged: false, ct);

            if (resp.IsSuccessStatusCode) return true;
            if (resp.StatusCode != HttpStatusCode.TooManyRequests) return false;

            // 429 inside invite loop — back off and retry
            var delay = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            _log.LogWarning("SendGroupInvite rate limited. Waiting {s}s (attempt {n}/{max})",
                delay.TotalSeconds, attempt, MaxRetries);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        return false;
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a request with throttling.  On 429, throws <see cref="VrchatRateLimitException"/>
    /// unless <paramref name="paged"/> is true, in which case it retries up to
    /// <see cref="MaxRetries"/> times.
    /// </summary>
    private async Task<(HttpResponseMessage Resp, string Body)> SendAsync(
        HttpMethod method,
        string     relUrl,
        object?    payload = null,
        bool       paged   = false,
        CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            await _store.ThrottleAsync(ct).ConfigureAwait(false);

            // HttpRequestMessage cannot be reused after sending — create fresh each attempt.
            using var req = BuildRequest(method, relUrl, payload);
            var resp = await _store.Http.SendAsync(req, ct).ConfigureAwait(false);

            _log.LogDebug("→ {method} {url} {status}", method, SanitizeUrl(relUrl), resp.StatusCode);

            if (resp.StatusCode != HttpStatusCode.TooManyRequests)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return (resp, body);
            }

            var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            resp.Dispose();

            if (!paged)
                throw new VrchatRateLimitException(retryAfter);

            _log.LogWarning("VRChat rate limit on {url}. Waiting {s}s (attempt {n}/{max})",
                SanitizeUrl(relUrl), retryAfter.TotalSeconds, attempt + 1, MaxRetries);
            await Task.Delay(retryAfter, ct).ConfigureAwait(false);
        }

        throw new VrchatRateLimitException();
    }

    /// <summary>Convenience overload for paged calls with automatic 429 retry.</summary>
    private Task<(HttpResponseMessage Resp, string Body)> SendPagedAsync(
        string relUrl, CancellationToken ct)
        => SendAsync(HttpMethod.Get, relUrl, payload: null, paged: true, ct: ct);

    private static HttpRequestMessage BuildRequest(HttpMethod method, string relUrl, object? payload)
    {
        var req = new HttpRequestMessage(method, relUrl);
        if (payload is not null)
        {
            req.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
        }
        return req;
    }

    // ── JSON parsing helpers ──────────────────────────────────────────────────

    private VrchatLoginResultDto ParseLoginResponse(string body)
    {
        using var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Check for 2FA requirement
        if (root.TryGetProperty("requiresTwoFactorAuth", out var twoFa)
            && twoFa.ValueKind == JsonValueKind.Array
            && twoFa.GetArrayLength() > 0)
        {
            var types = new List<string>();
            foreach (var t in twoFa.EnumerateArray())
                types.Add(t.GetString() ?? string.Empty);

            return new VrchatLoginResultDto(
                Success: false, RequiresTwoFactor: true, TwoFactorTypes: types,
                UserId: null, DisplayName: null, ProfilePicUrl: null, ErrorMessage: null);
        }

        // Successful login
        var userId      = Str(root, "id")          ?? string.Empty;
        var displayName = Str(root, "displayName")  ?? string.Empty;
        var pic         = Str(root, "userIcon")
                       ?? Str(root, "currentAvatarThumbnailImageUrl");

        _store.SetSession(userId, displayName, pic);

        _log.LogInformation("VRChat session established: {display} ({id})", displayName, userId);

        return new VrchatLoginResultDto(
            Success: true, RequiresTwoFactor: false,
            TwoFactorTypes: Array.Empty<string>(),
            UserId: userId, DisplayName: displayName, ProfilePicUrl: pic,
            ErrorMessage: null);
    }

    private static VrchatGroupDto ParseGroup(JsonElement el, string fallbackId)
        => new(
            Id:          Str(el, "groupId") ?? Str(el, "id") ?? fallbackId,
            Name:        Str(el, "name")        ?? string.Empty,
            ShortCode:   Str(el, "shortCode"),
            Description: Str(el, "description"),
            MemberCount: Int(el, "memberCount"),
            OnlineCount: Int(el, "onlineMemberCount"),
            IconUrl:     Str(el, "iconUrl"),
            BannerUrl:   Str(el, "bannerUrl"),
            Privacy:     Str(el, "privacy"),
            OwnerId:     Str(el, "ownerId"),
            CreatedAt:   ParseDateTime(el, "createdAt"));

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object)
                    return Str(err, "message");
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString();
            }
            return Str(doc.RootElement, "message");
        }
        catch { return null; }
    }

    // ── Micro-helpers (static) ────────────────────────────────────────────────

    private static string? Str(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() : null;

    private static string? NestedStr(JsonElement el, string parent, string key)
        => el.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object
           ? Str(p, key) : null;

    private static int Int(JsonElement el, string key)
        => el.TryGetProperty(key, out var v)
           && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTime? ParseDateTime(JsonElement el, string key)
    {
        var s = Str(el, key);
        return s is not null && DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }

    private static VrchatLoginResultDto Fail(string message)
        => new(false, false, Array.Empty<string>(), null, null, null, message);

    /// <summary>Replace the embedded API key with *** in log strings.</summary>
    private static string SanitizeUrl(string url)
        => url.Replace(ApiKey, "***", StringComparison.Ordinal);
}
