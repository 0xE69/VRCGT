# VRCGT v2 — Packaging & Distribution

## Overview

VRCGT is distributed as a **self-contained, single-folder application**.
End-users do not need .NET, Node.js, npm, or a database server installed.
Everything is bundled inside the artifact directory produced by the build scripts.

The entry point is **`VRCGT.Launcher`** (`VRCGT.Launcher.exe` on Windows).
It starts the embedded ASP.NET Core server in-process and opens the user's
default browser to the local dashboard.

---

## Architecture

```
VRCGT.Launcher (entry point, in-process host)
  └─ VRCGT.Server (ASP.NET Core / Kestrel, in-process)
       ├─ wwwroot/          ← built from VRCGT.Web (React + Vite)
       ├─ VRCGT.Core        ← shared DTOs, constants, interfaces
       └─ VRCGT.Infrastructure  ← EF Core / SQLite, service implementations
```

Because the Launcher hosts the Server **in-process**, there is only one
OS process and no child-process management or IPC is needed.
EF Core migrations run automatically at startup — no manual migration step.

---

## Building

### Prerequisites (build machine only)

| Tool | Minimum version | Purpose |
|------|----------------|---------|
| .NET SDK | 8.0 | Compiles and publishes C# projects |
| Node.js | 18+ | Builds the React frontend |
| npm | 9+ | Installs JS dependencies |

End-users of the artifact need **none** of the above.

### Windows (PowerShell)

```powershell
# From repo root — builds all 4 platforms
.\scripts\build.ps1

# Build only Windows
.\scripts\build.ps1 -Rids win-x64

# Skip frontend rebuild (wwwroot already up-to-date)
.\scripts\build.ps1 -SkipFrontend
```

### Linux / macOS (Bash)

```bash
# From repo root — builds all 4 platforms
./scripts/build.sh

# Build only the current platform
./scripts/build.sh --rids linux-x64

# Skip frontend rebuild
./scripts/build.sh --skip-frontend --rids osx-arm64
```

---

## Publish targets

| RID | Platform |
|-----|----------|
| `win-x64` | Windows 10/11 x64 |
| `linux-x64` | Ubuntu 20.04+, Debian 11+, Fedora 36+ (glibc) |
| `osx-x64` | macOS 12+ on Intel |
| `osx-arm64` | macOS 12+ on Apple Silicon |

> **Cross-compilation note:** `dotnet publish` supports cross-targeting all
> RIDs from any host OS.  However, `PublishReadyToRun=true` (AOT startup
> optimisation) only works when the host OS matches the target RID.  The
> scripts leave it `false` by default to ensure reproducible builds on any CI
> machine.

---

## Artifact layout

```
artifacts/
  win-x64/
    VRCGT.Launcher.exe     ← run this
    wwwroot/               ← static React assets served by Kestrel
    *.dll                  ← bundled .NET runtime + app libraries
    README.txt
    VERSION.txt
  linux-x64/
    VRCGT.Launcher         ← run this (chmod +x first)
    wwwroot/
    *.so / *.a             ← native runtime libraries
    README.txt
    VERSION.txt
  osx-x64/
    VRCGT.Launcher
    wwwroot/
    README.txt
    VERSION.txt
  osx-arm64/
    VRCGT.Launcher
    wwwroot/
    README.txt
    VERSION.txt
```

---

## Running the artifact

### Windows

```
VRCGT.Launcher.exe
```

Or double-click it in Explorer.

### Linux

```bash
chmod +x VRCGT.Launcher
./VRCGT.Launcher
```

### macOS

```bash
chmod +x VRCGT.Launcher
./VRCGT.Launcher
```

> On macOS, Gatekeeper may block the first launch because the binary is not
> notarised.  Right-click → Open → Open to bypass the warning once, or run:
> ```bash
> xattr -d com.apple.quarantine VRCGT.Launcher
> ```

---

## Data storage

User data is stored outside the artifact directory in the OS-standard
application data location, so data persists across updates.

| OS | Path |
|----|------|
| Windows | `%LOCALAPPDATA%\VRCGT\` |
| Linux | `~/.local/share/VRCGT/` |
| macOS | `~/Library/Application Support/VRCGT/` |

Sub-directories created at first startup:

```
VRCGT/
  v2/
    data/
      vrcgt.db             ← SQLite database (EF Core migrations auto-applied)
      access-settings.json ← LAN bind-mode sidecar (Stage 16)
    logs/
    cache/
    exports/
    backups/
```

---

## Database migrations

EF Core migrations are **compiled into the assembly** and applied
automatically at startup via `db.Database.MigrateAsync()` in
`ServerAppFactory.cs`.  No manual migration command is needed in the artifact.

---

## Versioning

The canonical version string lives in `src/VRCGT.Core/Class1.cs`:

```csharp
public const string AppVersion = "2.0.0-alpha.1";
```

The build scripts read this value and embed it in `VERSION.txt` inside each
artifact directory.

---

## CI / automated builds

The scripts can be used in GitHub Actions or any CI pipeline.  A minimal
Windows workflow step:

```yaml
- name: Build VRCGT artifacts
  shell: pwsh
  run: .\scripts\build.ps1 -Rids win-x64,linux-x64 -Configuration Release
```

On a Linux runner:

```yaml
- name: Build VRCGT artifacts
  run: |
    chmod +x scripts/build.sh
    ./scripts/build.sh --rids linux-x64
```
