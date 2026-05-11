# VRCGT v2 — Launcher

## What the Launcher does

`VRCGT.Launcher` is the normal user-facing entry point for VRCGT v2.  When
launched (double-click, shortcut, or `dotnet run`), it:

1. **Finds an available local port** — starting at the preferred port 45721 and
   trying sequentially up to 45730 (inclusive).
2. **Starts the ASP.NET Core server in-process** via `ServerAppFactory.Create()`.
   No child process is spawned.  The Kestrel server runs as an async `Task` on
   the .NET thread-pool inside the same OS process.
3. **Polls `GET /health`** in a loop (500 ms interval, 15 s total) until the
   server returns HTTP 200.
4. **Opens the OS default browser** to `http://127.0.0.1:<port>` using
   `Process.Start({ UseShellExecute = true })`.
5. **Blocks** in the main thread until a shutdown signal is received.
6. **Shuts down cleanly**: Ctrl+C (or SIGTERM on Unix) cancels a shared
   `CancellationToken` that is passed to `WebApplication.RunAsync()`, causing
   Kestrel to drain and stop.

---

## Why everything is bound to localhost

The server binds exclusively to `127.0.0.1` (IPv4 loopback).  This is an
intentional security constraint:

- **No network exposure by default.** Other devices on the same LAN, the
  internet, or a VPN cannot reach the server unless the user explicitly enables
  LAN mode in settings.
- **No firewall prompts.** Operating systems do not prompt for firewall rules
  when a process binds only to the loopback interface.
- **No ambient authority risk.** Cookies/sessions cannot be hijacked by a
  device that is not the local machine.

Binding is performed in `ServerAppFactory`:

```csharp
builder.WebHost.UseKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);   // 127.0.0.1 only — never 0.0.0.0
});
```

LAN mode (future, opt-in only) will add a **second** explicit binding to the
specific local LAN IP address chosen by the user — never a wildcard `0.0.0.0`
binding.  See `WEB_LOCAL_APP_MIGRATION_PLAN.md § Section 12`.

---

## How ports are selected

| Step | Detail |
|------|--------|
| Preferred port | **45721** (chosen to avoid common dev-tool ports and the v1 WPF app's default) |
| Fallback range | 45722 – 45730 (tried in order if 45721 is occupied) |
| Detection method | Attempt to `TcpListener.Start()` on `127.0.0.1:<port>`. If it succeeds the port is free; the listener is immediately stopped. |
| All taken | `InvalidOperationException` is thrown — the launcher prints an error and exits with code 1. |
| Override | Set `VRCGT_PORT=<n>` env var **or** pass `--port <n>` to bypass port discovery entirely. This is currently wired in the standalone `VRCGT.Server/Program.cs` but will be plumbed into the Launcher in Phase 1. |

### Why not use ephemeral port 0?

Port 0 asks the OS to assign any free ephemeral port.  That is fine for
networking but makes it impossible to have a stable bookmarkable URL or to
detect that a server is already running by checking a known port.  The fixed
range 45721–45730 gives stability while still handling the case where the
preferred port is occupied by another application.

---

## In-process vs child-process hosting

VRCGT.Launcher uses **in-process hosting**: `ServerAppFactory.Create()` returns
a `WebApplication` that is started as `await webApp.RunAsync(ct)` in the same
process.

| Aspect | In-process (chosen) | Child process (rejected) |
|--------|--------------------|-----------------------|
| Complexity | Low — shared DI, no IPC | High — need pipes or sockets |
| Start-up time | Faster — no new process | Slower — OS process spawn |
| Error propagation | Direct — faulted Task | Indirect — parse stderr |
| Single-binary publish | Simple | Requires bundling two EXEs |
| Port hand-off | Trivial — Launcher picks port before Create() | Requires reading stdout |

The only downside of in-process is that a crash in the server kills the
launcher.  This is acceptable (and actually desirable — the user sees the error
immediately in the terminal/event log rather than silently losing the server
process).

---

## Future tray icon plan

The terminal-based launcher is intentional for Stage 3.  Platform-specific tray
icons are planned for a later phase:

| Platform | Technology | Notes |
|----------|-----------|-------|
| Windows | `System.Windows.Forms.NotifyIcon` | Requires `net8.0-windows` target in a separate `VRCGT.Launcher.Windows` project or conditional `<TargetFramework>` |
| macOS | `NSStatusItem` via `AppKit` | Available through .NET / NativeAOT interop |
| Linux | None — terminal only | `--no-browser` flag for headless/server use |

When tray support is added:
- The console window will be hidden (Windows: `<OutputType>WinExe</OutputType>`).
- The tray menu will expose: Open Browser, Data Folder, About, Exit.
- The existing `CancellationTokenSource` shutdown path is unchanged; tray Exit
  will simply call `cts.Cancel()`.
- A `server.lock` file (written on start, deleted on stop) will allow detecting
  a running instance so a second launch opens the browser instead of starting
  a second server.

---

## Data directory

The data directory is `%LOCALAPPDATA%\VRCGT` (Windows) /
`~/.local/share/VRCGT` (Linux/macOS).  It is printed to the console on startup.
In Phase 1 the SQLite database, session cache, and settings file will be stored
here.

---

## Running

```sh
# Normal launch (picks port, starts server, opens browser)
dotnet run --project src/VRCGT.Launcher

# Run server standalone (no browser — useful for CI / remote dev)
dotnet run --project src/VRCGT.Server

# Run server on a specific port
dotnet run --project src/VRCGT.Server -- --port 8080
```
