using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

/// <summary>Reads a fresh snapshot only; never emits log lines or private location modifiers.</summary>
public static class CurrentInstanceSessionReader
{
    private static readonly Regex Location = new(
        @"(?<world>wrld_[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}):(?<instance>[^\s]+)",
        RegexOptions.Compiled);

    public static CurrentInstanceInfo? Read(string directory, DateTime? processStartedUtc, DateTime utcNow)
    {
        if (processStartedUtc == null || processStartedUtc > utcNow || !Directory.Exists(directory)) return null;
        var file = Directory.GetFiles(directory, "output_log_*.txt")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (file == null) return null;
        var modifiedUtc = File.GetLastWriteTimeUtc(file);
        if (File.GetCreationTimeUtc(file) < processStartedUtc.Value.AddSeconds(-5)
            || modifiedUtc < processStartedUtc.Value
            || utcNow - modifiedUtc > TimeSpan.FromMinutes(2)
            || modifiedUtc > utcNow.AddSeconds(5)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > 5_000_000) stream.Seek(-5_000_000, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n');
        var joined = false;
        foreach (var line in lines.Reverse())
        {
            if (!line.Contains("[Behaviour]")) continue;
            if (line.Contains("OnLeftRoom") || line.Contains("OnDisconnected")
                || line.Contains("Joining or Creating Room:")) return null;
            if (line.Contains("OnJoinedRoom")) { joined = true; continue; }
            if (!line.Contains("Joining ") && !line.Contains("Rejoining local world:")) continue;
            if (!joined) return null;
            var match = Location.Match(line);
            if (!match.Success) return null;
            return new()
            {
                WorldId = match.Groups["world"].Value,
                InstanceId = match.Groups["instance"].Value,
                FullLocation = match.Value,
                DetectedAt = utcNow.ToLocalTime()
            };
        }
        return null;
    }
}
