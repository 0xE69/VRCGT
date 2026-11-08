using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCGroupTools.Services;

public sealed record PublicationDraft(string Key, string AccountId, string GroupId, string Action, string Title,
    string Body, string Audience, bool Notify, string? ImagePath = null, string? EventJson = null, string? ExistingPostId = null, string? TimeZoneId = null);
public sealed record PublicationContext(string? AccountId, string GroupId, bool LoggedIn, long Session);
public sealed record PublicationEffect(string State, string? TargetId = null);
public sealed record PublicationHistoryItem(string EffectKey, string Digest, string State, string? TargetId);
public sealed record PublicationReport(PublicationEffect Upload, PublicationEffect Publication)
{
    public override string ToString() => $"Artwork: {Upload.State} {Upload.TargetId}; publication: {Publication.State} {Publication.TargetId}. Unknown/unverified/in-flight effects are blocked from retry; check exact targets in VRChat. No automatic retry.";
}
public sealed class PublicationPreview
{
    public PublicationDraft Draft { get; }
    public string Digest { get; }
    public string Payload { get; }
    public byte[] ImageBytes => (byte[])_image.Clone();
    private readonly byte[] _image;
    internal PublicationPreview(PublicationDraft draft, string payload, byte[] image)
    {
        Draft = draft; Payload = payload; _image = image;
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(draft) + payload + Convert.ToHexString(SHA256.HashData(image)))));
    }
    public string Summary => $"Account: {Draft.AccountId}\nGroup: {Draft.GroupId}\nAction: {Draft.Action} {(Draft.ExistingPostId == null ? "(create; not an update)" : "EXACT target: " + Draft.ExistingPostId + "; replaces content/artwork with this preview")}\nAudience: {Draft.Audience}; roles: unrestricted\nNotify group members: {Draft.Notify}\nTime: now on approval; event dates below are UTC. Event timezone: {Draft.TimeZoneId ?? "Not applicable"}\nArtwork: {(_image.Length == 0 ? "None" : "Upload this PNG to acting account gallery, then attach it. This does not replace the group header.")}\nTitle: {Draft.Title}\n\n{Draft.Body}\n\nExact JSON payload (imageId is filled ONLY from the approved upload):\n{Payload}\n\nApproval SHA256: {Digest}\nExpires in five minutes. This approval is for ONE publication only; Discord is not included.";
}
public interface IApprovalPublicationTransport
{
    PublicationContext Context(string group);
    Task<bool> CanPublishAsync(PublicationPreview preview);
    Task<PublicationEffect> UploadApprovedAsync(PublicationPreview preview, Func<Task<bool>> guard);
    Task<PublicationEffect> CreateApprovedAsync(PublicationPreview preview, string? imageId, Func<Task<bool>> guard);
}

