using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

/// <summary>Incremental local reader: unchanged polls read no log contents; only appended complete lines are processed.
/// A rejoin, even to the same address, creates a new consent/session identity. Never logs private locations.</summary>
public sealed class IncrementalInstanceSessionReader
{
    private string? _source;
    private long _offset;
    private long _joinGeneration;
    private string _partial = "";
    private string? _pendingLocation;
    private CurrentInstanceInfo? _current;
    private readonly object _lock = new();
    private static readonly Regex Location = new(@"(?<world>wrld_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}):(?<instance>[^\s]+)", RegexOptions.Compiled);
    public CurrentInstanceInfo? Read(string directory, DateTime? process, DateTime now)
    {
        lock (_lock)
        {
            if (process == null || process > now || !Directory.Exists(directory)) { _source = null; return null; }
            var file = Directory.GetFiles(directory, "output_log_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (file == null) { _source = null; return null; }
            var info = new FileInfo(file);
            if (info.CreationTimeUtc < process.Value.AddSeconds(-5) || info.LastWriteTimeUtc < process.Value || now - info.LastWriteTimeUtc > TimeSpan.FromMinutes(2) || info.LastWriteTimeUtc > now.AddSeconds(5)) { _source = null; return null; }
            var source = $"{process.Value.Ticks}:{file}:{info.CreationTimeUtc.Ticks}";
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (source != _source || stream.Length < _offset)
            {
                _source = source; _offset = Math.Max(0, stream.Length - 5_000_000);
                _current = null; _pendingLocation = null; _partial = ""; ++_joinGeneration;
            }
            stream.Position = _offset;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = _partial + reader.ReadToEnd(); _offset = stream.Position;
            var lines = text.Split('\n'); _partial = lines[^1];
            foreach (var line in lines.Take(lines.Length - 1))
            {
                if (!line.Contains("[Behaviour]")) continue;
                if (line.Contains("OnDisconnected") || line.Contains("OnLeftRoom") || line.Contains("Joining or Creating Room:"))
                { _current = null; _pendingLocation = null; ++_joinGeneration; continue; }
                if (line.Contains("Joining ") || line.Contains("Rejoining local world:"))
                {
                    _current = null; ++_joinGeneration;
                    var match = Location.Match(line); _pendingLocation = match.Success ? match.Value : null;
                    continue;
                }
                if (line.Contains("OnJoinedRoom") && _pendingLocation != null)
                {
                    var match = Location.Match(_pendingLocation);
                    _current = new() { WorldId = match.Groups["world"].Value, InstanceId = match.Groups["instance"].Value,
                        FullLocation = match.Value, DetectedAt = now.ToLocalTime(), SessionKey = source + ":" + _joinGeneration };
                    _pendingLocation = null;
                }
            }
            return _current;
        }
    }
}
