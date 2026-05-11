using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VRCGT.Core;

/// <summary>Whether the Kestrel server binds only to loopback or to all interfaces.</summary>
public enum BindMode
{
    LocalOnly = 0,
    Lan       = 1,
}

/// <summary>Current server access configuration returned by GET/PATCH /api/system/access.</summary>
public sealed record AccessSettingsDto(
    /// <summary>"LocalOnly" or "Lan" — the <em>saved</em> setting (takes effect after restart).</summary>
    string SavedBindMode,
    /// <summary>"LocalOnly" or "Lan" — what Kestrel is actually bound to right now.</summary>
    string RuntimeBindMode,
    int    Port,
    /// <summary>True when SavedBindMode != RuntimeBindMode (restart needed to apply change).</summary>
    bool   RequiresRestart,
    /// <summary>URLs Kestrel is currently listening on.</summary>
    IReadOnlyList<string> ListenUrls,
    /// <summary>Private-network IPv4 addresses detected on this machine.</summary>
    IReadOnlyList<string> LanAddresses
);

/// <summary>
/// Manages the server bind-mode setting and LAN IP detection.
/// Implemented by VRCGT.Infrastructure.AccessSettingsService.
/// </summary>
public interface IAccessSettingsService
{
    /// <summary>Returns current settings including runtime vs saved state.</summary>
    Task<AccessSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Persists the new bind mode (takes effect after server restart).</summary>
    Task<AccessSettingsDto> UpdateAsync(BindMode newMode, CancellationToken ct = default);

    /// <summary>Returns private-network (RFC1918 / APIPA) IPv4 addresses on this machine.</summary>
    IReadOnlyList<string> GetLanAddresses();
}

/// <summary>
/// Static utilities for detecting LAN addresses and reading/writing the
/// access-settings sidecar file <em>before</em> the DI container is built.
/// Called directly by <c>ServerAppFactory.CreateAsync()</c> to configure Kestrel.
/// </summary>
public static class AccessSettingsHelper
{
    private const string FileName = "access-settings.json";

    /// <summary>Path to the sidecar file that persists bind-mode across restarts.</summary>
    public static string GetSidecarPath(string dataPath) =>
        Path.Combine(dataPath, FileName);

    /// <summary>
    /// Reads the sidecar file and returns <c>true</c> when LAN mode is saved.
    /// Returns <c>false</c> on any error or when the file does not exist.
    /// Safe to call before EF Core is initialised.
    /// </summary>
    public static bool IsLanModeEnabled(string dataPath)
    {
        var path = GetSidecarPath(dataPath);
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            // Minimal parse: look for "bindMode":"Lan" without a full JSON deserialiser
            return json.Contains("\"Lan\"", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Returns private-network IPv4 addresses on this machine.</summary>
    public static IReadOnlyList<string> GetLanAddresses()
    {
        var results = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IsPrivateIpv4(uni.Address))
                        results.Add(uni.Address.ToString());
                }
            }
        }
        catch { /* non-fatal */ }
        return results;
    }

    /// <summary>Builds the list of URLs Kestrel is actually listening on.</summary>
    public static IReadOnlyList<string> BuildListenUrls(BindMode runtimeMode, int port)
    {
        var urls = new List<string> { $"http://127.0.0.1:{port}" };
        if (runtimeMode == BindMode.Lan)
        {
            foreach (var ip in GetLanAddresses())
                urls.Add($"http://{ip}:{port}");
        }
        return urls;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static bool IsPrivateIpv4(IPAddress addr)
    {
        var b = addr.GetAddressBytes();
        return b[0] == 10                                      // 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)     // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254);                   // APIPA 169.254.0.0/16
    }
}

/// <summary>
/// Captures the bind mode and port that Kestrel was actually started with.
/// Registered as a singleton in DI so endpoint handlers can report the
/// live runtime state separately from the <em>saved</em> setting.
/// </summary>
public sealed class RuntimeAccessInfo
{
    public required int      Port     { get; init; }
    public required BindMode BindMode { get; init; }
}
