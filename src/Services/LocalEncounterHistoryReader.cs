using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Services;

public sealed record GameLogFile(string FullPath, long SizeBytes, DateTime LastWriteTime, bool ReadError)
{
    public string Name => Path.GetFileName(FullPath);
}

public sealed record GameLogScanResult(IReadOnlyList<GameLogFile> Files, List<InstanceUser> Encounters);

internal static class VrchatLogFormat
{
    public const string OptionalHeader = @"(?:(?<timestamp>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)\s+(?:Log|Debug)\s+-\s+)?";
    public const string UserId = @"usr_[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}";
    private static readonly string[] TimeFormats = ["yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm:ss.FFFFFFF"];

    public static DateTime Timestamp(Match match)
    {
        DateTime.TryParseExact(match.Groups["timestamp"].Value, TimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var timestamp);
        return timestamp;
    }
}

/// <summary>Reads complete local player records across sessions. A cursor belongs to one consumer.</summary>
public sealed class LocalEncounterHistoryReader
{
    // Only a single record is bounded. All bytes in every file are visited, including after oversized lines.
    private const int MaxRecordBytes = 16 * 1024;
    private const int ChunkBytes = 64 * 1024;
    private const string NameAndId = @"(?<name>.+?)(?:\s+\((?<id>" + VrchatLogFormat.UserId + @")\))?";
    private static readonly Regex Joined = new("^" + VrchatLogFormat.OptionalHeader +
        @"(?:\[Behaviour\]\s+OnPlayerJoined\s+" + NameAndId + @"|\[Player\]\s+" + NameAndId + @"\s+joined)\s*$", RegexOptions.Compiled);
    private static readonly Regex WorldTransition = new("^" + VrchatLogFormat.OptionalHeader +
        @"\[Behaviour\]\s+(?:OnLeftRoom\b|OnDisconnected\b|Entering Room:|" +
        @"(?:Joining(?!\s+or Creating Room:)\s+|Rejoining local world:\s*)(?:(?<world>wrld_[a-fA-F0-9-]{36})(?=[:\s]|$))?)", RegexOptions.Compiled);

