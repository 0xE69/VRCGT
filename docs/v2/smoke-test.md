# VRCGT v2 — Runtime Smoke Test

**Last verified:** 2026-05-11 (Run 3 — all clean)  
**Platform:** Windows 10 · .NET 8 · Node 20

This document gives the exact manual steps to verify that VRCGT v2 starts,
serves the web UI, and shuts down cleanly.  Run this after every significant
change to `VRCGT.Server`, `VRCGT.Launcher`, or `VRCGT.Infrastructure`.

---

## Prerequisites

- .NET 8 SDK installed (`dotnet --version` → `8.x.x`)
- Node.js 18+ installed (`node --version`)
- Working directory: repo root (`VRCGT-main/`)
- A port in the range 45721–45730 must be free

---

## 1. One-time build

```powershell
# Build everything including the React frontend
dotnet build VRCGroupTools.sln --configuration Release

# OR — skip frontend (faster if it was already built)
dotnet build VRCGroupTools.sln --configuration Release /p:SkipWebFrontend=true
```

Expected: `Build succeeded` with no errors.

---

## 2. Smoke-test VRCGT.Server (standalone)

### 2.1 Start the server

```powershell
dotnet run --project src\VRCGT.Server --configuration Release /p:SkipWebFrontend=true -- --port 57432
```

**Expected startup output** (abbreviated):
```
Using launch settings from src\VRCGT.Server\Properties\launchSettings.json...
Building...
info: ...Migrations[20405]  No migrations were applied. The database is already up to date.
warn: ...Kestrel[0]  Overriding address(es) 'http://127.0.0.1:57432'. ...
```

> **Note:** The `Overriding address(es)` warning is cosmetic and expected only
> when using `dotnet run` (which loads launchSettings.json). Both the
> launchSettings profile and `--port 57432` resolve to the same address
> (`127.0.0.1:57432`), so the effective bind address is identical. This warning
> does not appear when the Launcher starts the server in-process.

### 2.2 Probe endpoints

In a **second terminal**, run:

```powershell
$base = "http://127.0.0.1:57432"

# GET /  — must return HTTP 200 with text/html (the React SPA)
Invoke-WebRequest "$base/" -UseBasicParsing

# GET /health  — unauthenticated liveness endpoint
Invoke-WebRequest "$base/health" -UseBasicParsing
# Expected body: {"status":"ok","app":"VRCGT","mode":"local"}

# GET /api/v2/version  — runtime info
Invoke-WebRequest "$base/api/v2/version" -UseBasicParsing
# Expected body: {"app":"VRCGT","version":"2.0.0-alpha",...,"lanMode":false}

# GET /api/system/storage  — data directory paths
Invoke-WebRequest "$base/api/system/storage" -UseBasicParsing
# Expected body: {"appDataPath":"...","databaseExists":true,...}
```

All four requests must return HTTP 200.

### 2.3 Stop the server

Press **Ctrl+C** in the server terminal.

Expected shutdown output:
```
Application is shutting down...
```
The process exits with code 0.

---

## 3. Smoke-test VRCGT.Launcher

### 3.1 Start the Launcher

```powershell
dotnet run --project src\VRCGT.Launcher --configuration Release /p:SkipWebFrontend=true
```

**Expected console header:**
```
  ╔══════════════════════════════════════════════╗
  ║   VRCGT Local Web Console  v2.0.0-alpha      ║
  ╠══════════════════════════════════════════════╣
  ║  URL      http://127.0.0.1:45721             ║
  ║  Data     C:\Users\...\AppData\Local\VRCGT   ║
  ║  Stop     Ctrl+C                             ║
  ╚══════════════════════════════════════════════╝
```

**Expected startup sequence:**
1. EF Core migration check runs (no output if DB is current).
2. `[VRCGT] Waiting for server to become ready` appears (with zero or more `.` dots).
3. `[VRCGT] Server ready at http://127.0.0.1:45721` appears within ~5 s.
4. `[VRCGT] Opening browser…` appears and the default browser opens.
5. The browser loads the VRCGT setup or login page.

> If port 45721 is occupied, the Launcher silently moves to 45722, 45723, …
> up to 45730. The URL in the header reflects the actual port chosen.

### 3.2 Verify health while Launcher is running

In a **second terminal**:

```powershell
Invoke-WebRequest "http://127.0.0.1:45721/health" -UseBasicParsing
# Expected: {"status":"ok","app":"VRCGT","mode":"local"}
```

### 3.3 Stop the Launcher

Press **Ctrl+C** in the Launcher terminal.

**Expected shutdown output:**
```
  [VRCGT] Shutting down…
  [VRCGT] Stopped. Goodbye.
```

The process exits with code 0.

---

## 4. Bind-address verification

Confirm the server is NOT accessible from outside the machine:

```powershell
# Must timeout or connection-refuse (not serving on 0.0.0.0)
Invoke-WebRequest "http://0.0.0.0:45721/health" -UseBasicParsing -TimeoutSec 2
```

Expected: connection error (not HTTP 200).

---

## 5. Fresh-database check

Delete the database and restart to verify schema migration:

```powershell
Remove-Item "$env:LOCALAPPDATA\VRCGT\v2\data\vrcgt-v2.db" -Force
dotnet run --project src\VRCGT.Launcher --configuration Release /p:SkipWebFrontend=true
```

Expected: all migrations apply on first start; `/api/system/storage` returns
`"databaseExists": true`.

---

## 6. Known cosmetic output (not failures)

| Message | Cause | Action required |
|---------|-------|-----------------|
| `Overriding address(es) 'http://127.0.0.1:57432'` | `dotnet run` loads launchSettings.json; both sources agree on the same port | None — cosmetic only; does not affect bind address |
| `Rollup: A comment "/*#__PURE__*/" … cannot be interpreted` | SignalR npm package uses annotations Rollup can't parse | None — third-party package; output is correct |
| EF Core SQL `SELECT "MigrationId"` log lines | EF Core migration check on every startup | None — expected; suppress by setting log level to Warning in production |
