# VRCGT v2 - Local Web Console

> **Alpha Preview - v2.0.0-alpha.1**
>
> This is an early alpha release of the v2 rewrite. It validates the new local-web architecture and ships a working subset of features. It is **not yet a full replacement** for the v1 WPF app. Users who need the full moderation, inviter, security monitor, Discord webhook, backup, or auto-closer feature set should continue using the v1 WPF build.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-informational) ![License](https://img.shields.io/badge/License-MIT-green) ![Build](https://img.shields.io/github/actions/workflow/status/0xE69/VRCGT/build.yml?branch=2.0.0)

---

## What is VRCGT v2?

VRCGT v2 is a **local web application** for VRChat group owners and moderators. It runs a small web server on your own machine and opens in your browser. There is no cloud, no remote account, and no subscription. Your VRChat credentials and group data never leave your machine.

### Architecture

```
Your machine only
+--------------------------------------------------+
|  VRCGT.Launcher.exe                              |
|    +- starts VRCGT.Server (ASP.NET Core/Kestrel) |
|         +- binds to 127.0.0.1:<port> (default)   |
|         +- serves React web UI from wwwroot/      |
|         +- calls VRChat API on your behalf        |
|         +- stores everything in local SQLite DB   |
+--------------------------------------------------+
         ^ browser opens automatically
```

- **Runs locally:** `127.0.0.1` only by default. LAN access is explicit opt-in.
- **No cloud:** no account creation, no remote storage, no telemetry.
- **Cross-platform:** single self-contained executable for Windows, Linux, and macOS.
- **No runtime required:** .NET 8 and all dependencies are bundled.

---

## What works today (v2.0.0-alpha.1)

| Feature | Status |
|---------|--------|
| First-run setup wizard | Done |
| Local user accounts (owner + multi-user, bcrypt passwords) | Done |
| VRChat login + 2FA (TOTP and email code) | Done |
| Group dashboard (info, member count, online count) | Done |
| Members list (browse + search by display name) | Done |
| Audit log (full history, live updates via SignalR) | Done |
| Badge scanner (scan 18+ verification, bulk kick unverified) | Done |
| Kill switch (bulk role removal with snapshot + restore) | Done |
| Server access settings (LAN mode toggle, port config) | Done |
| Local user management (create, edit, permissions) | Done |

---

## What is not yet implemented in v2

Users who rely on any of the following should continue using the **v1 WPF app** (see [v1 Legacy](#v1-legacy-wpf-app-vrcgrouptools) below).

| Feature | Status |
|---------|--------|
| Kick / ban individual members from the UI | Planned |
| Bans list (view + unban) | Planned |
| Group posts (view, create, edit, delete) | Planned |
| Calendar events (create, manage, recurrence) | Planned |
| Instance creator (build + schedule launch links) | Planned |
| Instance inviter (invite users from current instance) | Planned |
| Friend inviter (bulk invite online friends) | Planned |
| Group join requests (approve + reject) | Planned |
| Invite to group (search + send invite) | Planned |
| Member backup and disaster recovery | Planned |
| Discord webhooks (event notifications) | Planned |
| Security monitor (automated threat detection) | Planned |
| Auto Closer (age-gate enforcement) | Planned |
| User search and profile viewer | Planned |
| Auto-update | Planned |
| Game log scanner | Not planned (requires local filesystem access) |

---

## Running packaged builds

### Windows

Download the `win-x64` artifact from the [Releases](../../releases) page, then:

```cmd
VRCGT.Launcher.exe
```

The launcher starts the server, prints the local URL, and opens your default browser automatically. Press `Ctrl+C` to stop.

> **Windows SmartScreen warning:** The binary is not code-signed. Click **More info -> Run anyway**. The app is open source and built by GitHub Actions on a clean runner.

### Linux

```bash
chmod +x ./VRCGT.Launcher
./VRCGT.Launcher
```

### macOS

```bash
chmod +x ./VRCGT.Launcher
./VRCGT.Launcher
```

On first run macOS may quarantine the binary:

```bash
xattr -c ./VRCGT.Launcher
```

Use the `osx-arm64` artifact on Apple Silicon.

---

## Running from source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) with npm
- Git

### Steps

```bash
git clone https://github.com/0xE69/VRCGT.git
cd VRCGT
git checkout 2.0.0

# Build the React frontend once
cd src/VRCGT.Web
npm ci
npm run build
cd ../..

# Start the server + open browser
dotnet run --project src/VRCGT.Launcher
```

If the frontend is already built you can skip the npm steps:

```bash
dotnet run --project src/VRCGT.Launcher
```

---

## Data storage

All data is stored locally. Nothing is sent anywhere except directly to the VRChat API.

| Platform | Path |
|----------|------|
| Windows  | `%LOCALAPPDATA%\VRCGT\v2\` |
| Linux    | `~/.local/share/VRCGT/v2/` |
| macOS    | `~/Library/Application Support/VRCGT/v2/` |

| Subfolder / file | Contents |
|------------------|----------|
| `data/vrcgt-v2.db` | SQLite database (all application data) |
| `logs/`            | Rotating application log files |
| `cache/`           | Short-lived VRChat API response cache |
| `exports/`         | CSV / JSON exports you trigger manually |
| `backups/`         | Member list snapshots |

---

## Security model

- **Localhost-only by default.** The server binds to `127.0.0.1`. No other device on your network can reach it unless you enable LAN mode.
- **LAN mode is opt-in.** Enabling it from Settings -> Server Access requires the `ManageSecurity` permission and a typed confirmation token.
- **No cloud.** Your VRChat session cookie, local user passwords (bcrypt-hashed), and all group data stay on your disk.
- **HttpOnly session cookie.** The browser-to-server auth cookie is `HttpOnly` and `SameSite=Strict` — not accessible from JavaScript.
- **Destructive actions require a confirmation token.** Kick-all, remove-roles, restore-snapshot, and bulk-delete operations all require a `ConfirmationGuard` token in the request body.

---

## Running tests

```bash
dotnet test /p:SkipWebFrontend=true
```

Expected results: 53 tests across 3 projects, all passing.

---

## Building release packages

```powershell
# All 4 platforms
powershell -ExecutionPolicy Bypass -File scripts/build.ps1

# Windows only
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Rids win-x64

# Skip frontend rebuild
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipFrontend
```

Output goes to `artifacts/<rid>/`. Each artifact is fully self-contained — no .NET install required on the target machine.

---

## v1 Legacy WPF app (VRCGroupTools)

The original Windows-only WPF application is still present under `src/VRCGroupTools.csproj` and continues to work on Windows. It provides the full feature set that v2 has not yet implemented.

**v1 is not deprecated.** It will be maintained until v2 reaches full feature parity.

### v1 feature set

| Category | Features |
|----------|----------|
| Authentication | VRChat login with 2FA (TOTP/email), session caching |
| Group dashboard | Info, member counts, online stats, roles, events |
| User search & moderation | Search by name/ID, view profiles, kick, ban/unban, assign roles |
| Invite to group | Search users, preview profile, send invite |
| Inviter Hub | Instance inviter (18+ filter, trust level filter), friend inviter, game log scanner, join requests |
| Calendar events | Create/manage events, recurrence, templates, platform and language tags |
| Group posts | View, create, edit, delete posts |
| Instance creator | Build launch links, schedule instances, region/access type selection |
| Members list | Browse, filter by role, moderation actions |
| Bans list | View all bans, unban with one click |
| Audit logs | Full history, filters, date range, search, auto-refresh, CSV export |
| 18+ Badge scanner | Scan group, kick unverified individually or in bulk, CSV export |
| Kill switch | Bulk role removal with snapshot and restore |
| Member backup | Full member snapshots, smart comparison, selective restore |
| Security monitor | Automated threat detection, configurable thresholds, Discord alerts |
| Auto Closer | Auto-close non-age-gated instances, region restrictions |
| Discord webhooks | Rich embed notifications for all event types |
| App settings | Auto-update, start with Windows, theme, timezone |

To build v1:

```cmd
build.bat
```

Output: `src/bin/Publish/VRCGroupTools.exe`

---

## Project structure

```
VRCGT/
  src/
    VRCGT.Core/          # Domain interfaces, DTOs, AppConstants
    VRCGT.Infrastructure/# EF Core, SQLite, VRChat API client, services
    VRCGT.Server/        # ASP.NET Core API, controllers, SignalR hubs
    VRCGT.Launcher/      # Entry point, Kestrel config, browser launch
    VRCGT.Web/           # React 19 + Vite 6 frontend
    VRCGroupTools.csproj # v1 WPF app (legacy, still maintained)
  tests/
    VRCGT.Core.Tests/
    VRCGT.Infrastructure.Tests/
    VRCGT.Server.Tests/
  scripts/
    build.ps1            # Cross-platform packaging (Windows/Linux/macOS)
    build.sh
  docker/
    Dockerfile
    docker-compose.yml
  docs/v2/               # Architecture and API documentation
```

---

## Contributing

Contributions are welcome. Please open an issue before starting a large feature.

1. Fork the repository
2. Checkout the `2.0.0` branch: `git checkout 2.0.0`
3. Create a feature branch: `git checkout -b feature/my-feature`
4. Commit your changes: `git commit -m "feat: my feature"`
5. Push and open a Pull Request against `2.0.0`

---

## License

MIT - see [LICENSE](LICENSE) for details.

---

## Disclaimer

This project is not affiliated with, endorsed by, or connected to VRChat Inc. VRChat and related marks are trademarks of VRChat Inc. Use in accordance with VRChat's Terms of Service.