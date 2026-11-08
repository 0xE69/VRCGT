using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public partial class VRChatApiService
{
    // Identity and optional filter metadata come from the same real response. Legacy GetUserAsync remains unchanged.
    public async Task<UserDetails?> GetExactUserIdentityAsync(string userId)
    {
        if (!ApprovalPublisher.Id(userId,"usr") || !IsLoggedIn) return null;
        var account=CurrentUserId; var generation=InvitationSession;
        try
        {
            await RateLimitAsync();
            if (!IsLoggedIn || CurrentUserId!=account || InvitationSession!=generation) return null;
            using var response=await _httpClient.GetAsync($"users/{userId}?apiKey={ApiKey}");
            if (!response.IsSuccessStatusCode) return null;
            var body=JToken.Parse(await response.Content.ReadAsStringAsync());
            if (body.Type!=JTokenType.Object || body["id"]?.Type!=JTokenType.String || body["id"]!.Value<string>()!=userId) return null;
            if (!IsLoggedIn || CurrentUserId!=account || InvitationSession!=generation) return null;
            return new UserDetails
            {
                UserId=body["id"]!.Value<string>()!, DisplayName=body["displayName"]?.Value<string>() ?? "Unknown",
                IsAgeVerified=body["ageVerified"]?.Value<bool>()==true && body["ageVerificationStatus"]?.Value<string>()=="18+",
                Tags=body["tags"]?.ToObject<List<string>>() ?? new(),
                ProfilePicUrl=body["userIcon"]?.Value<string>() ?? body["currentAvatarThumbnailImageUrl"]?.Value<string>()
            };
        }
        catch { return null; }
    }
    private readonly SemaphoreSlim _membershipReadGate = new(1,1);
    private DateTimeOffset _nextExactMembershipRead;
    // Unofficial vrchatapi/specification: GetGroupMemberResponse explicitly documents 200 null
    // for non-members. Errors/404 are NOT non-membership evidence.
    public async Task<GroupMembershipState> CheckGroupMembershipAsync(string groupId, string userId)
    {
        if (!ApprovalPublisher.Id(groupId,"grp") || !ApprovalPublisher.Id(userId,"usr") || !IsLoggedIn) return GroupMembershipState.Unknown;
        var account=CurrentUserId; var generation=InvitationSession;
        await _membershipReadGate.WaitAsync();
        try
        {
            var wait=_nextExactMembershipRead-DateTimeOffset.UtcNow;
            if (wait>TimeSpan.Zero) await Task.Delay(wait);
            await RateLimitAsync();
            if (!IsLoggedIn || CurrentUserId!=account || InvitationSession!=generation) return GroupMembershipState.Unknown;
            _nextExactMembershipRead=DateTimeOffset.UtcNow.AddSeconds(13);
            using var response=await _httpClient.GetAsync($"groups/{groupId}/members/{userId}?apiKey={ApiKey}");
            if (!response.IsSuccessStatusCode) return GroupMembershipState.ApiError;
            var body=JToken.Parse(await response.Content.ReadAsStringAsync());
            if (body.Type==JTokenType.Null) return GroupMembershipState.CachedNonMember;
            if (body.Type!=JTokenType.Object || body["userId"]?.Value<string>()!=userId) return GroupMembershipState.Unknown;
            var status=body["membershipStatus"]?.Value<string>();
            return status is "banned" or "blocked" ? GroupMembershipState.Blocked : GroupMembershipState.CachedMember;
        }
        catch { return GroupMembershipState.ApiError; }
        finally { _membershipReadGate.Release(); }
    }
}
