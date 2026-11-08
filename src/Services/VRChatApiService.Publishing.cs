using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

// Routes/fields checked against vrchatapi/specification main, 2026-11-07 (UNOFFICIAL community spec).
// Never use legacy UploadImageAsync's guessed fallback endpoints or unguarded SendJsonAsync here.
public partial class VRChatApiService : IApprovalPublicationTransport
{
    private long _publicationSession;
    public PublicationContext Context(string group) => new(CurrentUserId, group, IsLoggedIn, Interlocked.Read(ref _publicationSession));
    public async Task<bool> CanPublishAsync(PublicationPreview preview)
    {
        var d = preview.Draft;
        var session = Context(d.GroupId);
        if (!IsLoggedIn || CurrentUserId != d.AccountId) return false;
        // Read effective permissions of this exact authenticated membership; news and calendar are independent.
        var group = await PublishingGetAsync("groups/" + d.GroupId);
        if (group?["id"]?.Value<string>() != d.GroupId || Context(d.GroupId) != session) return false;
        if (group["ownerId"]?.Value<string>() == d.AccountId) return true;
        var member = await PublishingGetAsync($"groups/{d.GroupId}/members/{d.AccountId}");
        if (member == null || member["userId"]?.Value<string>() != d.AccountId || Context(d.GroupId) != session) return false;
        var permissions = member["permissions"] as JArray;
        var needed = d.Action == "event" ? "group-calendar-manage" : "group-announcement-manage";
        if (permissions != null) return permissions.Any(p => p.Value<string>() is "*" || p.Value<string>() == needed);
        var roles = await PublishingReadAsync($"groups/{d.GroupId}/roles");
        if (roles is not JArray roleList || member["roleIds"] is not JArray ids || Context(d.GroupId) != session) return false;
        return roleList.Any(r => ids.Any(id => id.Value<string>() == r["id"]?.Value<string>()) && r["permissions"] is JArray perms && perms.Any(p => p.Value<string>() is "*" || p.Value<string>() == needed));
    }
    private async Task<JToken?> PublishingReadAsync(string path)
    {
        await RateLimitAsync();
        using var response = await _httpClient.GetAsync(path);
        if (!response.IsSuccessStatusCode) return null;
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length > 2 * 1024 * 1024) return null;
        return JToken.Parse(text);
    }
    private async Task<JObject?> PublishingGetAsync(string path) => await PublishingReadAsync(path) as JObject;
    private static PublicationEffect Rejection(HttpStatusCode status) => new(status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests ? "Rejected" : "Unknown");
    public async Task<PublicationEffect> UploadApprovedAsync(PublicationPreview preview, Func<Task<bool>> guard)
    {
        var bytes = preview.ImageBytes;
        var session = Context(preview.Draft.GroupId);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "approved-artwork.png"); form.Add(new StringContent("gallery"), "tag");
        await RateLimitAsync();
        if (!await guard()) return new("NotSent");
        using var response = await _httpClient.PostAsync("file/image", form);
        if (!response.IsSuccessStatusCode) return Rejection(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 2 * 1024 * 1024) return new("Unknown");
        var created = JObject.Parse(body); var id = created["id"]?.Value<string>();
        if (!ApprovalPublisher.Id(id, "file")) return new("Unknown");
        try
        {
            if (!IsLoggedIn || CurrentUserId != preview.Draft.AccountId) return new("SentUnverified", id);
            var read = await PublishingGetAsync("file/" + id);
            var md5 = Convert.ToBase64String(MD5.HashData(bytes)); // API FileVersion digest, not a security decision.
            var versions = read?["versions"] as JArray;
            var verified = read != null && read["id"]?.Value<string>() == id && read["ownerId"]?.Value<string>() == preview.Draft.AccountId &&
                versions != null && versions.Any(v => v["file"]?["md5"]?.Value<string>() == md5 && v["file"]?["status"]?.Value<string>() == "complete");
            return new(verified && Context(preview.Draft.GroupId) == session ? "Confirmed" : "SentUnverified", id);
        }
        catch { return new("SentUnverified", id); }
    }
    public async Task<PublicationEffect> CreateApprovedAsync(PublicationPreview preview, string? imageId, Func<Task<bool>> guard)
    {
        var draft = preview.Draft; var payload = JObject.Parse(preview.Payload);
        var session = Context(draft.GroupId);
        if (imageId != null) { if (!ApprovalPublisher.Id(imageId,"file")) return new("NotSent"); payload["imageId"] = imageId; }
        else payload["imageId"] = JValue.CreateNull();
        var route = draft.Action == "event" ? $"calendar/{draft.GroupId}/event" : $"groups/{draft.GroupId}/posts" + (draft.Action == "news-update" ? "/" + draft.ExistingPostId : "");
        using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
        await RateLimitAsync();
        if (!await guard()) return new("NotSent");
        using var request = new HttpRequestMessage(draft.Action == "news-update" ? HttpMethod.Put : HttpMethod.Post, route) { Content = content };
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return Rejection(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 2 * 1024 * 1024) return new("Unknown");
        var created = JObject.Parse(body); var id = created["id"]?.Value<string>();
        if (!ApprovalPublisher.Id(id, draft.Action == "event" ? "cal" : "not") || (draft.Action == "news-update" && id != draft.ExistingPostId)) return new("Unknown");
        try
        {
            if (!IsLoggedIn || CurrentUserId != draft.AccountId) return new("SentUnverified", id);
            JObject? target = null;
            if (draft.Action == "event") target = await PublishingGetAsync($"calendar/{draft.GroupId}/{id}");
            else
            {
                // The community spec has no single-post GET. Read paginated group posts and match exact returned ID.
                for (int offset = 0; offset < 1000; offset += 100)
                {
                    var page = await PublishingGetAsync($"groups/{draft.GroupId}/posts?n=100&offset={offset}&publicOnly=false");
                    if (page?["posts"] is not JArray posts) break;
                    target = posts.OfType<JObject>().SingleOrDefault(p => p["id"]?.Value<string>() == id);
                    if (target != null || posts.Count < 100) break;
                }
            }
            bool matches = target != null && target["id"]?.Value<string>() == id && target[draft.Action == "event" ? "ownerId" : "groupId"]?.Value<string>() == draft.GroupId;
            foreach (var property in payload.Properties())
            {
                if (property.Name is "sendNotification" or "sendCreationNotification") continue; // write-only flags: show requested, never claim delivery.
                var actual = target?[property.Name];
                if (property.Name == "imageId" && property.Value.Type == JTokenType.Null && (actual == null || actual.Type == JTokenType.Null)) continue;
                if (!JToken.DeepEquals(actual, property.Value)) matches = false;
            }
            return new(matches && Context(draft.GroupId) == session ? "Confirmed" : "SentUnverified", id);
        }
        catch { return new("SentUnverified", id); }
    }
}