    private sealed record Cursor(DateTime CreatedUtc, DateTime ModifiedUtc, long Offset, byte[] Partial, bool Discard, string? WorldId);
    private Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "VRChat", "VRChat");
    public string Diagnostic { get; private set; } = "Game logs have not been scanned yet.";

    public Task<GameLogScanResult> ScanAsync(string directory, GroupInviteLedger ledger) =>
        ScanCoreAsync(directory, ledger ?? throw new ArgumentNullException(nameof(ledger)));

    internal Task<GameLogScanResult> PreviewAsync(string directory) => ScanCoreAsync(directory, null);

    private async Task<GameLogScanResult> ScanCoreAsync(string directory, GroupInviteLedger? ledger)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var next = new Dictionary<string, Cursor>(StringComparer.OrdinalIgnoreCase);
            var scan = await Task.Run(() => ReadDirectory(directory, next)).ConfigureAwait(false);
            if (ledger != null && scan.Encounters.Count > 0)
                await SaveDiscoveryAsync(ledger, scan.Encounters).ConfigureAwait(false);
            // A failed SQLite transaction must leave the complete batch available for retry.
            _cursors = next;
            return scan;
        }
        finally { _gate.Release(); }
    }

    internal static Task SaveDiscoveryAsync(GroupInviteLedger ledger, List<InstanceUser> users) =>
        ledger.SaveLocalDiscoveriesAsync(users.Select(u => new SeenUser
        {
            UserId = u.UserId, DisplayName = u.DisplayName, LastSeenAt = u.JoinTime, WorldId = u.WorldId,
            Source = "Local VRChat game log"
        }).ToList());

    private GameLogScanResult ReadDirectory(string directory, Dictionary<string, Cursor> next)
    {
        var files = new List<GameLogFile>();
        var users = new Dictionary<string, InstanceUser>(StringComparer.OrdinalIgnoreCase);
        string[] paths;
        try { paths = Directory.GetFiles(directory, "output_log*.txt"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostic = ex is DirectoryNotFoundException ? "VRChat log directory missing." : "VRChat log directory could not be read.";
            return new(files, []);
        }
        var unsupported = false;
        var buffer = new byte[ChunkBytes + MaxRecordBytes];
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = new FileInfo(path);
            var size = 0L;
            var modified = DateTime.MinValue;
            var readError = false;
            _cursors.TryGetValue(path, out var cursor);
            try
            {
                size = file.Length;
                modified = file.LastWriteTime;
                if (cursor == null || cursor.CreatedUtc != file.CreationTimeUtc || size < cursor.Offset ||
                    (size == cursor.Offset && cursor.ModifiedUtc != file.LastWriteTimeUtc))
                    cursor = new(file.CreationTimeUtc, file.LastWriteTimeUtc, 0, [], false, null);
                if (size > cursor.Offset)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    cursor = ReadFile(stream, cursor, size, buffer, users, ref unsupported) with { ModifiedUtc = file.LastWriteTimeUtc };
                }
                next[path] = cursor;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_cursors.TryGetValue(path, out var previous)) next[path] = previous;
                readError = true;
            }
            files.Add(new(path, size, modified, readError));
        }
        var failures = files.Count(f => f.ReadError);
        Diagnostic = files.Count == 0 ? "No VRChat output logs found." : $"{files.Count - failures} of {files.Count} local game logs readable.";
        if (unsupported) Diagnostic += " Unsupported player record format encountered.";
        return new(files.OrderByDescending(f => f.LastWriteTime).ToList(), users.Values.ToList());
    }

    private static Cursor ReadFile(FileStream stream, Cursor cursor, long end, byte[] buffer,
        Dictionary<string, InstanceUser> users, ref bool unsupported)
    {
        stream.Position = cursor.Offset;
        var pending = cursor.Partial.Length;
        cursor.Partial.CopyTo(buffer, 0);
        var discard = cursor.Discard;
        var world = cursor.WorldId;
        while (stream.Position < end)
        {
            var count = stream.Read(buffer, pending, (int)Math.Min(ChunkBytes, end - stream.Position));
            if (count == 0) throw new EndOfStreamException();
            var length = pending + count;
            var start = 0;
            int newline;
            while ((newline = Array.IndexOf(buffer, (byte)'\n', start, length - start)) >= 0)
            {
                if (!discard && newline - start <= MaxRecordBytes)
                    ParseLine(buffer.AsSpan(start, newline - start), users, ref world, ref unsupported);
                start = newline + 1;
                discard = false;
            }
            pending = length - start;
            discard |= pending > MaxRecordBytes;
            if (discard) pending = 0;
            else if (pending > 0) Buffer.BlockCopy(buffer, start, buffer, 0, pending);
        }
        return cursor with { Offset = end, Partial = buffer[..pending], Discard = discard, WorldId = world };
    }

    private static void ParseLine(ReadOnlySpan<byte> bytes, Dictionary<string, InstanceUser> users,
        ref string? world, ref bool unsupported)
    {
        if (bytes.IndexOf("[Behaviour]"u8) < 0 && bytes.IndexOf("[Player]"u8) < 0 &&
            bytes.IndexOf("OnPlayerJoined"u8) < 0) return;
        var line = Encoding.UTF8.GetString(bytes).TrimEnd('\r').TrimStart('\uFEFF');
        var match = Joined.Match(line);
        if (!match.Success)
        {
            var transition = WorldTransition.Match(line);
            if (transition.Success) world = transition.Groups["world"].Success ? transition.Groups["world"].Value : null;
            unsupported |= line.Contains("OnPlayerJoined", StringComparison.Ordinal);
            return;
        }
        var name = match.Groups["name"].Value.Trim();
        if (name.Length == 0) return;
        var id = match.Groups["id"].Value.ToLowerInvariant();
        var key = id.Length > 0 ? id : "name:" + name;
        var seen = VrchatLogFormat.Timestamp(match);
        if (!users.TryGetValue(key, out var prior) || prior.JoinTime <= seen)
            users[key] = new InstanceUser { DisplayName = name, UserId = id, JoinTime = seen, WorldId = world };
    }
}
