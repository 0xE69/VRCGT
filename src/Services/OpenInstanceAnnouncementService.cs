using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace VRCGroupTools.Services;

public record AnnouncementContext(string? AccountId, string? GroupId, bool LoggedIn, long Session = 0);
public record AnnouncementPreview(string AccountId, string GroupId, string WebhookId, string ChannelId, string GuildId, string Content, DateTime PreviewUtc)
{
    public string Summary => $"Account: {AccountId}\nGroup: {GroupId}\nAction: ONE Discord webhook message now\nDestination webhook: {WebhookId}\nChannel: {ChannelId}; server: {GuildId}\nAudience: configured Discord channel; public group-public instance only\nImage: none. Mentions/role/user/everyone notifications: disabled.\nPreview UTC: {PreviewUtc:O}\n\nExact message:\n{Content}\n\nApprove only this ONE message? Approval expires after five minutes. VRChat news/events are NOT included.";
}
public record AnnounceableInstance(string GroupId, string WorldId, string InstanceId, string Access, bool? Active, bool? Closed, DateTime ObservedUtc);
public record AnnouncementSnapshot(bool Complete, IReadOnlyList<AnnounceableInstance> Instances);
public record AnnouncementConfiguration(bool Enabled, bool HasWebhook);

// Separate from legacy audit webhooks: no global fallback, no secret in settings/exports, no constructor polling.
public sealed class OpenInstanceAnnouncementService : IDisposable
{
    private readonly string _path;
    private readonly Func<AnnouncementContext> _context;
    private readonly Func<string, CancellationToken, Task<AnnouncementSnapshot>> _discover;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _now;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private CancellationTokenSource? _run;
    private string? _activeGroup;
    private DateTime _nextPoll;
    public string Status { get; private set; } = "Paused. No announcements are sent until explicitly enabled and started for this group.";
    public bool IsRunning => _run != null;
    public IReadOnlyList<AnnouncementPreview> Ready { get; private set; } = Array.Empty<AnnouncementPreview>();

