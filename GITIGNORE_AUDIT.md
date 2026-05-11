# VRCGT — .gitignore Audit

**Date:** 2026-05-11

---

## 1. Problems Found in the Previous .gitignore

### Critical: `appsettings.json` and `appsettings.*.json` were too broad

The old `.gitignore` contained:

```
appsettings.json
appsettings.*.json
```

This ignored **all** `appsettings*.json` files anywhere in the repository,
including the intentional source files:

- `src/VRCGT.Server/appsettings.json` — base logging config (`AllowedHosts: *`)
- `src/VRCGT.Server/appsettings.Development.json` — development log level overrides

These are not secrets; they belong in source control. The fix replaces the
blanket rules with specific patterns that only ignore local developer overrides:

```gitignore
appsettings.Local.json
appsettings.Development.local.json
appsettings.*.local.json
secrets.json
```

### Missing: `artifacts/` directory

The release build output directory was not ignored, causing it to show as
untracked (`?? artifacts/`) in `git status`. Added `artifacts/` to ignore.

### Missing: several required categories

See §2 for the full list of new rules added.

### Duplicate / redundant rules

`[Pp]ublish/` was present but `publish/` was also listed elsewhere. Cleaned
into a single canonical entry.

---

## 2. New Rules Added (and Reason)

### .NET Build Outputs

| Rule | Reason |
|---|---|
| `TestResults/` | `dotnet test` output directory |
| `*.rsuser` | Rider/ReSharper user-settings file |
| `*.snupkg` | Symbol NuGet packages — generated |

### Publish / Release Artifacts

| Rule | Reason |
|---|---|
| `artifacts/` | Build script output (`scripts/build.ps1` / `build.sh`) |
| `releases/` | Possible manual release staging directory |
| `dist-release/` | Alternative release staging directory |
| `*.zip` | Archive outputs from release pipeline |
| `*.tar.gz` | Archive outputs from release pipeline |
| `*.7z` | Compressed archive outputs |
| `*.msi` | Windows installer outputs |

### Frontend

| Rule | Reason |
|---|---|
| `node_modules/` | npm dependency directory — restored with `npm ci` |
| `src/VRCGT.Web/dist/` | Vite build output (project uses `../VRCGT.Server/wwwroot` as outDir, but `dist/` added defensively) |
| `npm-debug.log*` | npm debug log files |
| `yarn-debug.log*` | Yarn debug log files |
| `yarn-error.log*` | Yarn error log files |
| `pnpm-debug.log*` | pnpm debug log files |
| `.vite/` | Vite dev-server cache directory |
| `coverage/` | Vitest / Jest coverage output |

### Local Application Data

| Rule | Reason |
|---|---|
| `exports/` | Runtime user-export directory |
| `backups/` | Runtime backup directory |

### Secrets and Local Config

| Rule | Reason |
|---|---|
| `appsettings.Local.json` | Per-developer machine config override |
| `appsettings.Development.local.json` | Per-developer dev override |
| `appsettings.*.local.json` | Any per-developer environment override |
| `secrets.json` | ASP.NET Core user-secrets fallback file |
| `.env.*` | All `.env` variant files (`.env.production`, `.env.test`, etc.) |
| `!.env.example` | Negation: `.env.example` IS tracked (template for new devs) |

### IDE

| Rule | Reason |
|---|---|
| `.idea/` | JetBrains Rider / IntelliJ project directory |
| `.vscode/*` | All VS Code workspace files… |
| `!.vscode/extensions.json` | …except recommended extensions list (tracked) |
| `!.vscode/settings.example.json` | …except shared settings template (tracked) |

> The old rule was `.vscode/` (ignore entire directory). The new rules use a
> negation pattern to allow specific shareable VS Code files through.

---

## 3. Rules Removed or Changed

| Old rule | Change | Reason |
|---|---|---|
| `appsettings.json` | **Removed** | Too broad — would ignore source files |
| `appsettings.*.json` | **Removed** | Too broad — would ignore source files |
| `*.local` | **Removed** | Overly broad file extension; replaced by specific `appsettings.*.local.json` |
| `*.local.json` | **Removed** | Replaced by more targeted `appsettings.*.local.json` |
| `.vscode/` | **Changed** | Now `.vscode/*` with negation exceptions for shareable files |
| `[Pp]ublish/` | **Replaced** | Normalised to `[Pp]ublish/` — kept equivalent |

