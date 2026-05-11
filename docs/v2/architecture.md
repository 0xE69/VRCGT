# VRCGT v2 — Local-First Architecture

**Status:** Phase 0 scaffolding  
**Date:** May 2026  

---

## Overview

VRCGT v2 is a **cross-platform, local-first desktop application** for VRChat group owners and moderators.

Instead of a native WPF window (v1), the user interface is a React web application that runs inside the user's browser — served by an embedded local web server bundled inside the application executable.

---

## Core Principle: Everything Runs Locally

Every component of the application runs on the user's local machine inside the single downloaded binary:

| Component | Where it runs |
|---|---|
| HTTP server (Kestrel) | Inside the VRCGT process |
| REST API | Inside the VRCGT process |
| SignalR real-time hub | Inside the VRCGT process |
| React UI | Served from `wwwroot/` embedded in the executable |
| SQLite database | Local file in OS app data directory |
| Settings (JSON) | Local file in OS app data directory |
| VRChat API session | Managed inside the VRCGT process |
| Background jobs | `IHostedService` instances inside the VRCGT process |
| Discord webhook calls | Outbound HTTP from the VRCGT process |

No cloud backend. No remote server. No separate install. No external service dependency beyond VRChat API and Discord webhooks (both user-configured, both optional to the application functioning).

---

## Default Bind Address: 127.0.0.1

The Kestrel HTTP server binds to `127.0.0.1` (loopback) by default.

```
127.0.0.1:57432   ← only address that receives requests by default
```

This means:
- No other machine on the local network can reach the server.
- No public internet exposure is possible.
- Other processes on the same machine can reach the port — mitigated by a session token mechanism (Phase 1).

---

## LAN Access: Strictly Opt-In

LAN access (allowing other devices on the same Wi-Fi/Ethernet network to use the app) is:

- **Disabled by default.**
- **Never enabled automatically.**
- Enabled only via App Settings → Advanced → "Enable LAN Access" toggle, which requires the user to read and acknowledge a warning dialog.
- When enabled, Kestrel adds a second listener on the specific LAN IP address (e.g., `192.168.1.42:57432`) — NOT on `0.0.0.0` (all interfaces).

Remote/public internet access is never enabled by the application under any circumstances.

See `WEB_LOCAL_APP_MIGRATION_PLAN.md` § Section 12 for full details.

---

## Process Model

```
User launches VRCGT
        │
        ▼
VRCGT.Launcher process starts
        │
        ├── Checks for running instance (server.lock file)
        │   └── If found: opens existing URL in browser, exits
        │
        ├── Starts VRCGT.Server WebApplication (in-process)
        │
        ├── Polls GET /health until HTTP 200
        │
        ├── Opens http://127.0.0.1:57432/?session=<token> in default browser
        │
        └── Shows system tray icon (Windows/macOS) or terminal prompt (Linux)
                │
                └── On Exit: graceful server shutdown
```

---

## Project Structure

```
src/
├── VRCGroupTools/           ← v1 WPF app (maintenance only, do not modify)
├── VRCGT.Core/              ← Domain interfaces and constants (no ASP.NET, no EF)
├── VRCGT.Infrastructure/    ← EF Core, services, VRChat API client (references Core)
├── VRCGT.Server/            ← ASP.NET Core server, API routes, SignalR hub (references Core + Infrastructure)
├── VRCGT.Launcher/          ← Tray host, browser-open, process lifecycle (references Server)
└── VRCGT.Web/               ← React + TypeScript frontend (Vite; output embedded in Server/wwwroot/)
```

### Dependency Graph

```
VRCGT.Core
    ↑
VRCGT.Infrastructure
    ↑
VRCGT.Server  ←── VRCGT.Web (compiled output copied to wwwroot/)
    ↑
VRCGT.Launcher
```

---

## Data Flow: Request Lifecycle

```
Browser (React UI)
    │  HTTP GET /api/v2/groups/{id}/members
    ▼
Kestrel (127.0.0.1:57432)
    │
    ▼
ASP.NET Core Minimal API route handler
    │
    ├── Validates session cookie + CSRF token
    ├── Calls IVRChatApiService (if live data needed)
    │       └── Outbound HTTPS to api.vrchat.cloud
    ├── Queries SQLite via IDbContextFactory<AppDbContext>
    └── Returns JSON response
    │
    ▼
Browser (React updates UI)
```

---

## Real-Time Updates: SignalR

Background jobs (audit log polling, security monitor, badge scanner, etc.) publish events to an internal event bus. The SignalR hub at `/hubs/live` subscribes to these events and pushes them to connected browser clients.

SignalR is an enhancement layer — all data is also available via REST. If the SignalR connection drops, the React UI falls back to polling via TanStack Query.

---

## v1 (WPF) Compatibility

- v1 and v2 can coexist on the same machine.
- v2 uses a `v2/` subdirectory in the app data folder to avoid overwriting v1 data.
- v1 is reference-only during v2 development. No code in `src/VRCGroupTools/` is modified for v2.
- A one-time migration wizard imports v1 data into v2 on first v2 launch.

---

## External Services Contacted

| Service | When | User Control |
|---|---|---|
| `https://api.vrchat.cloud` | Login and continuous session | Required |
| Discord Webhooks (user URLs) | On configured trigger events | Optional — user-configured |
| `https://api.github.com/repos/0xE69/VRCGT/releases/latest` | Startup + daily | Can be disabled in settings |

No telemetry. No analytics. No crash reporting to a remote server.
