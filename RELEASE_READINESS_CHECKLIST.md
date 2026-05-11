# VRCGT v2 — Release Readiness Checklist

**Version:** <!-- e.g. 2.0.0 -->  
**Release date:** <!-- YYYY-MM-DD -->  
**Checked by:** <!-- name / handle -->  
**Branch/commit:** <!-- git rev-parse --short HEAD -->

Mark every item ✅ before tagging the release.  
Items marked ⚠️ require a written explanation in the Notes column.

---

## 1. Build

> Commands: `dotnet restore`, `dotnet build`, `dotnet test`, `npm run build`, `scripts/build.ps1`

| # | Item | Status | Notes |
|---|------|--------|-------|
| 1.1 | `dotnet restore VRCGroupTools.sln` — exits 0, no errors | ☐ | |
| 1.2 | `dotnet build VRCGroupTools.sln -c Release /p:SkipWebFrontend=true` — exits 0, **0 errors** | ☐ | |
| 1.3 | Build warning count is equal to or less than the baseline (14 nullable warnings as of 2026-05-11) | ☐ | |
| 1.4 | `dotnet test VRCGroupTools.sln /p:SkipWebFrontend=true` — all 53 tests pass, 0 failures | ☐ | |
| 1.5 | `cd src/VRCGT.Web && npm ci` — exits 0 | ☐ | |
| 1.6 | `npm run build` (in `src/VRCGT.Web`) — exits 0; `src/VRCGT.Server/wwwroot/` contains `index.html` + hashed `.js`/`.css` assets | ☐ | |
| 1.7 | `scripts/build.ps1` (all 4 RIDs: `win-x64 linux-x64 osx-x64 osx-arm64`) — all exit 0; `artifacts/<rid>/` directories populated | ☐ | |
| 1.8 | Each `artifacts/<rid>/VERSION.txt` contains the correct version string | ☐ | |
| 1.9 | Each `artifacts/<rid>/README.txt` is present and legible | ☐ | |

---

## 2. Runtime

> Run `VRCGT.Launcher` from the `artifacts/win-x64/` publish output (not from `dotnet run`).

| # | Item | Status | Notes |
|---|------|--------|-------|
| 2.1 | Launcher prints startup header (URL, data path, stop instructions) to console | ☐ | |
| 2.2 | Server starts within 5 seconds; `[VRCGT] Server ready at http://127.0.0.1:<port>` printed | ☐ | |
| 2.3 | `GET /health` returns `{"status":"ok","app":"VRCGT","mode":"local"}` with HTTP 200 | ☐ | |
| 2.4 | `GET /api/v2/version` returns JSON with correct `version`, `runtime`, `arch`, `bindAddress`, `lanMode: false` | ☐ | |
| 2.5 | `GET /api/system/storage` returns correct `appDataPath`, `databasePath`, `databaseExists: true` | ☐ | |
| 2.6 | OS default browser opens automatically to `http://127.0.0.1:<port>` | ☐ | |
| 2.7 | Application shuts down cleanly on Ctrl+C / close; `[VRCGT] Stopped. Goodbye.` printed; no crash dump | ☐ | |
| 2.8 | Second launch re-uses the same data directory and existing database without error | ☐ | |

---

## 3. Auth

> Tests use the v2 local-user auth system (`/api/auth`, `/api/setup`). No VRChat credentials needed for these steps.

| # | Item | Status | Notes |
|---|------|--------|-------|
| 3.1 | First run: browser lands on `/setup` (setup wizard, not `/login`) | ☐ | |
| 3.2 | `POST /api/setup/owner` creates the owner account; `setupRequired` becomes `false` on next `GET /api/setup/status` | ☐ | |
| 3.3 | `POST /api/auth/login` with valid owner credentials returns 200 and sets an HttpOnly `auth` cookie | ☐ | |
| 3.4 | `GET /api/auth/me` with valid session returns the authenticated user's `username`, `displayName`, `permissions` | ☐ | |
| 3.5 | `POST /api/auth/logout` clears the auth cookie; subsequent `GET /api/auth/me` returns 401 | ☐ | |
| 3.6 | Closing and reopening the browser: session cookie persists; user lands on `/app/dashboard` without re-logging in | ☐ | |
| 3.7 | `POST /api/auth/login` with wrong password returns 401; no stack trace or internal detail in the response body | ☐ | |
| 3.8 | Unauthenticated request to a protected endpoint (e.g. `GET /api/auth/me`) returns 401, not 500 | ☐ | |