---

## 4. Rules Retained (Already Correct)

`bin/`, `obj/`, `*.user`, `*.suo`, `*.userosscache`, `packages/`, `*.nupkg`,
`project.lock.json`, `.env`, `*.pem`, `*.key`, `*.db`, `*.db-shm`, `*.db-wal`,
`*.sqlite`, `*.sqlite3`, `settings.json`, `Cache/`, `cache/`, `Logs/`, `logs/`,
`CrashReports/`, `crashreports/`, `auth_cookie.txt`, `session.json`,
`credentials.enc`, `*.enc`, `*.cookie`, `*.cookies`, `*.token`, `auth_*.txt`,
`auth_*.json`, `webhook*.txt`, `discord_*.json`, `.vs/`, `.DS_Store`, `Thumbs.db`,
`desktop.ini`, `*.log`, `*.bak`, `*.backup`, `*.tmp`, `*.temp`, `~$*`,
`installer/Output/`

---

## 5. Validation Results

All checks performed against the updated `.gitignore`:

| Test | Expected | Actual |
|---|---|---|
| `src/VRCGT.Server/appsettings.json` | NOT ignored | ✅ Not matched |
| `src/VRCGT.Server/appsettings.Development.json` | NOT ignored | ✅ Not matched |
| `appsettings.Local.json` | Ignored | ✅ Matched line 69 |
| `appsettings.Development.local.json` | Ignored | ✅ Matched line 71 |
| `secrets.json` | Ignored | ✅ Matched line 72 |
| `artifacts/` | Ignored | ✅ Matched line 22 |
| `src/VRCGT.Web/node_modules/` | Ignored | ✅ Matched line 40 |
| `.env.example` | NOT ignored (negation) | ✅ Not matched |
| All currently-tracked source files | NOT ignored | ✅ `git ls-files \| git check-ignore` = 0 matches |

---

## 6. Currently Tracked Files That Should Be Removed

`git ls-files` shows **no generated files are currently tracked** (the new v2
projects under `src/VRCGT.Core/`, `src/VRCGT.Server/`, etc. are entirely
untracked `??` directories). There are no tracked `bin/` or `obj/` files to
remove.

The following untracked directories will be correctly ignored once their files
are added to the index:

- `artifacts/` — build script output (`.gitignore` now ignores it)
- `src/VRCGT.Web/node_modules/` — npm packages (`.gitignore` now ignores it)

---

## 7. Advisory: `src/VRCGT.Server/wwwroot/` Build Artifacts

The Vite build outputs pre-built React assets to `src/VRCGT.Server/wwwroot/`.
This directory is **not currently tracked** and is **not currently ignored** —
it will become visible to git once the `src/VRCGT.Server/` tree is first
committed.

**Recommendation:** Do **not** commit `src/VRCGT.Server/wwwroot/` assets.
They are generated from source in `src/VRCGT.Web/src/` and rebuilt automatically
by:
- `scripts/build.ps1` / `scripts/build.sh`
- GitHub Actions CI
- `dotnet build` (via `BuildWebFrontend` MSBuild target)

To enforce this, add to `.gitignore` when the `src/VRCGT.Server/` tree is
committed:

```gitignore
src/VRCGT.Server/wwwroot/assets/
```

> `src/VRCGT.Server/wwwroot/` itself should remain tracked (so the directory
> exists in a fresh clone before the first `npm run build`). Only the generated
> content-hashed asset files need to be ignored.

This is left as an advisory rather than an immediate change because tracking
vs. not tracking the built frontend is a project policy decision.

---

## 8. How to Remove Accidentally Tracked Generated Files (If Needed)

If in future `bin/`, `obj/`, or other generated files end up tracked, use:

```bash
# Remove from index only (keeps the files on disk)
git rm -r --cached bin/ obj/ TestResults/
git rm -r --cached src/VRCGT.Web/node_modules/
git rm -r --cached artifacts/

git commit -m "chore: remove tracked build artifacts from index"
```
