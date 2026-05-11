# BUILD AUDIT — VRCGT Repository

**Date:** 2026-05-11  
**Auditor:** GitHub Copilot  
**Goal:** Confirm the entire repository builds cleanly before release hardening.

---

## 1. Repository Structure

### Solution file
| File | Location |
|------|----------|
| `VRCGroupTools.sln` | repo root |

### .csproj files
| Project | Path | Type |
|---------|------|------|
| `VRCGroupTools` | `src/VRCGroupTools.csproj` | **Legacy WPF** — v1 app (net8.0-windows, UseWPF) |
| `VRCGT.Core` | `src/VRCGT.Core/VRCGT.Core.csproj` | **v2 — Core DTOs / interfaces** (net8.0) |
| `VRCGT.Infrastructure` | `src/VRCGT.Infrastructure/VRCGT.Infrastructure.csproj` | **v2 — EF Core / services** (net8.0) |
| `VRCGT.Server` | `src/VRCGT.Server/VRCGT.Server.csproj` | **v2 — ASP.NET Core web server** (net8.0) |
| `VRCGT.Launcher` | `src/VRCGT.Launcher/VRCGT.Launcher.csproj` | **v2 — Launcher / entry point** (net8.0) |

### Frontend
| Item | Location |
|------|----------|
| React 19 + Vite 6 + TypeScript 5.8 | `src/VRCGT.Web/` |
| Build output (wwwroot) | `src/VRCGT.Server/wwwroot/` |

### Project classification
| Project | Category |
|---------|----------|
| `VRCGroupTools` | Legacy WPF — v1; **included** in sln; excluded from v2 namespaces via explicit `<Compile Remove>` directives |
| `VRCGT.Core` | v2 active |
| `VRCGT.Infrastructure` | v2 active |
| `VRCGT.Server` | v2 active |
| `VRCGT.Launcher` | v2 active |
| Test projects | None present |
| Orphaned / unused | None — all 5 csproj files appear in the .sln |

### Project reference graph (v2 only)
```
VRCGT.Launcher
  └─ VRCGT.Server
       ├─ VRCGT.Core
       └─ VRCGT.Infrastructure
            └─ VRCGT.Core
```

---

## 2. Commands Run

### dotnet restore
```
dotnet restore
```
**Result:** `Restore complete (1.0s)` — no errors.

### dotnet build (initial — .NET only, no frontend)
```
dotnet build VRCGroupTools.sln --configuration Release /p:SkipWebFrontend=true
```
**Result:** `Build succeeded with 14 warning(s)` — no errors.

### TypeScript type-check
```
npx tsc --noEmit   (run from src/VRCGT.Web/)
```
**Result:** **6 errors** — see Section 4 for details and fixes.

### npm run build (Vite frontend)
```
npm run build   (run from src/VRCGT.Web/)
```
**Result:** Success. Output: `../VRCGT.Server/wwwroot/`

### dotnet build (full solution including frontend)
```
dotnet build VRCGroupTools.sln --configuration Release
```
**Result:** Initially **failed** with 1 MSBuild error (MSB3030 on VRCGT.Launcher) — see Section 4.  
After fix: `Build succeeded in 5.5s` — no errors.

### dotnet publish (Launcher, win-x64, self-contained)
```
dotnet publish src\VRCGT.Launcher\VRCGT.Launcher.csproj
  --configuration Release --runtime win-x64 --self-contained true
  /p:SkipWebFrontend=true --output artifacts\audit-test
```
**Result:** `Build succeeded` — `wwwroot/` present in publish artifact. Artifact cleaned after verification.

---

## 3. Final Build Result

| Step | Result |
|------|--------|
| `dotnet restore` | ✅ Clean |
| `dotnet build` (.NET only) | ✅ 0 errors, 14 warnings (all legacy WPF) |
| `npx tsc --noEmit` (TypeScript) | ✅ 0 errors (after fixes) |
| `npm run build` (Vite) | ✅ Clean |
| `dotnet build` (full, incl. frontend) | ✅ 0 errors, 14 warnings |
| `dotnet publish` (Launcher win-x64) | ✅ wwwroot included in artifact |

---

## 4. Errors Fixed

### 4.1 MSBuild MSB3030 — VRCGT.Launcher "could not copy file … not found"

**File:** `src/VRCGT.Launcher/VRCGT.Launcher.csproj`  
**Symptom:** `error MSB3030: Could not copy the file "…\index-CWzo5vdi.js" because it was not found`

**Root cause:**  
The Launcher csproj used a static `<Content Include="..\VRCGT.Server\wwwroot\**\*">` glob. MSBuild evaluates `<Content>` globs at *project parse time* (before any build targets run), resolving them to whatever filenames exist in wwwroot at that moment. VRCGT.Server has a `BeforeTargets="Build"` step that runs `npm run build`, which cleans the Vite output directory and re-emits new content-hashed filenames (e.g. `index-C9tZH2Qy.js`). If the hash changes between the previous build and the current build, the Launcher's pre-resolved glob points to old filenames that have been deleted, producing MSB3030.