---

## 4. Storage

| # | Item | Status | Notes |
|---|------|--------|-------|
| 4.1 | App data folder created at the platform path on first run: `%APPDATA%\VRCGT\v2` (Windows) / `~/.local/share/VRCGT/v2` (Linux) / `~/Library/Application Support/VRCGT/v2` (macOS) | ☐ | |
| 4.2 | SQLite database file `vrcgt-v2.db` created inside the data path; all EF Core migrations applied automatically | ☐ | |
| 4.3 | `logs/` folder created inside the data path; log file(s) present after server startup | ☐ | |
| 4.4 | Settings written via the UI (e.g. an app setting value) are present after a full restart | ☐ | |
| 4.5 | `data/`, `cache/`, `exports/`, `backups/` sub-directories created (visible in `GET /api/system/storage` response) | ☐ | |

---

## 5. VRChat

> Requires real VRChat credentials. Can be skipped on a credential-free CI machine — must be run on at least one developer machine before release.

| # | Item | Status | Notes |
|---|------|--------|-------|
| 5.1 | `POST /api/vrchat/login` with valid username + password succeeds; returns `requiresTwoFactor: false` (or `true` on 2FA accounts) | ☐ | |
| 5.2 | 2FA flow: `requiresTwoFactor: true` response; submitting correct TOTP/email code via `POST /api/vrchat/verify-2fa` completes login | ☐ | |
| 5.3 | After restart, `GET /api/vrchat/session` indicates the VRChat session is still valid (cookie-jar persistence) | ☐ | |
| 5.4 | `POST /api/groups` (add a group by ID) succeeds; group appears in `GET /api/groups` | ☐ | |
| 5.5 | Dashboard auto-refreshes group member count / status without a full page reload | ☐ | |
| 5.6 | VRChat credentials are not written to logs (verify `logs/` directory contents) | ☐ | |

---

## 6. Web UI

> Verify with an actual browser. Do not use `dotnet run` — use the published artifact or a `dotnet build` + server start.

| # | Item | Status | Notes |
|---|------|--------|-------|
| 6.1 | `/setup` route renders the owner-creation wizard; submitting the form redirects to `/login` | ☐ | |
| 6.2 | `/login` route renders the login form; successful login redirects to `/app/dashboard` | ☐ | |
| 6.3 | `/app/dashboard` route renders group information; navigation sidebar is visible | ☐ | |
| 6.4 | `/app/settings` route renders the settings page | ☐ | |
| 6.5 | `/app/members` route renders the members list | ☐ | |
| 6.6 | `/app/audit-log` route renders the audit log | ☐ | |
| 6.7 | `/app/badge-scanner` route renders the badge scanner | ☐ | |
| 6.8 | `/app/kill-switch` route renders the kill switch | ☐ | |
| 6.9 | Hard-refreshing (`F5`) any deep route (e.g. `/app/dashboard`) returns the SPA `index.html` and the page loads correctly — not a 404 | ☐ | |
| 6.10 | Navigating to an unknown route shows a graceful error or redirects to a known route | ☐ | |

---

## 7. Security

| # | Item | Status | Notes |
|---|------|--------|-------|
| 7.1 | Default bind address is `127.0.0.1`; `GET /api/v2/version` confirms `"lanMode": false` | ☐ | |
| 7.2 | LAN mode (`0.0.0.0`) is **disabled by default** and can only be enabled via the Settings UI after an explicit typed-confirmation token | ☐ | |
| 7.3 | Enabling LAN mode requires `ManageSecurity` permission on the authenticated local user | ☐ | |
| 7.4 | Destructive endpoints (`kick`, `kick-all`, `remove-roles`, `restore-backup`, `delete-group`) require a valid `ConfirmationGuard` token in the request body; missing or wrong token returns 400 | ☐ | |
| 7.5 | Auth cookie is `HttpOnly`, `SameSite=Strict`; not accessible from JavaScript (`document.cookie` does not show it) | ☐ | |
| 7.6 | Log files in `logs/` contain no VRChat passwords, API keys, or session cookies | ☐ | |
| 7.7 | Log files contain no local-user passwords (hashed or plain) | ☐ | |
| 7.8 | Users without `ManageSecurity` permission receive 403 on `PATCH /api/system/access` | ☐ | |
| 7.9 | No hard-coded secrets, API keys, or credentials in any tracked source file (`git grep -i "password\|secret\|apikey"` returns no source hits) | ☐ | |

