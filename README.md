# VRCGT v2 â€” Local Web Console

> **âš ï¸ Alpha Preview â€” v2.0.0-alpha.1**
>
> This is an early alpha release of the v2 rewrite. It validates the new local-web architecture and ships a working subset of features. It is **not yet a full replacement** for the v1 WPF app. See [What is not complete yet](#what-is-not-complete-yet) and [FEATURE_PARITY_V1_TO_V2.md](FEATURE_PARITY_V1_TO_V2.md) for the full picture.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-informational) ![License](https://img.shields.io/badge/License-MIT-green) ![Build](https://img.shields.io/github/actions/workflow/status/0xE69/VRCGT/build.yml?branch=main)

---

## What is VRCGT v2?

VRCGT v2 is a **local web application** for VRChat group owners and moderators. It runs a small web server on your own machine and opens in your browser. There is no cloud, no remote account, and no subscription â€” your VRChat credentials and group data never leave your machine.

### Architecture

```
Your machine only
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚  VRCGT.Launcher.exe                              â”‚
â”‚    â””â”€ starts VRCGT.Server (ASP.NET Core / Kestrel)â”‚
â”‚         â”œâ”€ binds to 127.0.0.1:<port>  (default)  â”‚
â”‚         â”œâ”€ serves React web UI from wwwroot/      â”‚
â”‚         â”œâ”€ calls VRChat API on your behalf        â”‚
â”‚         â””â”€ stores everything in local SQLite DB   â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
         â†‘ browser opens automatically
```

- **Runs locally:** `127.0.0.1` only by default. LAN access is an explicit opt-in.
- **No cloud:** no account creation, no remote storage, no telemetry.
- **Cross-platform:** single self-contained executable for Windows, Linux, and macOS.
- **No runtime required:** .NET 8 and all dependencies are bundled.

---

## What works today (v2.0.0-alpha.1)

| Feature | Status |
|---------|--------|
| Local user accounts (owner + multi-user) | âœ… Done |
| First-run setup wizard | âœ… Done |
| VRChat login + 2FA | âœ… Done |
| Group dashboard (info + member count) | âœ… Done |
| Members list (view + search) | âœ… Done |
| Audit log (view + live push via SignalR) | âœ… Done |
| Badge scanner (scan + kick unverified) | âœ… Done |
| Kill switch (bulk role removal + restore) | âœ… Done |
| Server access settings (LAN mode toggle) | âœ… Done |
| Local user management (permissions, roles) | âœ… Done |

---

## What is not complete yet

The following features from v1 are tracked but not yet implemented in v2. Users who need them should continue using the v1 WPF app (see [Legacy v1 app](#legacy-v1-wpf-app-vrcgrouptools) below).

| Feature | Status |
|---------|--------|
| Kick / ban individual members from UI | ðŸ“‹ Planned |
| Bans list | ðŸ“‹ Planned |
| Group posts | ðŸ“‹ Planned |
| Calendar events | ðŸ“‹ Planned |
| Instance creator | ðŸ“‹ Planned |
| Instance inviter / Friend inviter | ðŸ“‹ Planned |
| Group join requests | ðŸ“‹ Planned |
| Member backup & recovery | ðŸ“‹ Planned |
| Discord webhooks | ðŸ“‹ Planned |
| Security monitor | ðŸ“‹ Planned |
| Auto Closer | ðŸ“‹ Planned |
| User search | ðŸ“‹ Planned |
| Auto-update | ðŸ“‹ Planned |
| Game log scanner | â›” Not planned (requires local filesystem access) |

Full parity table: [FEATURE_PARITY_V1_TO_V2.md](FEATURE_PARITY_V1_TO_V2.md)

---

## Running packaged builds

### Windows

```cmd
:: Download win-x64 artifact from the release page, then:
VRCGT.Launcher.exe
```

The launcher starts the server, prints the URL, and opens your default browser automatically. Press `Ctrl+C` to stop.

> **First launch:** Windows SmartScreen may show "Windows protected your PC" because the binary is not code-signed. Click **More info â†’ Run anyway**. The app is open source and built by GitHub Actions on a clean runner.

### Linux

```bash
chmod +x ./VRCGT.Launcher
./VRCGT.Launcher
```

### macOS

```bash
chmod +x ./VRCGT.Launcher
./VRCGT.Launcher
# Or on Apple Silicon:
./VRCGT.Launcher  # use the osx-arm64 artifact
```

macOS may require: `xattr -c ./VRCGT.Launcher` to remove the quarantine attribute on first run.

---

## Running from source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) and npm (for the frontend)
- Git

### Steps

```bash
git clone https://github.com/0xE69/VRCGT.git
cd VRCGT

# Build the React frontend
cd src/VRCGT.Web
npm ci
npm run build
cd ../..

# Run the launcher (starts server + opens browser)
dotnet run --project src/VRCGT.Launcher
```

### Skip frontend rebuild (if you already built it)

```bash
dotnet run --project src/VRCGT.Launcher /p:SkipWebFrontend=true
```

---

## Data storage

All data is stored locally. No data is sent anywhere except the VRChat API.

| Platform | Path |
|----------|------|
| Windows | `%LOCALAPPDATA%\VRCGT\v2\` |
| Linux | `~/.local/share/VRCGT/v2/` |
| macOS | `~/Library/Application Support/VRCGT/v2/` |

Inside that directory:

| Folder / file | Contents |
|---------------|----------|
| `data/vrcgt-v2.db` | SQLite database (all app data) |
| `logs/` | Rotating application log files |
| `cache/` | Short-lived API response cache |
| `exports/` | CSV / JSON exports you trigger |
| `backups/` | Member list backups |

---

## Security model

- **Localhost-only by default.** The server binds to `127.0.0.1:<port>`. It is not reachable from any other device on your network.
- **LAN mode is opt-in.** You can enable `0.0.0.0` binding from Settings â†’ Server Access. This requires the `ManageSecurity` permission and an explicit typed-confirmation token.
- **No cloud.** There is no remote component. Your VRChat session cookie, local user passwords (bcrypt-hashed), and group data stay on your disk.
- **Session cookie.** The browser-to-server auth uses an `HttpOnly`, `SameSite=Strict` cookie. It is not accessible from JavaScript.
- **Destructive actions require confirmation.** Kick-all, remove-roles, restore-backup, and delete-group all require a `ConfirmationGuard` token in the request body to prevent accidental or scripted calls.

---

## Running tests

```bash
dotnet test /p:SkipWebFrontend=true
```

Expected output:

```
Passed!  - Failed: 0, Passed: 13, Skipped: 0  â€” VRCGT.Core.Tests
Passed!  - Failed: 0, Passed: 28, Skipped: 0  â€” VRCGT.Infrastructure.Tests
Passed!  - Failed: 0, Passed: 12, Skipped: 0  â€” VRCGT.Server.Tests
```

See [TESTING.md](TESTING.md) for details.

---

## Building release packages

```powershell
# All 4 platforms (requires PowerShell; use -ExecutionPolicy Bypass if needed)
powershell -ExecutionPolicy Bypass -File scripts/build.ps1

# Windows only
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Rids win-x64

# Skip frontend (if already built)
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipFrontend
```

Output goes to `artifacts/<rid>/`. Each artifact is self-contained (no .NET install required).

See [PACKAGING_AUDIT.md](PACKAGING_AUDIT.md) for detailed packaging notes.

---

## Creating a GitHub release

1. Ensure `AppConstants.AppVersion` in `src/VRCGT.Core/Class1.cs` matches the tag.
2. Build all 4 RIDs: `scripts/build.ps1`
3. Create and push the tag:
   ```bash
   git tag v2.0.0-alpha.1
   git push origin v2.0.0-alpha.1
   ```
4. GitHub Actions will build, test, and attach artifacts to the release automatically.
5. Edit the release description using [ALPHA_RELEASE_NOTES.md](ALPHA_RELEASE_NOTES.md) as a template.

---

## Legacy v1 WPF app (VRCGroupTools)

The original v1 Windows-only WPF application is still present in this repository under `src/VRCGroupTools.csproj`. It continues to work on Windows and provides features not yet implemented in v2 (see the table above).

**v1 is not deprecated.** It will continue to be maintained until v2 reaches full feature parity. The two apps share no code and use separate data directories.

To build v1:

```cmd
build.bat
```

Output: `src/bin/Publish/VRCGroupTools.exe`

---

## Contributing

Contributions are welcome. Please open an issue before starting a large feature.

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Commit your changes: `git commit -m 'Add my-feature'`
4. Push and open a Pull Request

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

## Disclaimer

This project is not affiliated with, endorsed by, or connected to VRChat Inc. VRChat and related marks are trademarks of VRChat Inc. Use in accordance with VRChat's Terms of Service.
