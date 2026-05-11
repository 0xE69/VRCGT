# VRCGT v2.0.0-alpha.1 — Alpha Release Notes

**Release type:** Alpha preview  
**Date:** 2025  
**Tag:** `v2.0.0-alpha.1`

---

## What this release is

This is the first alpha preview of **VRCGT v2**, a ground-up rewrite of the VRChat Group Tools application.

v2 replaces the Windows-only WPF desktop app with a **local web application**: a self-contained server process that binds to `127.0.0.1` on your machine and serves a React-based UI in your browser. There is no cloud component, no remote account, and no telemetry. All your data stays on your machine.

This alpha exists to:

1. Validate that the new local-web architecture works reliably across platforms.
2. Deliver the working subset of features to early adopters for feedback.
3. Establish the foundation (auth, DB, API layer, packaging pipeline) for completing the remaining feature modules.

---

## What works in this alpha

The following features are implemented and tested:

| Feature | Notes |
|---------|-------|
| Local user accounts | Owner + multi-user, bcrypt passwords, role-based permissions |
| First-run setup wizard | Creates owner account and links to VRChat account |
| VRChat login + 2FA | TOTP and email code support |
| Group dashboard | Group info, member count, online count, group ID |
| Members list | Browse all members, search by display name |
| Audit log | Full history, live updates via SignalR, per-event-type filter |
| Badge scanner | Scan group for 18+ verification, kick unverified in bulk |
| Kill switch | Bulk role removal with snapshot + restore |
| Server access settings | LAN mode toggle (off by default), port configuration |
| Local user management | Create/edit/delete local users, permission management |

**7 of 25 v1 feature modules** have reached parity. See [FEATURE_PARITY_V1_TO_V2.md](FEATURE_PARITY_V1_TO_V2.md) for the full breakdown.

---

## What is NOT complete yet

The following features from the v1 WPF app are **not yet implemented** in v2:

- Kick / ban individual members from the UI
- Bans list
- Group posts (view / create / edit / delete)
- Calendar events
- Instance creator
- Instance inviter / Friend inviter
- Group join requests
- Member backup & recovery
- Discord webhooks
- Security monitor (automated threat detection)
- Auto Closer (age-gate enforcement)
- User search
- Auto-update

See [FEATURE_PARITY_V1_TO_V2.md](FEATURE_PARITY_V1_TO_V2.md) for status and planned order.

---

## Who should stay on v1 WPF for now

If you rely on any of the following, **continue using the v1 WPF app** (`VRCGroupTools.exe`):

- Kicking or banning individual members
- Managing group bans list
- Creating group posts or calendar events
- Instance inviter or friend inviter
- Join request processing
- Member backup and disaster recovery
- Security monitor / threshold alerts
- Auto Closer for age-gated instances
- Discord webhook notifications

The v1 WPF application is **not deprecated**. It continues to be available in this repository and will be maintained until v2 reaches full feature parity.

---

## Known alpha limitations

- **Binary is unsigned.** Windows SmartScreen will warn on first launch. Click **More info → Run anyway**. The binary is built by GitHub Actions on a clean runner; full source is available.
- **No auto-update.** Update by downloading the new release artifact and replacing the executable.
- **Feature parity is 7/25.** This is intentional for an alpha — the architecture milestone is the focus.
- **No installer.** Distributed as a zip archive containing a single self-contained executable.
- **LAN mode requires confirmation.** Enabling network-wide access requires the `ManageSecurity` permission and an explicit token to prevent accidental exposure.

---

## Platforms

| Platform | Artifact | Status |
|----------|----------|--------|
| Windows x64 | `win-x64` | Tested |
| Linux x64 | `linux-x64` | Built, limited testing |
| macOS x64 | `osx-x64` | Built, limited testing |
| macOS ARM64 | `osx-arm64` | Built, limited testing |

---

## How to run

### Packaged build (recommended)

```
# Windows
VRCGT.Launcher.exe

# Linux / macOS
chmod +x ./VRCGT.Launcher
./VRCGT.Launcher
```

### From source

```bash
cd src/VRCGT.Web && npm ci && npm run build && cd ../..
dotnet run --project src/VRCGT.Launcher
```

See [README.md](README.md) for full instructions.

---

## Testing

53 unit and integration tests pass across all three test projects:

```
dotnet test /p:SkipWebFrontend=true
```

---

## Feedback

Please open a [GitHub issue](../../issues) for bugs, crashes, or unexpected behavior. Include your OS, the launcher version (shown in the browser UI footer), and steps to reproduce.