---

## 8. Packaging

> Verify the published artifacts in `artifacts/<rid>/` — not the development build.

| # | Item | Status | Notes |
|---|------|--------|-------|
| 8.1 | `artifacts/win-x64/VRCGT.Launcher.exe` is present and runs on a clean Windows machine | ☐ | |
| 8.2 | `artifacts/linux-x64/VRCGT.Launcher` is present; runs on Ubuntu 22.04 LTS (or verify via CI) | ☐ | |
| 8.3 | `artifacts/osx-x64/VRCGT.Launcher` is present; runs on macOS 13+ Intel | ☐ | |
| 8.4 | `artifacts/osx-arm64/VRCGT.Launcher` is present; runs on macOS 14+ Apple Silicon | ☐ | |
| 8.5 | **No Node.js or npm required** — all artifact directories contain `wwwroot/` with pre-built frontend assets | ☐ | |
| 8.6 | **No .NET runtime required** — artifacts are self-contained (`--self-contained true` in `scripts/build.ps1`); confirm by running on a machine with no .NET installed | ☐ | |
| 8.7 | Total size of each artifact directory is reasonable (ballpark: < 200 MB per RID uncompressed) | ☐ | |
| 8.8 | GitHub Actions CI workflow (`build.yml`) passed on the release commit: all 4 RIDs built; 53 tests passed | ☐ | |

---

## 9. Git Hygiene

| # | Item | Status | Notes |
|---|------|--------|-------|
| 9.1 | `git status` shows a clean working tree on the release branch (no uncommitted changes) | ☐ | |
| 9.2 | No `*.db` or `*.db-shm` / `*.db-wal` database files tracked in git (`git ls-files "*.db"` returns empty) | ☐ | |
| 9.3 | No `*.log` files tracked in git | ☐ | |
| 9.4 | No `node_modules/` directory tracked in git | ☐ | |
| 9.5 | No `bin/` or `obj/` directories tracked in git | ☐ | |
| 9.6 | No `artifacts/` directory tracked in git | ☐ | |
| 9.7 | No `appsettings.Production.json` or other environment-specific secrets tracked in git | ☐ | |
| 9.8 | `.gitignore` covers all categories above (validate with `git check-ignore -v <path>`) | ☐ | |
| 9.9 | Release commit is tagged with the version string (e.g. `git tag v2.0.0`) | ☐ | |

---

## 10. Documentation

| # | Item | Status | Notes |
|---|------|--------|-------|
| 10.1 | `README.md` reflects v2 as the current version; install / run instructions are correct for the published artifacts | ☐ | |
| 10.2 | `docs/v2/` updated: architecture doc, packaging doc, auth doc reflect the state of this release | ☐ | |
| 10.3 | `TESTING.md` test counts match actual test output (currently: 13 + 28 + 12 = 53) | ☐ | |
| 10.4 | `PACKAGING_AUDIT.md` records are consistent with the build script and artifact layout for this release | ☐ | |
| 10.5 | `BUILD_AUDIT.md` reflects any new projects, warnings, or dependency changes introduced since last audit | ☐ | |
| 10.6 | `FEATURE_PARITY_V1_TO_V2.md` is up to date (v2 ✅ Done items accurately reflect what is shipping) | ☐ | |
| 10.7 | Migration notes or upgrade instructions written for users upgrading from v1 or from a prior v2 pre-release | ☐ | |
| 10.8 | `CHANGELOG.md` entry for this version is present and accurate | ☐ | |

---

## Sign-off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Build verifier | | | |
| Runtime tester | | | |
| Security reviewer | | | |
| Release approver | | | |

> **Tagging rule:** Do not run `git tag` until every required checkbox above is marked ✅ or has an accepted written exception in the Notes column.
