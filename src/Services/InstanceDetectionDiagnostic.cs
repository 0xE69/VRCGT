namespace VRCGroupTools.Services;

// Safe to share for support: no log text, file paths, account IDs, or instance locations.
public enum InstanceDetectionCode
{
    Unknown, Detected, ClientNotRunning, MultipleClients, ProcessUnavailable, ProcessInspectionFailed,
    LogDirectoryMissing, LogMissing, LogReadFailed, LogSessionMismatch, ClockMismatch, LogNotFresh,
    LogScanLimit, JoinNotObserved, JoinIncomplete, LocationUnrecognized, LeftOrDisconnected
}

public sealed record InstanceDetectionDiagnostic(InstanceDetectionCode Code)
{
    public string Message => Code switch
    {
        InstanceDetectionCode.Detected => "A completed current-client join was detected.",
        InstanceDetectionCode.ClientNotRunning => "No local VRChat client was found. Run VRCGT on the same Windows PC and account as VRChat.",
        InstanceDetectionCode.MultipleClients => "More than one VRChat client was found. Close extra clients and refresh.",
        InstanceDetectionCode.ProcessUnavailable => "The current VRChat process session could not be established. Check that exactly one client is running locally.",
        InstanceDetectionCode.ProcessInspectionFailed => "Windows could not inspect the VRChat process. Check that both apps use the same Windows account and access level, then refresh.",
        InstanceDetectionCode.LogDirectoryMissing => "The default VRChat log folder is missing for this Windows account. Check which PC/account runs VRChat and whether logging is enabled.",
        InstanceDetectionCode.LogMissing => "No VRChat output log was found. Check whether client logging is enabled, then refresh after joining.",
        InstanceDetectionCode.LogReadFailed => "The client log could not be read or rotated during detection. Refresh again; check log access if this persists.",
        InstanceDetectionCode.LogSessionMismatch => "The latest log cannot be associated with the running client session. Refresh after the client writes its current log; historical rooms are not used.",
        InstanceDetectionCode.ClockMismatch => "Process or log timestamps are ahead of the local clock. Check the Windows clock, then refresh.",
        InstanceDetectionCode.LogNotFresh => "The log has not changed for over two minutes. VRChat may simply be quiet; this does not prove you left. Refresh after new client activity or rejoining. Destination-instance invitations remain blocked until evidence is fresh; GROUP invitations use separately consented Game History.",
        InstanceDetectionCode.LogScanLimit => "The log exceeds the bounded detection scan. Restart VRChat and rejoin to create a new session log, then refresh.",
        InstanceDetectionCode.JoinNotObserved => "No supported completed-join sequence was found in this client log. Finish joining or rejoin, then refresh; if it persists, share this code and the app version, not the log.",
        InstanceDetectionCode.JoinIncomplete => "A join or room transition is not yet confirmed complete. Wait until the world finishes loading and refresh.",
        InstanceDetectionCode.LocationUnrecognized => "A join marker was found but its location format was not recognized. Share this code and the app version, not private location text.",
        InstanceDetectionCode.LeftOrDisconnected => "The latest recognized session event is a leave or disconnect. Finish joining a world and refresh.",
        _ => "The current instance could not be verified. Refresh and share the app version and this diagnostic code if it persists."
    };
    public string Display => $"Instance detection unverified [{Code}]. {Message}";
}

internal sealed class InstanceDetectionException(InstanceDetectionCode code) : Exception
{
    internal InstanceDetectionCode Code { get; } = code;
}
