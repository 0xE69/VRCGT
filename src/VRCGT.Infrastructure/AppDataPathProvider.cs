using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// Resolves the local application data directory for VRCGT v2.
///
/// Platform paths:
///   Windows : %LocalAppData%\VRCGT\v2          (e.g. C:\Users\Alice\AppData\Local\VRCGT\v2)
///   macOS   : ~/Library/Application Support/VRCGT/v2
///   Linux   : ~/.local/share/VRCGT/v2           (respects $XDG_DATA_HOME)
///
/// Subdirectory layout:
///   data/       — SQLite database and other persistent data files
///   logs/       — Rotating application log files
///   cache/      — Short-lived API response cache (safe to delete)
///   exports/    — User-initiated CSV/JSON exports
///   backups/    — Automatic member-list backups
///
/// Secrets are never stored here — see docs/v2/storage.md.
/// </summary>
public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public string RootPath    { get; }
    public string DataPath    { get; }
    public string LogsPath    { get; }
    public string CachePath   { get; }
    public string ExportsPath { get; }
    public string BackupsPath { get; }
    public string DatabasePath { get; }

    public AppDataPathProvider()
    {
        // VRCGT_DATA_DIR lets Docker / advanced users point all persistent state
        // at a specific directory (e.g. a container volume at /app/data).
        // When set, that directory IS the root AND the data directory — the
        // database, logs, cache, etc. all live inside it directly rather than
        // in sub-paths below a platform-specific parent.
        var envDataDir = Environment.GetEnvironmentVariable("VRCGT_DATA_DIR");
        bool hasEnvOverride = !string.IsNullOrWhiteSpace(envDataDir);

        RootPath = hasEnvOverride
            ? Path.GetFullPath(envDataDir!)
            : ResolveRoot();

        // When the env var is set the caller already chose the exact directory;
        // we use it as DataPath directly so nothing ends up at {dir}/data/data.
        DataPath    = hasEnvOverride ? RootPath : Path.Combine(RootPath, "data");
        LogsPath    = Path.Combine(RootPath, "logs");
        CachePath   = Path.Combine(RootPath, "cache");
        ExportsPath = Path.Combine(RootPath, "exports");
        BackupsPath = Path.Combine(RootPath, "backups");

        // The v2 database lives under data/ and is kept separate from the v1
        // WPF database so the two apps can coexist during the migration period.
        DatabasePath = Path.Combine(DataPath, "vrcgt-v2.db");
    }

    /// <inheritdoc />
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(ExportsPath);
        Directory.CreateDirectory(BackupsPath);
    }

    /// <summary>
    /// Resolves the data directory path without requiring an instance.
    /// Safe to call before the DI container is built (e.g. in ServerAppFactory
    /// to read the LAN-mode sidecar before Kestrel is configured).
    /// </summary>
    public static string GetDefaultDataPath()
    {
        // Honour the same env var as the constructor so the LAN-mode sidecar
        // (read before DI is built in ServerAppFactory) is found in the right place.
        var envDataDir = Environment.GetEnvironmentVariable("VRCGT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDataDir))
            return Path.GetFullPath(envDataDir);

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(localAppData, "VRCGT", "v2", "data");
    }

    // -------------------------------------------------------------------------

    private static string ResolveRoot()
    {
        // Environment.SpecialFolder.LocalApplicationData maps to the correct
        // platform-specific location:
        //   Windows : %LocalAppData%            (C:\Users\<user>\AppData\Local)
        //   macOS   : ~/Library/Application Support
        //   Linux   : $XDG_DATA_HOME or ~/.local/share
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(localAppData, "VRCGT", "v2");
    }
}