**Fix:**  
Replaced the static `<Content>` ItemGroup with two MSBuild Targets that contain the `<ItemGroup>` inline. Items declared inside a `<Target>` are evaluated at *execution time*, after VRCGT.Server's `BuildWebFrontend` target has already completed, so the glob always resolves to the current hashed filenames:

- `CopyServerWwwrootToOutput` — `AfterTargets="Build"`, copies to `$(OutDir)wwwroot/`  
- `CopyServerWwwrootToPublish` — `AfterTargets="Publish"`, copies to `$(PublishDir)wwwroot/`

Both targets are guarded by `Condition="Exists('…wwwroot\index.html')"` so they are silently skipped on a fresh clone before the frontend is first built.

---

### 4.2 TypeScript TS6133 — `DONE_STATUSES` declared but never read

**File:** `src/VRCGT.Web/src/components/JobsDrawer.tsx:5`  
**Cause:** A `const DONE_STATUSES = new Set(…)` was declared for anticipated use but the body of the component never references it.  
**Fix:** Removed the unused constant.

---

### 4.3 TypeScript TS6133 — `scanning` state value never read

**File:** `src/VRCGT.Web/src/pages/BadgeScannerPage.tsx:64`  
**Cause:** `const [scanning, setScanning] = useState(false)` — `setScanning` is called (to track whether a scan job is pending) but the `scanning` read-value is never used in JSX or logic.  
**Fix:** Changed destructuring to `const [, setScanning] = useState(false)` to discard the unused value. The setter is preserved because it is called in the scan flow.

---

### 4.4 TypeScript TS2551 — `m.WasKicked` wrong casing

**File:** `src/VRCGT.Web/src/pages/BadgeScannerPage.tsx:193`  
**Cause:** Property access `m.WasKicked` used PascalCase; the DTO type `BadgeScanMemberDto` declares `wasKicked` (camelCase).  
**Fix:** Changed `!m.WasKicked` → `!m.wasKicked`.

---

### 4.5 TypeScript TS2322 — `onRefresh` prop type mismatch on `GroupDashboard`

**File:** `src/VRCGT.Web/src/pages/DashboardPage.tsx:56`  
**Cause:** The `GroupDashboard` component declared `onRefresh: (id: string) => Promise<void>`. The caller passes `refreshGroup` from `useGroups()` which returns `Promise<GroupDetailDto>`. TypeScript rejects this because `Promise<GroupDetailDto>` is not assignable to `Promise<void>`.  
**Fix:** Updated the prop type to `onRefresh: (id: string) => Promise<GroupDetailDto>`. The component discards the resolved value with `await onRefresh(…)`, so behaviour is unchanged.

---

### 4.6 TypeScript TS6133 — unused `.map` index parameter `i`

**File:** `src/VRCGT.Web/src/pages/KillSwitchPage.tsx:354`  
**Cause:** `.map((r, i) => …)` — the index parameter `i` was declared in the callback signature but never used inside the body.  
**Fix:** Removed `i` from the parameter list: `.map((r) => …)`.

---

### 4.7 TypeScript TS6133 — `ApiError` imported but never used

**File:** `src/VRCGT.Web/src/pages/LocalUsersPage.tsx:2`  
**Cause:** `import { api, ApiError } from '../api/client'` — `ApiError` was imported for anticipated error handling but no `catch` block in the file references it.  
**Fix:** Removed `ApiError` from the import: `import { api } from '../api/client'`.

---

## 5. Remaining Warnings (Not Fixed)

All 14 remaining warnings come exclusively from the **legacy WPF project** (`VRCGroupTools`). They are pre-existing, do not affect v2, and are not build-breaking. They are preserved per the audit rules ("do not remove old WPF code yet").

| Count | Code | File | Description |
|-------|------|------|-------------|
| 1 | CS8601 | `src/ViewModels/GameLogViewModel.cs:298` | Possible null reference assignment |
| 6 | CS8602/CS8604 | `src/Services/VRChatApiService.cs:2235,2239,2245,2249,2255,2259` | Possible null dereference / null argument (Newtonsoft JToken calls) |

> Each warning appears twice in the build output because the WPF SDK internally creates a temporary compilation project (`_wpftmp`) before the final one. This doubles the count (7 unique × 2 = 14 reported) but represents the same 7 source locations.

### Frontend non-actionable warning
Vite/Rollup emits two `/*#__PURE__*/` annotation warnings on `node_modules/@microsoft/signalr/dist/esm/Utils.js`. These originate inside the SignalR npm package (third-party), cannot be suppressed without patching the package, and do not affect the build output.

---

## 6. Projects Included in Build

