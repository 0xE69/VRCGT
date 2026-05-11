# VRCGT — Legacy Code Cleanup Report

**Date:** 2026-05-11  
**Based on:** `LEGACY_CODE_INVENTORY.md`  
**Process:** Items marked `Safe to remove? = yes` in the inventory were targeted first; each batch was verified with `dotnet build` + `dotnet test` before proceeding.

---

## TL;DR

| Phase | Items targeted | Items deleted | Build result | Test result |
|---|---|---|---|---|
| Phase 1 — Build artifacts | 18 directories (6 484 files) | ✅ 18 directories deleted | 0 errors | 53/53 passed |
| Phase 2 — Source files | 0 | — nothing to do — | — | — |

**No source files were deleted.** All `yes`-rated items in the inventory were build artifacts. All remaining source-file candidates are rated `maybe` or `no`, which the cleanup rules exclude.

---

## Phase 1 — Build Artifacts (Completed)

### What was deleted

All `bin/` and `obj/` directories under every project, plus the top-level `artifacts/` directory and `src/VRCGT.Web/node_modules/`. Full details in `CLEANUP_GENERATED_FILES.md`.

| Directory | Files removed |
|---|---|
| `src/bin/` | 980 |
| `src/obj/` | 242 |
| `src/VRCGT.Core/bin/` | 6 |
| `src/VRCGT.Core/obj/` | 31 |
| `src/VRCGT.Infrastructure/bin/` | 12 |
| `src/VRCGT.Infrastructure/obj/` | 37 |
| `src/VRCGT.Server/bin/` | 650 |
| `src/VRCGT.Server/obj/` | 86 |
| `src/VRCGT.Launcher/bin/` | 478 |
| `src/VRCGT.Launcher/obj/` | 59 |
| `tests/VRCGT.Core.Tests/bin/` | 90 |
| `tests/VRCGT.Core.Tests/obj/` | 21 |
| `tests/VRCGT.Infrastructure.Tests/bin/` | 136 |
| `tests/VRCGT.Infrastructure.Tests/obj/` | 21 |
| `tests/VRCGT.Server.Tests/bin/` | 141 |
| `tests/VRCGT.Server.Tests/obj/` | 21 |
| `artifacts/` | 354 |
| `src/VRCGT.Web/node_modules/` | 3 119 |
| **Total** | **6 484** |

### Build verification after Phase 1

```
dotnet restore VRCGroupTools.sln                   → 8/8 projects restored
dotnet build VRCGroupTools.sln /p:SkipWebFrontend=true  → 0 errors, 14 warnings
dotnet test VRCGroupTools.sln /p:SkipWebFrontend=true   → 53/53 passed
```

The 14 warnings are pre-existing CS8602/CS8604 nullable warnings in `src/Services/VRChatApiService.cs` and `src/ViewModels/GameLogViewModel.cs`. Not introduced by this cleanup.

---

## Phase 2 — Source Files

### Inventory verdict

The complete inventory (`LEGACY_CODE_INVENTORY.md`) classifies every source file. The distribution of `Safe to remove?` ratings across source files is:

| Rating | Count | Action per cleanup rules |
|---|---|---|
| `yes` | 0 (all were build artifacts, now gone) | Delete |
| `maybe` | 13 candidates | **Do not delete** — human review required first |
| `no` | ~83 files | **Do not delete** — still referenced by v1 app or v2 planned feature |

**Result: nothing to delete in Phase 2.**

---

## The 13 `maybe` Candidates (Blocked — Need Human Decision)

These items cannot be deleted until a human answers the open questions listed in `LEGACY_CODE_INVENTORY.md §"Items That Need Human Review"`. Once a `maybe` is resolved to `yes`, it should be updated in the inventory and a new cleanup pass can run.

### Group A — v1 / v2 duplicate implementations (8 files)

These exist because both v1 WPF and v2 server implement the same feature independently, with no shared code. They can be removed only when v1 is retired or explicitly no longer shipped.

