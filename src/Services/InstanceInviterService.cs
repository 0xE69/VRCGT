using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VRCGroupTools.Services;

public interface IInstanceInviterService
{
    CurrentInstanceInfo? GetCurrentInstance();
    InstanceDetectionDiagnostic DetectionDiagnostic => new(InstanceDetectionCode.Unknown);
    Task<bool> InviteToCurrentInstanceAsync(string userId, IVRChatApiService apiService);
    Task<List<InstanceUser>> GetCurrentInstanceUsersAsync(IVRChatApiService apiService);
    // Only the approved local VRChat output-log directory; independent of live-instance detection.
    Task<List<InstanceUser>> ReadLocalEncounterHistoryAsync() => Task.FromResult(new List<InstanceUser>());
    string EncounterHistoryDiagnostic => "Unsupported reader";
}

public class InstanceInviterService : IInstanceInviterService
{
    private readonly string _logDirectory;
    private readonly Func<DateTime?> _processStartedUtc;
    private readonly Func<DateTime> _utcNow;
    private readonly IncrementalInstanceSessionReader _sessionReader = new();
    public InstanceDetectionDiagnostic DetectionDiagnostic { get; private set; } = new(InstanceDetectionCode.Unknown);

    public InstanceInviterService() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "VRChat", "VRChat"),
        GetProcessStartedUtc, () => DateTime.UtcNow) { }

    public InstanceInviterService(string logDirectory, Func<DateTime?> processStartedUtc, Func<DateTime> utcNow)
    {
        _logDirectory = logDirectory;
        _processStartedUtc = processStartedUtc;
        _utcNow = utcNow;
    }

    private readonly LocalEncounterHistoryReader _encounterReader = new();
    public string EncounterHistoryDiagnostic => _encounterReader.Diagnostic;
    public Task<List<InstanceUser>> ReadLocalEncounterHistoryAsync() => Task.FromResult(_encounterReader.Read(_logDirectory));

    private static DateTime? GetProcessStartedUtc()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("VRChat");
        try
        {
            // Multiple clients cannot be associated reliably with one log. Fail closed.
            if (processes.Length == 0) throw new InstanceDetectionException(InstanceDetectionCode.ClientNotRunning);
            if (processes.Length != 1) throw new InstanceDetectionException(InstanceDetectionCode.MultipleClients);
            return !processes[0].HasExited ? processes[0].StartTime.ToUniversalTime() : null;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public CurrentInstanceInfo? GetCurrentInstance()
    {
        DateTime? process;
        try { process = _processStartedUtc(); }
        catch (InstanceDetectionException ex)
        {
            DetectionDiagnostic = new(ex.Code);
            return null;
        }
        catch (Exception)
        {
            DetectionDiagnostic = new(InstanceDetectionCode.ProcessInspectionFailed);
            return null;
        }
        try
        {
            var current = _sessionReader.Read(_logDirectory, process, _utcNow());
            DetectionDiagnostic = _sessionReader.DetectionDiagnostic;
            return current;
        }
        catch (Exception)
        {
            // Never expose exceptions that may contain a user path or private location.
            DetectionDiagnostic = new(InstanceDetectionCode.LogReadFailed);
            return null;
        }
    }

    public async Task<bool> InviteToCurrentInstanceAsync(string userId, IVRChatApiService apiService)
    {
        var currentInstance = GetCurrentInstance();
        if (currentInstance == null)
        {
            Console.WriteLine("[INSTANCE-INVITER] Cannot invite: No current instance detected");
            return false;
        }

        Console.WriteLine($"[INSTANCE-INVITER] Inviting user {userId} to {currentInstance.WorldId}:{currentInstance.InstanceId}");
        return await apiService.InviteUserToInstanceAsync(userId, currentInstance.WorldId, currentInstance.InstanceId);
    }

    // Local-only discovery. Details are resolved once, on demand, by the viewmodel for enabled filters.
    // Never resolve a log display name by choosing an arbitrary search result.
    private string? _usersSession;
    private string? _usersFile;
    private long _usersOffset;
    private readonly Dictionary<string, InstanceUser> _localUsers = new(StringComparer.Ordinal);
    private readonly object _localLock = new();
    private string _partial = "";
    public Task<List<InstanceUser>> GetCurrentInstanceUsersAsync(IVRChatApiService apiService) => Task.FromResult(GetLocalUsers());

    public List<InstanceUser> GetLocalUsers()
    {
        lock (_localLock)
        {
            var current = GetCurrentInstance();
            if (current == null) { ResetUsers(); return new(); }
            var file = Directory.GetFiles(_logDirectory, "output_log_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (file == null) { ResetUsers(); return new(); }
            var session = current.SessionKey + ":" + current.FullLocation;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (session != _usersSession || file != _usersFile || stream.Length < _usersOffset)
            {
                ResetUsers(); _usersSession = session; _usersFile = file;
                _usersOffset = Math.Max(0, stream.Length - 5_000_000);
            }
            stream.Position = _usersOffset;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var content = _partial + reader.ReadToEnd();
            _usersOffset = stream.Position;
            var lines = content.Split('\n'); _partial = lines[^1];
            foreach (var raw in lines.Take(lines.Length - 1))
            {
                var line = raw.TrimEnd((char)13);
                if (line.Contains("[Behaviour]") && (line.Contains("Joining ") || line.Contains("Joining or Creating Room:") || line.Contains("Rejoining local world:") || line.Contains("OnLeftRoom") || line.Contains("OnDisconnected")))
                { _localUsers.Clear(); continue; }
                // Only recognized envelopes are discovery hints. Even full timestamped envelopes can be forged
                // by multiline Udon text; automatic eligibility MUST independently corroborate identity/location.
                var match = Regex.Match(line, @"^(?:\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}\s+Log\s+-\s+)?\[Behaviour\]\s+OnPlayer(?<kind>Joined|Left)\s+(?<name>.*?)(?:\s+\((?<id>usr_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\))?\s*$");
                if (!match.Success) continue;
                var name = match.Groups["name"].Value.Trim();
                var id = match.Groups["id"].Value;
                var key = id.Length == 0 ? "name:" + name : id;
                if (match.Groups["kind"].Value == "Left")
                {
                    if (id.Length > 0) _localUsers.Remove(key);
                    else foreach (var departed in _localUsers.Where(p => p.Value.DisplayName == name).Select(p => p.Key).ToArray()) _localUsers.Remove(departed);
                    continue;
                }
                var joined = DateTime.MinValue;
                if (line.Length >= 19) DateTime.TryParseExact(line[..19], "yyyy.MM.dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out joined);
                _localUsers[key] = new() { UserId = id, DisplayName = name, JoinTime = joined };
            }
            return _localUsers.Values.ToList();
        }
    }
    private void ResetUsers() { _localUsers.Clear(); _usersSession = null; _usersFile = null; _usersOffset = 0; _partial = ""; }

}

// Read-only, bounded discovery, NOT proof of encounter. Do not reuse this reader for a live destination.
// Known bare/second-resolution Log envelope only; other client headers remain unsupported.
internal sealed class LocalEncounterHistoryReader
{
    private sealed class Cursor { public long Offset; public byte[] Partial=Array.Empty<byte>(); public bool Discard; }
    private readonly Dictionary<string,Cursor> _files=new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate=new();
    private static readonly Regex Joined=new(@"^(?:\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}\s+Log\s+-\s+)?\[Behaviour\]\s+OnPlayerJoined\s+(?<name>.*?)\s+\((?<id>usr_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\)\s*$",RegexOptions.Compiled);
    public string Diagnostic { get; private set; }="No recognized local history records";
    public List<InstanceUser> Read(string directory)
    {
        lock (_gate)
        {
            var found=new Dictionary<string,InstanceUser>(StringComparer.Ordinal);
            try
            {
                if (!Directory.Exists(directory)) { Diagnostic="Local log directory missing"; return new(); }
                var files=Directory.GetFiles(directory,"output_log_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Take(16).ToArray();
                foreach (var old in _files.Keys.Except(files,StringComparer.OrdinalIgnoreCase).ToArray()) _files.Remove(old);
                var budget=1_000_000; var unsupported=false; var read=false;
                foreach (var file in files)
                {
                    if (budget<=0) break;
                    using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    if (!_files.TryGetValue(file,out var cursor) || stream.Length<cursor.Offset)
                    {
                        cursor=new Cursor { Offset=Math.Max(0,stream.Length-5_000_000) }; cursor.Discard=cursor.Offset>0; _files[file]=cursor;
                    }
                    var count=(int)Math.Min(budget,stream.Length-cursor.Offset);
                    if (count<=0) continue;
                    stream.Position=cursor.Offset; var bytes=new byte[count]; var length=stream.Read(bytes,0,count);
                    cursor.Offset+=length; budget-=length; read|=length>0;
                    var combined=new byte[cursor.Partial.Length+length]; cursor.Partial.CopyTo(combined,0); Array.Copy(bytes,0,combined,cursor.Partial.Length,length);
                    var start=0;
                    for(var i=0;i<combined.Length;i++)
                    {
                        if(combined[i]!=10) continue;
                        if(!cursor.Discard && i-start<=16_384)
                        {
                            var line=System.Text.Encoding.UTF8.GetString(combined,start,i-start).TrimEnd('\r');
                            var match=Joined.Match(line);
                            if(match.Success)
                            {
                                var id=match.Groups["id"].Value;
                                var joined=DateTime.MinValue;
                                if(line.Length>=19) DateTime.TryParseExact(line[..19],"yyyy.MM.dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out joined);
                                // Left/disconnect/room-ended records never delete history.
                                found.Remove(id); found[id]=new() { UserId=id,DisplayName=match.Groups["name"].Value,JoinTime=joined };
                                if(found.Count>GroupInviteLedger.EncounterLimit) found.Remove(found.Keys.First());
                            }
                            else if(line.Contains("OnPlayerJoined",StringComparison.Ordinal)) unsupported=true;
                        }
                        start=i+1; cursor.Discard=false;
                    }
                    var remaining=combined.Length-start;
                    cursor.Discard|=remaining>16_384;
                    cursor.Partial=cursor.Discard?Array.Empty<byte>():combined[start..];
                }
                if(unsupported) Diagnostic="Unsupported player record format; only known envelopes/exact IDs read (not all client headers supported)";
                else if(found.Count>0) Diagnostic="Known-format LOCAL log discovery only; 16 latest files, last 5 MB/file, 1 MB/check; NOT verified encounter";
                else if(read) Diagnostic="No recognized exact-ID local records in scanned range (format may be unsupported)";
                return found.Values.ToList();
            }
            catch { Diagnostic="Local history read unavailable; retained queue unchanged"; return found.Values.ToList(); }
        }
    }
}

public class CurrentInstanceInfo
{
    // Regex to extract group ID from instance ID (e.g., "12345~group(grp_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)")
    private static readonly Regex GroupInstanceRegex = new Regex(
        @"group\(grp_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string WorldId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string FullLocation { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>
    /// Returns true if this instance belongs to a group (group instance).
    /// </summary>
    public bool IsGroupInstance => GroupInstanceRegex.IsMatch(InstanceId);

    /// <summary>
    /// Extracts the group ID from the instance ID if this is a group instance.
    /// Returns null if not a group instance.
    /// </summary>
    public string? GetInstanceGroupId()
    {
        var match = GroupInstanceRegex.Match(InstanceId);
        if (!match.Success)
            return null;

        // Extract just the grp_xxx part from "group(grp_xxx)"
        var groupPart = match.Value; // e.g., "group(grp_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"
        var startIndex = groupPart.IndexOf("grp_");
        var endIndex = groupPart.LastIndexOf(")");
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return groupPart.Substring(startIndex, endIndex - startIndex);
        }
        return null;
    }

    /// <summary>
    /// Checks if sending group invites is allowed from this instance.
    /// Returns true if: not a group instance, OR it's the same group's instance.
    /// Returns false if: it's a different group's instance.
    /// </summary>
    public bool IsGroupInviteAllowed(string targetGroupId)
    {
        if (!IsGroupInstance)
        {
            // Non-group instances are always allowed
            return true;
        }

        var instanceGroupId = GetInstanceGroupId();
        if (string.IsNullOrEmpty(instanceGroupId))
        {
            // Couldn't extract group ID, allow by default
            return true;
        }

        // Only allow if the instance belongs to the same group we're inviting to
        return string.Equals(instanceGroupId, targetGroupId, StringComparison.OrdinalIgnoreCase);
    }
}

public class InstanceUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public bool IsAgeVerified { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime JoinTime { get; set; }
}
