using System.Text.Json;
using System.Text.Json.Serialization;
using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// Manages the server bind-mode setting via a lightweight JSON sidecar file
/// (<c>{DataPath}/access-settings.json</c>).
///
/// The sidecar is read before the DI container is built (in ServerAppFactory)
/// to configure Kestrel, so EF Core is intentionally not used here.
/// </summary>
public sealed class AccessSettingsService : IAccessSettingsService
{
    // ── Sidecar JSON model ────────────────────────────────────────────────────

    private sealed record SidecarData(
        [property: JsonPropertyName("bindMode")] string BindMode = "LocalOnly"
    );

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IAppDataPathProvider _paths;
    private readonly RuntimeAccessInfo    _runtime;

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true,
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public AccessSettingsService(
        IAppDataPathProvider paths,
        RuntimeAccessInfo    runtime)
    {
        _paths   = paths;
        _runtime = runtime;
    }

    // ── IAccessSettingsService ────────────────────────────────────────────────

    public Task<AccessSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var savedMode = ReadSaved();
        return Task.FromResult(Build(savedMode));
    }

    public Task<AccessSettingsDto> UpdateAsync(BindMode newMode, CancellationToken ct = default)
    {
        WriteSaved(newMode);
        return Task.FromResult(Build(newMode));
    }

    public IReadOnlyList<string> GetLanAddresses() =>
        AccessSettingsHelper.GetLanAddresses();

    // ── Private helpers ───────────────────────────────────────────────────────

    private AccessSettingsDto Build(BindMode savedMode)
    {
        var listenUrls  = AccessSettingsHelper.BuildListenUrls(_runtime.BindMode, _runtime.Port);
        var lanAddrs    = AccessSettingsHelper.GetLanAddresses();
        var needRestart = savedMode != _runtime.BindMode;

        return new AccessSettingsDto(
            SavedBindMode:   savedMode.ToString(),
            RuntimeBindMode: _runtime.BindMode.ToString(),
            Port:            _runtime.Port,
            RequiresRestart: needRestart,
            ListenUrls:      listenUrls,
            LanAddresses:    lanAddrs);
    }

    private BindMode ReadSaved()
    {
        var file = AccessSettingsHelper.GetSidecarPath(_paths.DataPath);
        if (!File.Exists(file)) return BindMode.LocalOnly;
        try
        {
            var data = JsonSerializer.Deserialize<SidecarData>(
                File.ReadAllText(file), s_jsonOpts);
            return Enum.TryParse<BindMode>(data?.BindMode, out var m) ? m : BindMode.LocalOnly;
        }
        catch { return BindMode.LocalOnly; }
    }

    private void WriteSaved(BindMode mode)
    {
        Directory.CreateDirectory(_paths.DataPath);
        var file = AccessSettingsHelper.GetSidecarPath(_paths.DataPath);
        var json = JsonSerializer.Serialize(new SidecarData(mode.ToString()), s_jsonOpts);
        File.WriteAllText(file, json);
    }
}