| File | v2 equivalent | v1 still ships? |
|---|---|---|
| `src/Views/BadgeScannerView.xaml` + `.cs` | `IBadgeScanService` + v2 endpoints ✅ | yes |
| `src/ViewModels/BadgeScannerViewModel.cs` | `IBadgeScanService` + v2 endpoints ✅ | yes |
| `src/Views/AuditLogView.xaml` + `.cs` | `IAuditLogService` + SignalR push ✅ | yes |
| `src/ViewModels/AuditLogViewModel.cs` | `IAuditLogService` + SignalR push ✅ | yes |
| `src/Views/KillSwitchView.xaml` + `.cs` | `IKillSwitchService` ✅ | yes |
| `src/ViewModels/KillSwitchViewModel.cs` | `IKillSwitchService` ✅ | yes |
| `src/Views/MembersListView.xaml` + `.cs` | `IMemberService` ✅ (kick/ban 📋 planned) | yes |
| `src/ViewModels/MembersListViewModel.cs` | `IMemberService` ✅ (kick/ban 📋 planned) | yes |

> `src/Services/VRChatApiService.cs` — large v1 VRChat API wrapper. Classified `v1-v2-duplicate` (`maybe`). Not in this batch because it is referenced by many v1 ViewModels; it cannot be removed independently without removing all v1 code.

### Group B — Possibly dead code in v1 (2 files)

These files appear to have no active call sites in the running v1 app. They are not referenced by v2.

| File | Finding | Open question |
|---|---|---|
| `src/Services/DiscordPresenceService.cs` | Registered as singleton in `App.xaml.cs` + disposed in `OnExit`. No ViewModel ever calls `UpdateGroupPresence` or any method on `IDiscordPresenceService`. | Is Discord Rich Presence intentionally dormant, or dead code? If dead code → mark `yes` and delete. |
| `src/ViewModels/GameLogViewModel.cs` | **Not registered** in `ConfigureServices`; loaded via XAML `d:DataContext` design-time binding. `GameLog` is ⛔ not planned in v2. | Was the DI registration intentionally omitted (abandoned feature) or accidentally missing? If intentionally abandoned → mark `yes` and delete. |

### Group C — Unreferenced asset files (2 files)

| File | Finding | Open question |
|---|---|---|
| `src/icon.jpg` | No reference in any `.cs`, `.xaml`, `.csproj`, or build script. | Is it the source image used to produce `src/icon.ico`? If no longer needed → mark `yes` and delete. |
| `src/icon_converted.ico` | Not used in any project file. `VRCGroupTools.csproj` uses `src/icon.ico`. | Safe to delete? If confirmed → mark `yes` and delete. |

### Group D — v1-only convenience scripts (1 item with multiple files)

| File | Finding | Open question |
|---|---|---|
| `build.bat` | Only builds v1 WPF app. Not referenced by CI. Misleading for v2 contributors. | Update with a `v1-only` warning comment? Or replace with a wrapper that calls `scripts/build.ps1`? |
| `install-dotnet.bat` | Opens browser to dotnet download page. Not referenced by CI. | Still useful? Update or remove? |
| `installer/setup.iss` | Broken icon path (`src/Resources/app.ico` does not exist; should be `src/icon.ico`). | Is the v1 installer still maintained? Fix broken reference or retire it? |

---

## How to Proceed

To unlock the next cleanup batch, take one or more of these actions:

1. **Retire v1 WPF** — If the WPF app is no longer shipped to users, change the 8 `v1-v2-duplicate` items + all `v1-legacy-referenced` items to `yes`, then run cleanup again. This is the largest potential deletion: ~83 source files + the entire `src/Views/`, `src/ViewModels/`, `src/Services/`, `src/Data/`, `src/Models/`, `src/Converters/`, `src/Helpers/` directories.

2. **Resolve Group B** — Confirm whether `DiscordPresenceService` and `GameLogViewModel` are dead code. If yes, update their entries to `yes` in `LEGACY_CODE_INVENTORY.md` and run cleanup again (2 small files; no other code depends on them).

3. **Resolve Group C** — Confirm whether `src/icon.jpg` and `src/icon_converted.ico` are needed. If not, update to `yes` and delete (no build impact).

4. **Resolve Group D** — Decide whether to update, annotate, or remove the v1 scripts and installer.

---

## Files Modified by This Cleanup

| File | Change |
|---|---|
| (none) | No source files were modified or deleted in Phase 2 |

See `CLEANUP_GENERATED_FILES.md` for the Phase 1 build-artifact deletion record.
