// VRCGT.Infrastructure — Concrete implementations of VRCGT.Core interfaces.
// This project will contain: EF Core DbContext, repository implementations,
// VRChat API client, SettingsService, CacheService, and all background services.
// Phase 0 placeholder only — real implementations added in later phases.

using System.Runtime.InteropServices;
using VRCGT.Core;

namespace VRCGT.Infrastructure;

/// <summary>
/// Provides application identity and runtime information.
/// Registered as a singleton in VRCGT.Server DI container.
/// </summary>
public sealed class AppInfo : IAppInfo
{
    public string AppName => AppConstants.AppName;

    public string Version => AppConstants.AppVersion;

    public string RuntimeDescription =>
        $".NET {Environment.Version} on {RuntimeInformation.OSDescription}";
}
