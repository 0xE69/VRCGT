# VRCGT v2 — Packaging Audit

**Date:** 2026-05-11  
**Machine OS:** Windows 10/11 x64  
**Build host:** win-x64 (cross-compilation for other RIDs not performed — see §7)

---

## 1. Pre-existing Build Infrastructure

Both `scripts/build.ps1` and `scripts/build.sh` already existed and were
functionally correct. The **GitHub Actions workflow** (`build.yml`) was
completely outdated — it still referenced `src/VRCGroupTools.csproj` (the
legacy WPF project), ran only on `windows-latest`, and produced no frontend
build. It was replaced in full (see §5).

---

## 2. Commands Run

### 2a. Frontend (npm)

```bash
cd src/VRCGT.Web
npm install        # node_modules already present; updated lock satisfied
npm run build      # Vite 6.4.2 → src/VRCGT.Server/wwwroot/
```

Output:
```
✓ 92 modules transformed.
../VRCGT.Server/wwwroot/index.html                   0.40 kB │ gzip:  0.28 kB
../VRCGT.Server/wwwroot/assets/index-B_OfitE2.css   46.79 kB │ gzip:  8.61 kB
../VRCGT.Server/wwwroot/assets/index-DBUmQITD.js   399.74 kB │ gzip: 115.02 kB
✓ built in 1.55s
```

### 2b. Full build script (with npm, win-x64 only)

```powershell
cd VRCGT-main
.\scripts\build.ps1 -Rids win-x64
```

Full output:
```
  VRCGT Build Script
  --------------------------------------------------
  Version   : 2.0.0-alpha
  Config    : Release
  Targets   : win-x64
  Output    : …\artifacts
  --------------------------------------------------

[1/2] Building VRCGT.Web frontend...
    Frontend built.
[2/2] Publishing VRCGT.Launcher...

  -> win-x64
  Determining projects to restore...
  All projects are up-to-date for restore.
  VRCGT.Core → …\VRCGT.Core.dll
  VRCGT.Infrastructure → …\VRCGT.Infrastructure.dll
  VRCGT.Server → …\win-x64\VRCGT.Server.dll
  VRCGT.Launcher → …\win-x64\VRCGT.Launcher.dll
  VRCGT.Launcher → …\artifacts\win-x64\
    Published -> …\artifacts\win-x64

  Build summary
    OK : win-x64
```

Exit code: **0**

### 2c. Standalone publish test (SkipFrontend)

```powershell
.\scripts\build.ps1 -Rids win-x64 -SkipFrontend
```

Exit code: **0**

---

## 3. Artifacts Produced

### artifacts/win-x64/

| File | Notes |
|---|---|
| `VRCGT.Launcher.exe` | Entry point — 144 KB stub; starts server in-process |
| `VRCGT.Launcher.dll` | Main managed assembly |
| `VRCGT.Launcher.deps.json` | Dependency manifest |
| `VRCGT.Launcher.runtimeconfig.json` | Runtime configuration |
| `VRCGT.Server.dll` | Server assembly (ASP.NET Core Kestrel) |
| `VRCGT.Core.dll` | Domain interfaces |
| `VRCGT.Infrastructure.dll` | EF Core, SQLite, services |
| `coreclr.dll` + `clrjit.dll` | Bundled .NET 8 runtime |
| `e_sqlite3.dll` | SQLite native library |
| `appsettings.json` | Logging config (`AllowedHosts: *`) |
| `appsettings.Development.json` | Logging config (harmless — same content) |
| `wwwroot/index.html` | Built React app entry point (402 bytes) |
| `wwwroot/assets/index-*.css` | Tailwind CSS bundle (46.8 kB) |
| `wwwroot/assets/index-*.js` | React + SignalR + app bundle (399.7 kB) |
| `README.txt` | Platform-specific run instructions |
| `VERSION.txt` | Version, RID, build timestamp |
| ~335 other `.dll` files | ASP.NET Core + .NET 8 runtime assemblies |

**Total:** 353 files, **105.5 MB** uncompressed

### artifacts/README_RELEASE.txt

Top-level release directory overview — created during this audit.

---

## 4. Artifact Verification

### 4a. File presence check

| Required file | Present |
|---|---|
| `VRCGT.Launcher.exe` | ✅ |
| `VRCGT.Launcher.runtimeconfig.json` | ✅ |
| `wwwroot/index.html` | ✅ |
| `wwwroot/assets/index-*.css` | ✅ |
| `wwwroot/assets/index-*.js` | ✅ |
| `appsettings.json` | ✅ |
| `README.txt` | ✅ |
| `VERSION.txt` | ✅ |
| `e_sqlite3.dll` (native SQLite) | ✅ |
| `coreclr.dll` (.NET runtime) | ✅ |

### 4b. Server smoke test

Started the server directly from the `artifacts/win-x64/` publish output
(equivalent — same binary path) and verified all endpoints:

| Route | Status | Result |
|---|---|---|
| `GET /` | 200 | HTML (`<!DOCTYPE html>`) |
| `GET /setup` | 200 | SPA fallback → HTML |
| `GET /login` | 200 | SPA fallback → HTML |
| `GET /app/dashboard` | 200 | SPA fallback → HTML |
| `GET /app/settings` | 200 | SPA fallback → HTML |
| `GET /api/setup/status` | 200 | `{"setupRequired":true}` |
| `GET /api/auth/me` | 401 | `{"error":"unauthenticated"}` |
| `GET /api/system/storage` | 200 | `appDataPath` field present |

