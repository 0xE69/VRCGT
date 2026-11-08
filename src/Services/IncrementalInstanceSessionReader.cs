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
    public InstanceDetectionDiagnostic DetectionDiagnostic { get; private set; } = new(InstanceDetectionCode.Unknown);
    private CurrentInstanceInfo? Unavailable(InstanceDetectionCode code)
    {
        DetectionDiagnostic = new(code);
        _source = null; _current = null; _pendingLocation = null; _partial = "";
        return null;
    }
    // Syntax anchoring prevents marker substrings in names from acting as events.
    // Logs (even forged full envelopes) remain untrusted hints, not API presence.
    private static readonly Regex BehaviourRecord = new(@"^(?:\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}\s+Log\s+-\s+)?\[Behaviour\]\s+(?<payload>.*)$", RegexOptions.Compiled);
    private static bool IsEvent(string payload, string marker) => payload == marker
        || payload.StartsWith(marker + " ", StringComparison.Ordinal)
        || payload.StartsWith(marker + ":", StringComparison.Ordinal);
    private static readonly Regex Location = new(@"(?<world>wrld_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}):(?<instance>[^\s]+)", RegexOptions.Compiled);
    public CurrentInstanceInfo? Read(string directory, DateTime? process, DateTime now)
    {
        lock (_lock)
        {
            if (process == null) return Unavailable(InstanceDetectionCode.ProcessUnavailable);
            if (process > now) return Unavailable(InstanceDetectionCode.ClockMismatch);
            if (!Directory.Exists(directory)) return Unavailable(InstanceDetectionCode.LogDirectoryMissing);
            var file = Directory.GetFiles(directory, "output_log_*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (file == null) return Unavailable(InstanceDetectionCode.LogMissing);
            var info = new FileInfo(file);
            if (info.CreationTimeUtc < process.Value.AddSeconds(-5) || info.LastWriteTimeUtc < process.Value)
                return Unavailable(InstanceDetectionCode.LogSessionMismatch);
            if (info.LastWriteTimeUtc > now.AddSeconds(5)) return Unavailable(InstanceDetectionCode.ClockMismatch);
            if (now - info.LastWriteTimeUtc > TimeSpan.FromMinutes(2)) return Unavailable(InstanceDetectionCode.LogNotFresh);
            var source = $"{process.Value.Ticks}:{file}:{info.CreationTimeUtc.Ticks}";
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (source != _source || stream.Length < _offset)
            {
                if (stream.Length > CurrentInstanceSessionReader.MaximumInitialLogBytes) return Unavailable(InstanceDetectionCode.LogScanLimit);
                _source = source; _offset = 0;
                _current = null; _pendingLocation = null; _partial = ""; ++_joinGeneration;
                DetectionDiagnostic = new(InstanceDetectionCode.JoinNotObserved);
            }
            stream.Position = _offset;
            var appended = CurrentInstanceSessionReader.ReadBoundedSnapshot(stream);
            if (appended == null || _partial.Length + appended.Length > CurrentInstanceSessionReader.MaximumInitialLogBytes)
                return Unavailable(InstanceDetectionCode.LogScanLimit);
            var text = _partial + appended; _offset = stream.Position;
            var lines = text.Split('\n'); _partial = lines[^1];
            foreach (var line in lines.Take(lines.Length - 1))
            {
                var envelope = BehaviourRecord.Match(line);
                if (!envelope.Success) continue;
                var payload = envelope.Groups["payload"].Value.TrimEnd('\r');
                if (IsEvent(payload, "OnDisconnected") || IsEvent(payload, "OnLeftRoom") || payload.StartsWith("Entering Room:", StringComparison.Ordinal))
                {
                    _current = null; _pendingLocation = null; ++_joinGeneration;
                    DetectionDiagnostic = new(payload.StartsWith("Entering Room:", StringComparison.Ordinal) ? InstanceDetectionCode.JoinIncomplete : InstanceDetectionCode.LeftOrDisconnected);
                    continue;
                }
                if (payload.StartsWith("Joining or Creating Room:", StringComparison.Ordinal))
                {
                    // Name-only second stage of the SAME join. It supplies no address and
                    // cannot establish one, but must not discard a valid pending Joining.
                    _current = null;
                    DetectionDiagnostic = new(InstanceDetectionCode.JoinIncomplete);
                    continue;
                }
                if (payload.StartsWith("Joining ", StringComparison.Ordinal) || payload.StartsWith("Rejoining local world:", StringComparison.Ordinal))
                {
                    _current = null; ++_joinGeneration;
                    var address = payload.StartsWith("Joining ", StringComparison.Ordinal) ? payload[8..] : payload["Rejoining local world:".Length..].TrimStart();
                    var match = Location.Match(address); _pendingLocation = match.Success && match.Index == 0 ? match.Value : null;
                    DetectionDiagnostic = new(_pendingLocation != null ? InstanceDetectionCode.JoinIncomplete : InstanceDetectionCode.LocationUnrecognized);
                    continue;
                }
                if ((IsEvent(payload, "OnJoinedRoom") || IsEvent(payload, "Successfully joined room")) && _pendingLocation != null)
                {
                    var match = Location.Match(_pendingLocation);
                    _current = new() { WorldId = match.Groups["world"].Value, InstanceId = match.Groups["instance"].Value,
                        FullLocation = match.Value, DetectedAt = now.ToLocalTime(), SessionKey = source + ":" + _joinGeneration };
                    _pendingLocation = null;
                    DetectionDiagnostic = new(InstanceDetectionCode.Detected);
                }
            }
            return _current;
        }
    }
}
