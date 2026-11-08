using System.IO;
using System.Text;

namespace VRCGroupTools.Services;

/// <summary>Reads a fresh snapshot only; never emits log lines or private location modifiers.</summary>
public static class CurrentInstanceSessionReader
{
    // A tail without its join baseline cannot establish the latest completed session.
    // Bound initial scanning and fail closed beyond it rather than guessing from a tail.
    internal const long MaximumInitialLogBytes = 64_000_000;
    internal static string? ReadBoundedSnapshot(FileStream stream)
    {
        // Snapshot the byte count: concurrent log appends belong to the next poll.
        var length = stream.Length - stream.Position;
        if (length > MaximumInitialLogBytes) return null;
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
    // Share the transition reducer: latest incomplete joins cannot fall back to history.
    public static CurrentInstanceInfo? Read(string directory, DateTime? processStartedUtc, DateTime utcNow)
        => new IncrementalInstanceSessionReader().Read(directory, processStartedUtc, utcNow);
}
