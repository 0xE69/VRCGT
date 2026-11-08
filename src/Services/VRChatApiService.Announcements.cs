using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public partial class VRChatApiService
{
    // Unofficial vrchatapi community schema: group listings contain only location/memberCount/world.
    // They cannot establish active/closed/access state. Read full instance details, never legacy lossy DTOs.
    public async Task<AnnouncementSnapshot> DiscoverAnnouncementInstancesAsync(string group, CancellationToken ct)
    {
        var result = new List<AnnounceableInstance>();
        if (!Regex.IsMatch(group, @"\Agrp_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\z")) return new(false, result);
        var account = CurrentUserId;
        try
        {
            ct.ThrowIfCancellationRequested();
            await RateLimitAsync(); ct.ThrowIfCancellationRequested();
            using var response = await _httpClient.GetAsync($"groups/{group}/instances", ct);
            if (!response.IsSuccessStatusCode) return new(false, result);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length > 1024 * 1024) return new(false, result);
            var rows = JArray.Parse(body);
            if (rows.Count > 50) return new(false, result); // bounded work, never pretend a truncated list is authoritative
            foreach (var row in rows)
            {
                var location = row["location"]?.Value<string>();
                if (location == null || location.Length > 2000) return new(false, result);
                var colon = location.IndexOf(':');
                if (colon < 0) return new(false, result);
                var world = location[..colon]; var instance = location[(colon + 1)..];
                if (!Regex.IsMatch(world, @"\Awrld_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\z")) return new(false, result);
                // Privacy gate before even fetching details; restricted locations never reach Discord.
                if (!instance.Contains("~group(" + group + ")", StringComparison.Ordinal) || !instance.Contains("~groupAccessType(public)", StringComparison.Ordinal)) continue;
                await RateLimitAsync(); ct.ThrowIfCancellationRequested();
                if (!IsLoggedIn || CurrentUserId != account) return new(false, result);
                using var detail = await _httpClient.GetAsync($"instances/{Uri.EscapeDataString(world)}:{Uri.EscapeDataString(instance)}", ct);
                if (!detail.IsSuccessStatusCode) return new(false, result);
                var text = await detail.Content.ReadAsStringAsync(ct);
                if (text.Length > 1024 * 1024) return new(false, result);
                var item = JObject.Parse(text);
                var candidate = ParseAnnouncementInstance(item, group, world, instance, DateTime.UtcNow);
                if (candidate != null) result.Add(candidate);
            }
            return new(CurrentUserId == account && IsLoggedIn, result);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(false, Array.Empty<AnnounceableInstance>()); }
    }
    public static AnnounceableInstance? ParseAnnouncementInstance(JObject item, string group, string world, string instance, DateTime observed)
    {
        // Missing optional fields are unknown, not default active/open. Future close times are conservatively withheld too.
        if (item["location"]?.Value<string>() != world + ":" + instance || item["worldId"]?.Value<string>() != world ||
            item["ownerId"]?.Value<string>() != group || item["type"]?.Value<string>() != "group" ||
            item["groupAccessType"]?.Value<string>() != "public" || item["active"]?.Type != JTokenType.Boolean ||
            item["active"]?.Value<bool>() != true || item.Property("closedAt") == null || item["closedAt"]?.Type != JTokenType.Null ||
            item["roleRestricted"]?.Type != JTokenType.Boolean || item["roleRestricted"]?.Value<bool>() != false ||
            (item["accessType"] != null && item["accessType"]?.Value<string>() != "groupPublic")) return null;
        return new(group, world, instance, "groupPublic", true, false, observed);
    }
}