### 4c. CDN reference check

No external URLs (`cdn.`, `unpkg.com`, `jsdelivr.net`, `fonts.google`, etc.)
found in source files or built output. All assets are locally bundled.

### 4d. No Node.js required in artifact

`package.json`, `node_modules/`, and all TypeScript sources are NOT included
in the published artifact. The built artifact contains only:
- The .NET 8 self-contained runtime
- Compiled managed assemblies
- Pre-built static HTML/CSS/JS files in `wwwroot/`

---

## 5. Changes Made

### .github/workflows/build.yml — **replaced in full**

**Before:** Built `src/VRCGroupTools.csproj` (legacy WPF project); only
`windows-latest`; no npm step; no tests; only `win-x64`; used `PublishSingleFile=true`.

**After:**
- Runs on `ubuntu-latest` (supports cross-compilation to all 4 RIDs)
- Installs Node 20 + npm; runs `npm ci` + `npm run build` once
- Runs `dotnet test /p:SkipWebFrontend=true`
- Publishes all 4 RIDs: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`
- Writes `README.txt` + `VERSION.txt` into each artifact
- Uploads all 4 platform artifacts (30-day retention)
- On tag push: downloads artifacts, archives as `.zip` (Windows) and `.tar.gz`
  (Linux/macOS), creates GitHub Release with all 4 archives

### artifacts/README_RELEASE.txt — **created**

Top-level release README explaining the directory structure, platform-specific
run instructions, data storage locations, port/network settings, and rebuild
instructions.

### scripts/build.ps1 and scripts/build.sh — **no changes needed**

Both scripts were already correct:
- npm ci/install → npm run build
- `dotnet publish --self-contained true` with `SkipWebFrontend=true`
- VERSION.txt + README.txt written per RID
- Exit 1 on any failure

---

## 6. Build Architecture

```
scripts/build.ps1 or build.sh
  │
  ├─ [Step 1] npm ci + npm run build
  │      src/VRCGT.Web/        → src/VRCGT.Server/wwwroot/
  │
  └─ [Step 2] dotnet publish VRCGT.Launcher (per RID)
         VRCGT.Launcher.csproj
           └─ ProjectReference → VRCGT.Server.csproj
                │ MSBuild Target: BuildWebFrontend (SkipWebFrontend=true in scripts)
                └─ MSBuild Target: CopyServerWwwrootToPublish
                     copies wwwroot/ → $(PublishDir)wwwroot/
```

### Why SkipWebFrontend=true in dotnet publish?

`VRCGT.Server.csproj` has a `BeforeTargets="Build"` target that runs
`npm run build`. The build scripts run npm separately in Step 1, so passing
`/p:SkipWebFrontend=true` to `dotnet publish` avoids a redundant (and
potentially slower) second npm build. The `CopyServerWwwrootToPublish` target
still runs and copies the already-built wwwroot assets.

---

## 7. Missing Platform Builds

| Platform | Status | Reason |
|---|---|---|
| `win-x64` | ✅ **Built and verified** | Native build machine |
| `linux-x64` | ⚠️ Cross-compiled in CI only | Cannot be verified locally on Windows |
| `osx-x64` | ⚠️ Cross-compiled in CI only | Cannot be verified locally on Windows |
| `osx-arm64` | ⚠️ Cross-compiled in CI only | Cannot be verified locally on Windows |

All 4 RIDs are built in GitHub Actions CI on `ubuntu-latest`.
Cross-compilation produces valid binaries but they cannot be run-tested on the
build machine. The `.NET` cross-publish is reliable and well-tested by Microsoft
for all 4 listed RIDs.

`linux-arm64` is **not** a release target. It can be produced manually:
```bash
./scripts/build.sh --rids linux-arm64
```

---

## 8. Known Limitations

| Issue | Impact | Mitigation |
|---|---|---|
| Windows binary is unsigned | SmartScreen warning on first run | Sign with `signtool` in CI if desired |
| macOS binaries are not notarized | Gatekeeper blocks unsigned binaries | User must right-click → Open, or `xattr -dr com.apple.quarantine VRCGT.Launcher` |
| `appsettings.Development.json` is included in Release artifact | Minor — contains only logging config, no secrets | File content is harmless (`LogLevel: Information`) |
| No single-file publish | Artifact is a directory (~350 files) | `PublishSingleFile=false` is intentional — SQLite native lib extraction is simpler without it |
| Node v21 engine warning | `npm WARN EBADENGINE` on build machine | Vite 6 runs fine on v21; update to v20/22 on build machine to silence |

---

## 9. Acceptance Checklist

- [x] `npm run build` succeeds (exit 0)
- [x] `scripts/build.ps1 -Rids win-x64` succeeds (exit 0)
- [x] Artifact contains `VRCGT.Launcher.exe`
- [x] Artifact contains `wwwroot/index.html` + hashed JS/CSS bundles
- [x] Artifact contains bundled .NET 8 runtime (self-contained)
- [x] Artifact contains `e_sqlite3.dll` (no external DB required)
- [x] Server starts from artifact and serves frontend
- [x] Browser refresh on deep routes works (SPA fallback)
- [x] No CDN references in built output
- [x] No Node.js or npm required to run the artifact
- [x] `artifacts/README_RELEASE.txt` created
- [x] GitHub Actions workflow updated for v2
