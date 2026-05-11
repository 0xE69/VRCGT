// ============================================================
// VRCGT.Launcher — local server launcher and browser opener
// ============================================================
// This is the normal user-facing entry point for VRCGT v2.
//
// What it does:
//   1. Finds an available local port (prefers 45721, tries up to 45730).
//   2. Starts VRCGT.Server in-process via ServerAppFactory.Create()
//      — no child process is spawned; server and launcher share one PID.
//   3. Polls GET /health until the server responds HTTP 200.
//   4. Opens the OS default browser to http://127.0.0.1:<port>.
//   5. Blocks until Ctrl+C, SIGTERM, or process exit.
//   6. On exit, cancels the server's CancellationToken for a clean shutdown.
//
// In-process vs child process:
//   In-process hosting is used here. Because VRCGT.Launcher already references
//   VRCGT.Server, there is no IPC complexity. The WebApplication runs as a
//   hosted Task on the thread-pool; the launcher's main thread handles console
//   events. This is the recommended .NET pattern for single-binary local apps.
//   See docs/v2/launcher.md for a full explanation.
// ============================================================

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using VRCGT.Core;
using VRCGT.Server;

// ---- Port selection --------------------------------------------------------
// Prefer 45721; try sequentially up to 45730 if that port is occupied.
// Using a non-default port reduces collisions with other dev services.
// A port above 1024 requires no elevated privileges on any OS.
const int PreferredPort   = 45721;
const int MaxPortAttempts = 10;

int port;
try
{
    port = FindAvailablePort(PreferredPort, MaxPortAttempts);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[VRCGT] ERROR: {ex.Message}");
    return 1;
}

// ---- Data directory --------------------------------------------------------
var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    AppConstants.AppName);
Directory.CreateDirectory(dataDir);

var url = $"http://{AppConstants.DefaultBindAddress}:{port}";

// ---- Console header --------------------------------------------------------
Console.WriteLine();
Console.WriteLine("  ╔══════════════════════════════════════════════╗");
Console.WriteLine($"  ║   VRCGT Local Web Console  {"v" + AppConstants.AppVersion,-16}  ║");
Console.WriteLine("  ╠══════════════════════════════════════════════╣");
Console.WriteLine($"  ║  URL      {url,-35}║");
Console.WriteLine($"  ║  Data     {ShortenPath(dataDir, 35),-35}║");
Console.WriteLine("  ║  Stop     Ctrl+C                             ║");
Console.WriteLine("  ╚══════════════════════════════════════════════╝");
Console.WriteLine();

// ---- Cancellation ----------------------------------------------------------
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // prevent abrupt process kill; we handle shutdown below
    Console.WriteLine("\n  [VRCGT] Shutting down…");
    cts.Cancel();
};

// Also honour SIGTERM / process exit on Linux/macOS (no-op on Windows)
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!cts.IsCancellationRequested) cts.Cancel();
};

// ---- Start server in-process -----------------------------------------------
var webApp     = await ServerAppFactory.CreateAsync(port, args: []);
// IHost.RunAsync(CancellationToken) starts Kestrel and returns a Task that
// completes when the host stops.  WebApplication implements IHost; the overload
// that accepts a CancellationToken is the IHost extension method from
// Microsoft.Extensions.Hosting, not WebApplication.RunAsync(string? url).
var serverTask = ((IHost)webApp).RunAsync(cts.Token);

// ---- Health poll -----------------------------------------------------------
Console.Write("  [VRCGT] Waiting for server to become ready");
using var http  = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
bool serverReady = false;

for (int attempt = 0; attempt < 30 && !cts.IsCancellationRequested; attempt++)
{
    // Abort poll early if the server exited unexpectedly
    if (serverTask.IsCompleted)
    {
        Console.WriteLine();
        Console.Error.WriteLine("  [VRCGT] Server stopped unexpectedly before becoming ready.");
        if (serverTask.IsFaulted)
            Console.Error.WriteLine($"  [VRCGT] Cause: {serverTask.Exception?.GetBaseException().Message}");
        return 1;
    }

    try
    {
        var response = await http.GetAsync($"{url}/health", cts.Token);
        if (response.IsSuccessStatusCode) { serverReady = true; break; }
    }
    catch (HttpRequestException)         { /* server not listening yet — keep polling */ }
    catch (TaskCanceledException)        { break; }

    Console.Write(".");

    try   { await Task.Delay(500, cts.Token); }
    catch (OperationCanceledException) { break; }
}

Console.WriteLine();

if (!serverReady)
{
    if (!cts.IsCancellationRequested)
        Console.Error.WriteLine("  [VRCGT] Server did not become ready within 15 s. Check output above.");
    cts.Cancel();
    try { await serverTask; } catch { /* discard */ }
    return 1;
}

Console.WriteLine($"  [VRCGT] Server ready at {url}");

// ---- Open browser ----------------------------------------------------------
Console.WriteLine($"  [VRCGT] Opening browser…");
OpenBrowser(url);

// ---- Block until shutdown --------------------------------------------------
try
{
    await serverTask;
}
catch (OperationCanceledException)
{
    // Expected: Ctrl+C triggered cts.Cancel() → RunAsync completed
}
catch (Exception ex)
{
    Console.Error.WriteLine($"  [VRCGT] Server error: {ex.Message}");
    return 1;
}

Console.WriteLine("  [VRCGT] Stopped. Goodbye.");
return 0;

// ============================================================================
// Helpers
// ============================================================================

/// <summary>
/// Returns the first TCP port in the range [<paramref name="preferred"/>,
/// <paramref name="preferred"/> + <paramref name="attempts"/> - 1] that is
/// not currently bound on the loopback interface.
/// </summary>
static int FindAvailablePort(int preferred, int attempts)
{
    for (int p = preferred; p < preferred + attempts; p++)
    {
        try
        {
            // Bind a temporary listener; if it succeeds the port is free.
            using var probe = new TcpListener(IPAddress.Loopback, p);
            probe.Start();
            probe.Stop();
            return p;
        }
        catch (SocketException)
        {
            // Port in use — try the next one
        }
    }

    throw new InvalidOperationException(
        $"No available port found in range {preferred}–{preferred + attempts - 1}. " +
        "Close any conflicting processes or set VRCGT_PORT to an explicit port.");
}

/// <summary>
/// Opens <paramref name="url"/> in the OS default browser.
/// On all platforms, <c>UseShellExecute = true</c> delegates to the OS handler.
/// Failures are non-fatal — the URL is printed to the console as a fallback.
/// </summary>
static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine($"  [VRCGT] Could not open browser automatically.");
        Console.WriteLine($"  [VRCGT] Please navigate to: {url}");
    }
}

/// <summary>
/// Truncates a path to fit in <paramref name="maxLen"/> characters,
/// preserving the drive root and trailing segment.
/// </summary>
static string ShortenPath(string path, int maxLen)
{
    if (path.Length <= maxLen) return path;
    const string ellipsis = "…";
    // Keep drive/root (e.g. "C:\") and the last directory segment
    var root = Path.GetPathRoot(path) ?? string.Empty;
    var tail = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                                             Path.AltDirectorySeparatorChar));
    var middle = $"{ellipsis}{Path.DirectorySeparatorChar}{tail}";
    var available = maxLen - root.Length - middle.Length;
    return available > 0
        ? $"{root}{path.Substring(root.Length, available)}{middle}"
        : $"{root}{middle}"[..maxLen];
}


