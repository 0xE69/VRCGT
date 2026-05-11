# VRCGT v2 — Local Storage

## Overview

VRCGT v2 stores all persistent data on the local machine.  Nothing is sent to
a cloud database or remote API beyond the VRChat API calls that the application
explicitly makes on the user's behalf.

---

## Directory layout

| Subdirectory | Purpose |
|---|---|
| `data/` | SQLite database (`vrcgt-v2.db`) and future data files |
| `logs/` | Rotating application log files (plain text) |
| `cache/` | Short-lived API response cache — safe to delete |
| `exports/` | User-initiated CSV / JSON exports |
| `backups/` | Automatic member-list backups |

### Platform root paths

| OS | Root path |
|---|---|
| Windows | `%LocalAppData%\VRCGT\v2` |
| macOS | `~/Library/Application Support/VRCGT/v2` |
| Linux | `$XDG_DATA_HOME/VRCGT/v2` or `~/.local/share/VRCGT/v2` |

### Why `v2/`?

The VRCGT v1 WPF app uses `%LocalAppData%\VRCGT` (no `v2` suffix) for its own
SQLite database.  The `v2` subdirectory keeps the two databases completely
separate, allowing both apps to run in parallel during the migration period.

---

## SQLite database

File: `{root}/data/vrcgt-v2.db`

### Entities

| Table | Description |
|---|---|
| `AppSettings` | Key-value settings store (no secrets) |
| `LocalUsers` | Cached VRChat user identity (no credentials) |
| `ManagedGroups` | VRChat groups the user is managing |
| `AuditLog` | Persisted audit log events fetched from the VRChat API |
| `BackgroundJobs` | Long-running operation records (mass-invite, backup, etc.) |
| `BackgroundJobItems` | Per-item progress rows for a `BackgroundJob` |

### Migrations

VRCGT v2 uses Entity Framework Core code-first migrations.

```sh
# Add a new migration (run from repo root)
dotnet ef migrations add <MigrationName> \
  --project src/VRCGT.Infrastructure \
  --startup-project src/VRCGT.Server

# Apply migrations manually (normally done automatically on startup)
dotnet ef database update \
  --project src/VRCGT.Infrastructure \
  --startup-project src/VRCGT.Server
```

On startup, `ServerAppFactory.CreateAsync()` calls `db.Database.MigrateAsync()`
which creates the database if it does not exist and applies all pending
migrations.  This is intentional during active development.  A future release
will prompt the user before applying destructive schema changes.

---

## Secrets policy

**No secrets are stored in the database or any file in the data directory.**

| Data type | Storage location |
|---|---|
| VRChat session cookie | In-memory only; not persisted to disk |
| VRChat password | Never stored |
| Discord webhook URLs | `AppSettings` table (obfuscated in UI display only) |
| API tokens | Future: `Windows Credential Manager` / `libsecret` / `Keychain` |

The VRChat session cookie is intentionally kept in-memory only.  The user must
log in again when the server restarts.  Persistent session storage will be
addressed in a later phase using platform credential managers, never in a plain
SQLite column.

---

## API endpoint

`GET /api/system/storage` returns the resolved paths and database status:

```json
{
  "appDataPath":    "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2",
  "dataPath":       "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\data",
  "logsPath":       "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\logs",
  "cachePath":      "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\cache",
  "exportsPath":    "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\exports",
  "backupsPath":    "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\backups",
  "databasePath":   "C:\\Users\\Alice\\AppData\\Local\\VRCGT\\v2\\data\\vrcgt-v2.db",
  "databaseExists": true
}
```