| Project | Framework | Built | Notes |
|---------|-----------|-------|-------|
| `VRCGT.Core` | net8.0 | ✅ | |
| `VRCGT.Infrastructure` | net8.0 | ✅ | |
| `VRCGT.Server` | net8.0 | ✅ | Triggers `npm run build` via BeforeTargets |
| `VRCGT.Launcher` | net8.0 | ✅ | Copies wwwroot via AfterTargets |
| `VRCGroupTools` (WPF v1) | net8.0-windows | ✅ | Legacy; 7 nullable warnings |
| VRCGT.Web (frontend) | — | ✅ | Built by npm; output to VRCGT.Server/wwwroot |

## 7. Projects Excluded from Build

None — all 5 csproj files in the solution build successfully. There are no orphaned, test, or unused projects.

---

## 8. Remaining Concerns

| # | Concern | Severity | Action |
|---|---------|----------|--------|
| 1 | **Legacy WPF nullable warnings** (7 locations in `VRChatApiService.cs` and `GameLogViewModel.cs`) | Low | Pre-existing in v1 codebase. Address when v1 WPF code is cleaned up. |
| 2 | **SignalR npm Rollup warnings** (`/*#__PURE__*/` in `Utils.js`) | Info | Third-party issue; no action available until SignalR package is updated. |
| 3 | **`Class1.cs` placeholder files** exist in `VRCGT.Core` and `VRCGT.Infrastructure` | Low | Harmless empty files left from project creation; delete in a future cleanup pass. |
| 4 | **No test projects** in the solution | Medium | Unit and integration tests do not exist yet; flag for post-hardening. |
| 5 | **WPF v1 project is `net8.0-windows`** with `RuntimeIdentifier=win-x64` set unconditionally | Low | This prevents cross-platform restore of the sln on Linux/macOS CI. Acceptable because WPF is Windows-only; mitigate in CI by building v2 projects only on non-Windows agents. |
| 6 | **`scanning` state in `BadgeScannerPage`** — setter is called but value is never read | Low | State is intentionally set (tracks scan-in-progress for future UI gate). Currently renders no visible indicator; implement the indicator before release. |

---

## Audit Run 2 — 2026-05-11 (post setup-error fix)

**Trigger:** First-run setup form showed "An unexpected error occurred." instead of the actual server validation message when an email address was entered as a username.

### Root cause

`client.ts` `request<T>()` constructed `ApiError(status, payload.error, payload.message ?? res.statusText)`. The server returns `{"error":"Username may only contain…"}` — the human-readable detail is in `error`, not `message`. So `err.message` was always `res.statusText` ("Bad Request") and `SetupPage.tsx` displayed that instead of the validation text.

`SetupPage.tsx` additionally used `err instanceof ApiError ? err.message : 'An unexpected error occurred.'` — meaning any non-`ApiError` (e.g. network failure) silently fell back to the generic string.

### Fixes applied

| File | Change |
|------|--------|
| `src/VRCGT.Web/src/api/client.ts` | Error extraction changed to `payload.message ?? payload.error ?? res.statusText` so either response shape (`message` or `error`) surfaces a human-readable string |
| `src/VRCGT.Web/src/pages/SetupPage.tsx` | Catch widened to `err instanceof Error` (covers `ApiError` and native network errors); removed now-unused `ApiError` import (TS6133) |

### Commands run

```
# TypeScript check
npx tsc --noEmit                                               → 0 errors

# Frontend build
npm run build                                                  → ✓ built in 1.53s

# Full .NET build (SkipWebFrontend — frontend already built)
dotnet build VRCGroupTools.sln --configuration Release /p:SkipWebFrontend=true
                                                               → Build succeeded  0 errors  0 warnings

# Full .NET build (with frontend)
dotnet build VRCGroupTools.sln --configuration Release        → Build succeeded  0 errors  0 warnings

# Publish
dotnet publish src\VRCGT.Launcher\VRCGT.Launcher.csproj --configuration Release --output ./publish-test
                                                               → Build succeeded  (artifact cleaned after verification)
```

### Build result (Run 2)

| Step | Result |
|------|--------|
| `dotnet restore` | ✅ Clean |
| `dotnet build` (.NET only, SkipWebFrontend) | ✅ 0 errors, **0 warnings** |
| `npx tsc --noEmit` | ✅ 0 errors |
| `npm run build` | ✅ Clean |
| `dotnet build` (full, incl. frontend) | ✅ 0 errors, **0 warnings** |
| `dotnet publish` (Launcher) | ✅ wwwroot included in artifact |

> **Notable:** This run produced 0 warnings even from the legacy WPF project.
> The `VRCGroupTools` (WPF v1) nullable warnings recorded in Run 1 are incremental-build
> artifacts — they do not re-emit on a clean rebuild when no WPF source files have
> changed. They remain latent and will reappear if any WPF file is touched.

### Remaining concerns (unchanged from Run 1)

Items 1–6 from Section 8 above are unchanged. No new concerns introduced.