    public OpenInstanceAnnouncementService(IVRChatApiService api, ISettingsService settings)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCGroupTools", "instance-announcements.db"),
            () => new(api.CurrentUserId, settings.CurrentGroupId, api.IsLoggedIn, api is IApprovalPublicationTransport publishing ? publishing.Context(settings.CurrentGroupId ?? "").Session : 0),
            (g, c) => api is VRChatApiService concrete ? concrete.DiscoverAnnouncementInstancesAsync(g, c) : Task.FromResult(new AnnouncementSnapshot(false, Array.Empty<AnnounceableInstance>())),
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 1024 * 1024 }, () => DateTime.UtcNow) { }

    public OpenInstanceAnnouncementService(string path, Func<AnnouncementContext> context,
        Func<string, CancellationToken, Task<AnnouncementSnapshot>> discover, HttpClient http, Func<DateTime> now)
    {
        _path = path; _context = context; _discover = discover; _http = http; _now = now;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Configuration (GroupId TEXT PRIMARY KEY, Enabled INTEGER NOT NULL, Secret BLOB NOT NULL, Fingerprint TEXT NOT NULL, RetryUtc TEXT); CREATE TABLE IF NOT EXISTS Delivery (GroupId TEXT, WorldId TEXT, InstanceId TEXT, Destination TEXT, State TEXT NOT NULL, MessageId TEXT, PRIMARY KEY(GroupId,WorldId,InstanceId,Destination));";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 5 }.ToString()); db.Open(); return db; }
    public static bool ValidWebhook(string url) => Regex.IsMatch(url, @"\Ahttps://discord\.com/api/webhooks/[0-9]{17,20}/[A-Za-z0-9_-]{20,200}\z", RegexOptions.CultureInvariant);
    private static void ValidateGroup(string group) { if (!Regex.IsMatch(group, @"\Agrp_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\z")) throw new ArgumentException("Select an exact valid group ID."); }
    private static byte[] Entropy(string group) => Encoding.UTF8.GetBytes("VRCGT-open-instances:" + group);
    public void Configure(string group, string webhook, bool enabled)
    {
        ValidateGroup(group);
        if (!ValidWebhook(webhook)) throw new ArgumentException("Use an HTTPS discord.com/api/webhooks/{id}/{token} URL (no query, redirect or alternate host).");
        Stop();
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(webhook), Entropy(group), DataProtectionScope.CurrentUser);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(webhook)));
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO Configuration(GroupId,Enabled,Secret,Fingerprint) VALUES($g,$e,$s,$f) ON CONFLICT(GroupId) DO UPDATE SET Enabled=$e,Secret=$s,Fingerprint=$f";
        cmd.Parameters.AddWithValue("$g", group); cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0); cmd.Parameters.AddWithValue("$s", encrypted); cmd.Parameters.AddWithValue("$f", fingerprint); cmd.ExecuteNonQuery();
        Status = "Saved for this exact group using Windows user encryption. Paused; Start is separate.";
    }
    public AnnouncementConfiguration GetConfiguration(string group)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT Enabled FROM Configuration WHERE GroupId=$g"; cmd.Parameters.AddWithValue("$g", group);
        var value = cmd.ExecuteScalar(); return new(value != null && Convert.ToInt32(value) == 1, value != null);
    }
    private string? CurrentFingerprint(string group)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Fingerprint FROM Configuration WHERE GroupId=$g AND Enabled=1";
        cmd.Parameters.AddWithValue("$g", group); return cmd.ExecuteScalar() as string;
    }
    public void Disable(string group)
    {
        Stop(); using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE Configuration SET Enabled=0 WHERE GroupId=$g"; cmd.Parameters.AddWithValue("$g", group); cmd.ExecuteNonQuery();
        Status = "Disabled for this group. Existing deduplication history retained.";
    }
    public void Start(string group, bool background = true)
    {
        ValidateGroup(group); Stop();
        var ctx = _context();
        if (!ctx.LoggedIn || string.IsNullOrEmpty(ctx.AccountId) || ctx.GroupId != group || !GetConfiguration(group).Enabled) { Status = "Start blocked: sign in, select this group, and save explicit opt-in first."; return; }
        _activeGroup = group; _run = new(); // Preserve pacing across pause/start and group changes.
        Status = "Local previews running for selected group; checks at most every 2 minutes. NEVER sends unattended. Review and approve each message.";
        if (background) _ = LoopAsync(group, _run.Token);
    }
    public void Stop() { Ready = Array.Empty<AnnouncementPreview>(); _activeGroup = null; var run = _run; _run = null; run?.Cancel(); run?.Dispose(); Status = "Paused. In-flight accepted Discord requests cannot be recalled."; }
    private async Task LoopAsync(string group, CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) { await PollOnceAsync(group, ct); await Task.Delay(TimeSpan.FromMinutes(2), ct); } }
        catch (OperationCanceledException) { }
        catch { Status = "Checks paused after a local error. Review configuration and restart explicitly."; Stop(); }
    }
    public async Task PollOnceAsync(string group, CancellationToken cancellation = default, Func<AnnouncementPreview, Task<bool>>? approve = null, bool explicitReview = false)
    {
        if (!await _pollGate.WaitAsync(0, cancellation)) return;
        try
        {
            var run = _run;
            if (run == null || _activeGroup != group || (!explicitReview && _now() < _nextPoll)) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, run.Token);
            var ct = linked.Token;
            var ctx = _context();
            string? expectedDestination = null;
            bool Current() => !ct.IsCancellationRequested && _run == run && _activeGroup == group && _context() == ctx && ctx.LoggedIn && ctx.GroupId == group && !string.IsNullOrEmpty(ctx.AccountId) && GetConfiguration(group).Enabled && (expectedDestination == null || CurrentFingerprint(group) == expectedDestination);
            if (!Current()) { Stop(); return; }
            _nextPoll = _now().AddMinutes(2);
            using var db = Open(); using var cfg = db.CreateCommand(); cfg.CommandText = "SELECT Secret,Fingerprint,RetryUtc FROM Configuration WHERE GroupId=$g AND Enabled=1"; cfg.Parameters.AddWithValue("$g", group);
            string url, destination;
            using (var reader = cfg.ExecuteReader())
            {
                if (!reader.Read()) return;
                if (!reader.IsDBNull(2) && DateTime.TryParse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var retry) && retry > _now()) return;
                url = Encoding.UTF8.GetString(ProtectedData.Unprotect((byte[])reader[0], Entropy(group), DataProtectionScope.CurrentUser)); destination = reader.GetString(1);
            }
            if (!ValidWebhook(url)) return;
            expectedDestination = destination;
            var ready = new List<AnnouncementPreview>();
            Ready = Array.Empty<AnnouncementPreview>();
            var snapshot = await _discover(group, ct);
            if (!snapshot.Complete || !Current()) { Status = "Discovery incomplete/changed; no announcements or closure inference."; return; }
            if (approve == null) Status = "No new verified public-instance drafts. Nothing sent.";
            foreach (var instance in snapshot.Instances)
            {
                if (!Eligible(instance, group, _now()) || !Current()) continue;
                var content = $"A group-public instance is open. Group: {group}\nhttps://vrchat.com/home/launch?worldId={Uri.EscapeDataString(instance.WorldId)}&instanceId={Uri.EscapeDataString(instance.InstanceId)}";
                if (content.Length > 2000) continue;
                using var previous = db.CreateCommand();
                previous.CommandText = "SELECT 1 FROM Delivery WHERE GroupId=$g AND WorldId=$w AND InstanceId=$i AND Destination=$d";
                Bind(previous, group, instance, destination);
                if (previous.ExecuteScalar() != null) continue;
                var webhookId = new Uri(url).Segments[^2].TrimEnd('/');
                var preview = new AnnouncementPreview(ctx.AccountId!, group, webhookId, "Resolve on review", "Resolve on review", content, _now());
                if (approve == null)
                {
                    ready.Add(preview); Ready = ready.ToArray();
                    Status = $"{ready.Count} ready message(s). Monitoring never sends; Review and approve each item explicitly.";
                    continue;
                }
                // Resolve the exact Discord destination without exposing its token in preview/status/logs.
                using var destinationResponse = await _http.GetAsync(url, ct);
                if (!destinationResponse.IsSuccessStatusCode)
                {
                    if (destinationResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) Disable(group);
                    Status = "Discord destination unavailable; no approval or send performed.";
                    continue;
                }
                if (!Current()) continue;
                using var destinationJson = JsonDocument.Parse(await destinationResponse.Content.ReadAsStringAsync(ct));
                var metadata = destinationJson.RootElement;
                if (!metadata.TryGetProperty("id", out var destinationId) || destinationId.GetString() != webhookId ||
                    !metadata.TryGetProperty("channel_id", out var channel) || !metadata.TryGetProperty("guild_id", out var guild)) continue;
                var channelId = channel.GetString(); var guildId = guild.GetString();
                if (!Regex.IsMatch(channelId ?? "", @"\A[0-9]{1,20}\z") || !Regex.IsMatch(guildId ?? "", @"\A[0-9]{1,20}\z")) continue;
                preview = preview with { ChannelId = channelId!, GuildId = guildId!, PreviewUtc = _now() };
                if (!Current() || !await approve(preview) || !Current() || _now() - preview.PreviewUtc >= TimeSpan.FromMinutes(5)) continue;
                var fresh = await _discover(group, ct);
                if (!fresh.Complete || !fresh.Instances.Any(i => i.WorldId == instance.WorldId && i.InstanceId == instance.InstanceId && Eligible(i, group, _now())) || !Current() || _now() - preview.PreviewUtc >= TimeSpan.FromMinutes(5)) continue;
                using var finalDestinationResponse = await _http.GetAsync(url, ct);
                if (!finalDestinationResponse.IsSuccessStatusCode) continue;
                using var finalDestination = JsonDocument.Parse(await finalDestinationResponse.Content.ReadAsStringAsync(ct));
                var finalMetadata = finalDestination.RootElement;
                if (!finalMetadata.TryGetProperty("id", out var finalId) || finalId.GetString() != preview.WebhookId ||
                    !finalMetadata.TryGetProperty("channel_id", out var finalChannel) || finalChannel.GetString() != preview.ChannelId ||
                    !finalMetadata.TryGetProperty("guild_id", out var finalGuild) || finalGuild.GetString() != preview.GuildId ||
                    !Current() || _now() - preview.PreviewUtc >= TimeSpan.FromMinutes(5)) continue;
                using var claim = db.CreateCommand();
                claim.CommandText = "INSERT OR IGNORE INTO Delivery(GroupId,WorldId,InstanceId,Destination,State) VALUES($g,$w,$i,$d,'Unknown')";
                Bind(claim, group, instance, destination);
                if (claim.ExecuteNonQuery() != 1) continue;
                // The durable Unknown claim precedes HTTP; crashes/timeouts must never create duplicate retries.
                if (!Current()) { Update(db, group, instance, destination, "NotSent", null); continue; }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url + "?wait=true") { Content = new StringContent(JsonSerializer.Serialize(new { content, allowed_mentions = new { parse = Array.Empty<string>(), users = Array.Empty<string>(), roles = Array.Empty<string>(), replied_user = false } }), Encoding.UTF8, "application/json") };
                    using var response = await _http.SendAsync(request, ct);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        double seconds = 120;
                        try { using var json = JsonDocument.Parse(body); if (json.RootElement.TryGetProperty("retry_after", out var value) && value.TryGetDouble(out var n) && double.IsFinite(n)) seconds = Math.Max(seconds, n); } catch (JsonException) { }
                        if (response.Headers.RetryAfter?.Delta is TimeSpan delta) seconds = Math.Max(seconds, delta.TotalSeconds);
                        if (response.Headers.RetryAfter?.Date is DateTimeOffset date) seconds = Math.Max(seconds, (date.UtcDateTime - _now()).TotalSeconds);
                        var currentTime = _now();
                        var retryAt = seconds >= (DateTime.MaxValue - currentTime).TotalSeconds ? DateTime.MaxValue : currentTime.AddSeconds(seconds);
                        using var backoff = db.CreateCommand(); backoff.CommandText = "UPDATE Configuration SET RetryUtc=$r WHERE GroupId=$g; DELETE FROM Delivery WHERE GroupId=$g AND WorldId=$w AND InstanceId=$i AND Destination=$d"; Bind(backoff, group, instance, destination); backoff.Parameters.AddWithValue("$r", retryAt.ToString("O")); backoff.ExecuteNonQuery();
                        _nextPoll = retryAt; Status = "Discord rate limited; waiting before a proven-rejected request may be retried."; break;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        Update(db, group, instance, destination, (int)response.StatusCode >= 500 ? "Unknown" : "Rejected", null);
                        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            Disable(group);
                            Status = "Discord destination disabled after authorization/deleted-webhook rejection. Reconfigure explicitly; no automatic retries.";
                            break;
                        }
                        Status = "Discord rejected or outcome unknown; no automatic retry.";
                        continue;
                    }
                    using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var id = result.RootElement.GetProperty("id").GetString();
                    if (id == null || !Regex.IsMatch(id, @"\A[0-9]{1,20}\z")) continue;
                    Update(db, group, instance, destination, "SentUnverified", id);
                    using var readback = await _http.GetAsync(url + "/messages/" + id, ct);
                    if (!readback.IsSuccessStatusCode) continue;
                    using var message = JsonDocument.Parse(await readback.Content.ReadAsStringAsync(ct));
                    if (Current() && message.RootElement.GetProperty("id").GetString() == id && message.RootElement.GetProperty("content").GetString() == content &&
                        message.RootElement.GetProperty("channel_id").GetString() == preview.ChannelId && message.RootElement.GetProperty("webhook_id").GetString() == preview.WebhookId)
                        Update(db, group, instance, destination, "Confirmed", id);
                    Status = "Checked public instances. Accepted message IDs retained; no automatic resend on unverified outcomes.";
                }
                catch { Status = "Discord outcome unknown/unverified; retained claim prevents automatic duplicate sends."; }
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { Status = "Discovery/configuration error; no sends inferred successful and no closure inference. Retry is paced."; _nextPoll = _now().AddMinutes(5); }
        finally { _pollGate.Release(); }
    }
    public static bool Eligible(AnnounceableInstance i, string group, DateTime now) =>
        i.GroupId == group && i.Active == true && i.Closed == false && (i.Access == "public" || i.Access == "groupPublic") &&
        i.ObservedUtc <= now && now - i.ObservedUtc < TimeSpan.FromMinutes(2) &&
        Regex.IsMatch(i.WorldId, @"\Awrld_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\z") && !string.IsNullOrWhiteSpace(i.InstanceId) && i.InstanceId.Length < 1000 &&
        Regex.Matches(i.InstanceId, @"~group\(").Count == 1 && Regex.Matches(i.InstanceId, @"~groupAccessType\(").Count == 1 &&
        i.InstanceId.Contains("~group(" + group + ")", StringComparison.Ordinal) && i.InstanceId.Contains("~groupAccessType(public)", StringComparison.Ordinal) &&
        !Regex.IsMatch(i.InstanceId, @"~(private|hidden|friends)\(");
    private static void Bind(SqliteCommand cmd, string group, AnnounceableInstance instance, string destination) { cmd.Parameters.AddWithValue("$g", group); cmd.Parameters.AddWithValue("$w", instance.WorldId); cmd.Parameters.AddWithValue("$i", instance.InstanceId); cmd.Parameters.AddWithValue("$d", destination); }
    private static void Update(SqliteConnection db, string group, AnnounceableInstance instance, string destination, string state, string? id)
    { using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE Delivery SET State=$s, MessageId=$m WHERE GroupId=$g AND WorldId=$w AND InstanceId=$i AND Destination=$d"; Bind(cmd, group, instance, destination); cmd.Parameters.AddWithValue("$s", state); cmd.Parameters.AddWithValue("$m", (object?)id ?? DBNull.Value); cmd.ExecuteNonQuery(); }
    public void Dispose() { Stop(); _http.Dispose(); }
}