/// <summary>Only called by an explicit UI command. No timer, login or storage access on construction.</summary>
public sealed class ApprovalPublisher
{
    private readonly IApprovalPublicationTransport _transport;
    private readonly string _database;
    private readonly Func<DateTime> _now;
    public ApprovalPublisher(IApprovalPublicationTransport transport, string? database = null, Func<DateTime>? now = null)
    {
        _transport = transport;
        _database = database ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCGroupTools", "publication-effects.db");
        _now = now ?? (() => DateTime.UtcNow);
    }
    public static PublicationPreview Preview(PublicationDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Key) || draft.Key.Length > 200 || !Id(draft.AccountId, "usr") || !Id(draft.GroupId, "grp")) throw new ArgumentException("Exact draft, account and group identities are required.");
        if (draft.Action is not ("news" or "news-update" or "event") || draft.Audience is not ("group" or "public")) throw new ArgumentException("Choose news/event and an explicit audience.");
        if (draft.Action == "news-update" ? !Id(draft.ExistingPostId, "not") : draft.ExistingPostId != null) throw new ArgumentException("An update needs the exact existing post ID; creation must not have one.");
        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Length > 64 || string.IsNullOrWhiteSpace(draft.Body) || draft.Body.Length > 10000) throw new ArgumentException("Title (1–64) and body (1–10000) are required.");
        JObject payload;
        if (draft.Action == "event")
        {
            if (string.IsNullOrWhiteSpace(draft.TimeZoneId)) throw new ArgumentException("An explicit event timezone is required.");
            try { TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId); }
            catch (TimeZoneNotFoundException) { throw new ArgumentException("Invalid event timezone."); }
            catch (InvalidTimeZoneException) { throw new ArgumentException("Invalid event timezone."); }
            payload = JObject.Parse(draft.EventJson ?? throw new ArgumentException("An event needs explicit dates and timezone."));
            var allowed = new[] { "title", "description", "startsAt", "endsAt", "category", "accessType", "sendCreationNotification", "languages", "platforms", "tags", "isDraft", "roleIds" };
            if (payload.Properties().Any(p => !allowed.Contains(p.Name)) || payload["title"]?.Value<string>() != draft.Title || payload["description"]?.Value<string>() != draft.Body || payload["accessType"]?.Value<string>() != draft.Audience || payload["sendCreationNotification"]?.Value<bool>() != draft.Notify || payload["isDraft"]?.Value<bool>() != false) throw new ArgumentException("Event payload does not match preview.");
            var start = payload["startsAt"]?.Value<DateTime>() ?? default;
            var end = payload["endsAt"]?.Value<DateTime>() ?? default;
            if (start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || end <= start) throw new ArgumentException("Event needs unambiguous UTC start/end.");
            if (!new[] { "arts", "avatars", "dance", "education", "exploration", "film_media", "gaming", "hangout", "music", "other", "performance", "roleplaying", "wellness" }.Contains(payload["category"]?.Value<string>())) throw new ArgumentException("Invalid event category.");
            var platforms = payload["platforms"] as JArray;
            if (platforms == null || platforms.Count == 0 || platforms.Any(x => !new[] { "standalonewindows", "android", "ios" }.Contains(x.Value<string>()))) throw new ArgumentException("Select valid event platforms.");
            if (payload["languages"] is not JArray languages || languages.Count > 3 || languages.Any(x => !Regex.IsMatch(x.Value<string>() ?? "", "^[a-z]{3}$"))) throw new ArgumentException("Select at most three language codes.");
            if (payload["tags"] is not JArray tags || tags.Count > 5 || tags.Any(x => string.IsNullOrWhiteSpace(x.Value<string>()) || x.Value<string>()!.Length > 100)) throw new ArgumentException("Select at most five tags (1–100 characters).");
            if (payload["roleIds"] is not JArray roles || roles.Count != 0) throw new ArgumentException("Role-restricted publication is not supported in this preview.");
        }
        else payload = new JObject { ["title"] = draft.Title, ["text"] = draft.Body, ["visibility"] = draft.Audience, ["sendNotification"] = draft.Notify, ["roleIds"] = new JArray() };
        byte[] image = Array.Empty<byte>();
        if (!string.IsNullOrEmpty(draft.ImagePath))
        {
            var file = new FileInfo(draft.ImagePath);
            if (!file.Exists || file.Length > 10 * 1024 * 1024 || !file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Artwork must be a local PNG, at most 10 MiB.");
            image = File.ReadAllBytes(file.FullName);
            using var stream = new MemoryStream(image);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth > 4096 || decoder.Frames[0].PixelHeight > 4096) throw new ArgumentException("Artwork must be one PNG frame, at most 4096×4096.");
        }
        payload["imageId"] = JValue.CreateNull();
        return new(draft, payload.ToString(Formatting.None), image);
    }
    public static bool Id(string? value, string prefix) => Regex.IsMatch(value ?? "", @"\A" + prefix + @"_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\z");
    public async Task<PublicationReport> PublishAsync(Func<PublicationDraft> current, Func<PublicationPreview, Task<bool>> confirm)
    {
        var preview = Preview(current());
        var context = _transport.Context(preview.Draft.GroupId);
        var expires = _now().AddMinutes(5);
        bool Same()
        {
            try { return _now() < expires && _transport.Context(preview.Draft.GroupId) == context && context.LoggedIn && context.AccountId == preview.Draft.AccountId && context.GroupId == preview.Draft.GroupId && Preview(current()).Digest == preview.Digest; }
            catch { return false; }
        }
        async Task<bool> Guard() => Same() && await _transport.CanPublishAsync(preview) && Same() && (preview.Draft.Action != "event" || JObject.Parse(preview.Payload)["startsAt"]!.Value<DateTime>() > _now());
        var notSent = new PublicationEffect("NotSent");
        if (!await Guard() || !await confirm(preview) || !await Guard()) return new(notSent, notSent);
        var upload = new PublicationEffect("NotRequired");
        var scope = preview.Draft.GroupId + ":" + preview.Draft.Key + ":" + preview.Draft.Action;
        if (preview.ImageBytes.Length > 0)
        {
            upload = await Effect(scope + ":upload", preview.Digest, () => _transport.UploadApprovedAsync(preview, Guard));
            if (upload.State != "Confirmed") return new(upload, notSent);
        }
        if (!await Guard()) return new(upload, notSent);
        var publication = await Effect(scope + ":create", preview.Digest, () => _transport.CreateApprovedAsync(preview, upload.TargetId, Guard), preview.Draft.Action == "news-update");
        return new(upload, publication);
    }
    public async Task<IReadOnlyList<PublicationHistoryItem>> ReadHistoryAsync(string group)
    {
        if (!Id(group, "grp")) throw new ArgumentException("Select an exact group.");
        if (!File.Exists(_database)) return Array.Empty<PublicationHistoryItem>();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _database, Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 30 }.ToString());
        await connection.OpenAsync();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT EffectKey,Digest,State,TargetId FROM PublicationEffects WHERE substr(EffectKey,1,length($group))=$group ORDER BY rowid DESC";
        query.Parameters.AddWithValue("$group", group + ":");
        using var reader = await query.ExecuteReaderAsync();
        var rows = new List<PublicationHistoryItem>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }
    private async Task<PublicationEffect> Effect(string key, string digest, Func<Task<PublicationEffect>> send, bool allowVerifiedUpdate = false)
    {
        // Unique INSERT is the durable cross-process claim. Crash/unknown remains blocking; no timed lease retries.
        Directory.CreateDirectory(Path.GetDirectoryName(_database)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _database, DefaultTimeout = 30 }.ToString());
        await connection.OpenAsync();
        using var query = connection.CreateCommand();
        query.CommandText = "CREATE TABLE IF NOT EXISTS PublicationEffects (EffectKey TEXT PRIMARY KEY, Digest TEXT NOT NULL, State TEXT NOT NULL, TargetId TEXT NULL)";
        await query.ExecuteNonQueryAsync();
        query.CommandText = allowVerifiedUpdate
            ? "INSERT INTO PublicationEffects VALUES ($key,$digest,'InFlight',NULL) ON CONFLICT(EffectKey) DO UPDATE SET Digest=$digest,State='InFlight',TargetId=NULL WHERE PublicationEffects.State='Confirmed' AND PublicationEffects.Digest<>$digest"
            : "INSERT OR IGNORE INTO PublicationEffects VALUES ($key,$digest,'InFlight',NULL)";
        query.Parameters.AddWithValue("$key", key); query.Parameters.AddWithValue("$digest", digest);
        var claimed = await query.ExecuteNonQueryAsync();
        if (claimed != 1)
        {
            query.CommandText = "SELECT Digest,State,TargetId FROM PublicationEffects WHERE EffectKey=$key";
            using var reader = await query.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new IOException("Missing effect claim.");
            return reader.GetString(0) == digest ? new(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)) : new("Blocked: draft changed after earlier attempt");
        }
        PublicationEffect result;
        try { result = await send(); } catch { result = new("Unknown"); }
        query.CommandText = "UPDATE PublicationEffects SET State=$state,TargetId=$target WHERE EffectKey=$key";
        query.Parameters.AddWithValue("$state", result.State); query.Parameters.AddWithValue("$target", (object?)result.TargetId ?? DBNull.Value);
        try { await query.ExecuteNonQueryAsync(); } catch { return new("Unknown: completion storage failed; blocking claim retained", result.TargetId); }
        return result;
    }
}
