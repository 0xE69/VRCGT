# Local package verification — 1.1.21

**Regenerated after the authorized final HTTP-dispatch pacing repair. Ready for parent independent review and authorized release publication; not yet published.** See `releases/1.1.21-PRESENCE-FIX.md` for architecture, limited coverage, pinned community schema and RED→GREEN proof. Previous 1.1.21 hashes/counts are historical and superseded here.

## Outcome

Built and verified the real self-contained **win-x64 application**, **Inno Setup installer**, and **legacy-compatible portable update ZIP** from `C:/Users/mail/Projects/VRCGT`, preserving all dirty 1.1.20 source features. Version 1.1.21 is synchronized in App, csproj/assembly/file, installer and top changelog entry.

No production App/installer was started, no installed/Desktop executable was replaced, and no real session/database, invitation, login or publication was used. Disposable dummy executables and the actual replacement helpers were executed only in isolated update tests. Public release upload/readback is the parent agent's separately authorised operation; this child did not commit, push, tag or publish.

## Offline tests and build

All final commands exited zero:

| Suite | Passing groups |
|---|---:|
| PublicationTests | 25 |
| GroupInviteTests | 10 |
| InstanceInviteTests | 31 |
| GroupMetadataRegression | 1 |
| SchedulerWebhookTests | 30 |
| AutoInviteTests | 69 |
| UpdateTests | 33 (31 base + 2 frozen-candidate checks) |
| **Total** | **199** |

Full Release Rebuild: **0 errors, 12 pre-existing nullable warnings** (six legacy parsing warnings emitted by temporary WPF and final compilation). No new warning-cleanup claim. `git diff --check` passes. SDK: `C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe`.

Current run logs/results are under ignored `installer/Output/verification-1.1.21-dispatch/`, including final run logs, `results.json` and `acceptance-summary.json`. All seven suites ran before packaging and again with `--no-build` after packaging; frozen UpdateTests received the actual regenerated ZIP. Counts were aggregated from actual runner PASS output. Set `TMPDIR`, `TEMP`, and `TMP` to Hermes scratch; GroupInviteTests requires TMPDIR. Previous verification directories are historical evidence, not the current candidate.

The additional security regressions exercise LF/CRLF/forged timestamps, literal conventional records without corroboration, missing identity/location, private/offline/web-only/traveling/mismatched responses, read errors, unchanged/expired evidence without refetch, paced new candidates, and auth/context/departure/off/expiry during actual pre-HTTP pacing. Outer write-deny handlers distinguish fixture GETs from invite writes. WPF verification uses a plain Application plus DispatcherSynchronizationContext, never production App.

**Limited automatic coverage, not all occupants:** local logs only nominate candidates. Each new candidate needs an API-returned exact identity, state=online and exact current location, fresh within 20 seconds including pacing/network time. Hidden/private/offline/non-friend/unknown/mismatched/expired evidence is skipped without automatic refetch; manual explicit review remains available. UI/docs state this. No universal roster or physical-attendance/API propagation guarantee is invented. See AUTO-INVITE.md and the pinned community (not official) schema evidence.

Auto tests cover exact five-second polling, no startup send, nonoverlap, continued local detection during HTTP/detail awaits, separate thirteen-second send pacing, context/toggle/pacing/in-flight changes, same-location rejoin, local-only unchanged cycles, exact-ID discovery/departures, durable concurrent/manual/automatic claims, Unknown/Failed persistence, Retry-After, and the compiled checkbox/status/filter/unload/shutdown/DI path under a plain WPF Application. Self, members, unknown membership and unresolved IDs are present in the integrated fixture and skipped. Explicit manual refresh also resolves only new eligible IDs whose enabled filters need metadata, without enabling automation or re-fetching unchanged users. No production App construction is used.

Update tests retain the actual v1.1.18 source and isolate only environmental boundaries. Its real catalog client/download/staging and generated batch copy/relaunch execute against a loopback fixture and disposable dummy EXE, asserting real bytes and relaunch marker. The legacy self-deleting batch can exit nonzero after copy/start, and redirected timeout prints an input warning; those quirks are not misreported as a clean helper exit. The new bounded helper independently proves Unicode/metacharacter handling, an exited parent, SHA tamper rejection and launch-failure rollback/error logs. The actual frozen portable ZIP is additionally downloaded/staged by both old and new adapters without executing its production EXE.

## Frozen artifacts

Paths below are relative to `C:/Users/mail/Projects/VRCGT`.

| Artifact | Bytes | SHA256 |
|---|---:|---|
| `src/bin/Publish/VRCGroupTools.exe` | 183306889 | `2dd118c8210d1ef17db1fb7c0ce5550b871bd97fbb7d67799aed11434e787560` |
| `installer/Output/VRCGroupTools_Setup_1.1.21.exe` | 53528937 | `b110169a2b117229d90b14fa014c339a982d3627b020b014a416b91f3c7f153a` |
| `installer/Output/VRCGroupTools-v1.1.21-win-x64.zip` | 72622576 | `1d6fd98e888f01b03179b2ab1d4a46b40319ba317f10cbcbf67f32302d2e322f` |
| `installer/Output/VRCGroupTools-v1.1.21-win-x64.zip.sha256` | 101 | `3bc5a27a36469929ca1ea07a495caa3bd9cc5a8204d55f3ad8f0628d5140ab6f` |

- App file version **1.1.21.0**, informational product **1.1.21+8d68b4b94717d0767cd244b3436d2194577d178c**. Installer product version **1.1.21**.
- Tested Release DLL SHA256 **`0a024acf0adabc582b747215a036a2a989f2b24d004f8026f24b5d3e7a695e7c`**. It matches the DLL copied into all six app-referencing test outputs and the actual DLL embedded in the published single-file EXE. Publish used `--no-build --no-restore` after the full build/test run to preserve these exact tested bytes. Subsequent candidate staging tests did not change the DLL or artifacts.
- Bundle manifest **6.0**, **474 entries**, Microsoft.NETCore.App and Microsoft.WindowsDesktop.App **8.0.33**, managed/native WPF and native SQLite, AMD64 static runtime/JIT exports. Portable ZIP has exactly root `VRCGroupTools.exe` and `SHA256SUMS.txt`; contained EXE hash equals the tested published EXE. The external ZIP checksum and internal EXE checksum are both verified.
- `installer/verify-package.py` parses bundle/PE, checks version synchronization/Release assembly equality/runtime content/ZIP root/checksums and hashes actual files without starting them. Manifest: `installer/Output/package-manifest.json`.
- Inno Setup compiler **6.7.4**, existing portable `installer/Output/tooling/InnoSetup/ISCC.exe`, compiled only the published EXE/PDB. Existing `x64` deprecation warning is non-blocking. The installer is unsigned and not install-tested.
- Existing **1.1.20 installer remains unchanged** with SHA256 `ee5347f884d0efa3dc29a6fb33a8cb56c6f56b666fc24faee68ac9a25a000156`. Its previous verification note is preserved at `docs/releases/1.1.20-VERIFICATION.md` and old manifest under `installer/Output/verification-1.1.20/`.

## Reproduce frozen verification

```bash
export TMPDIR='C:/Users/mail/AppData/Local/hermes/cache/scratch'
export TEMP="$TMPDIR" TMP="$TMPDIR"
# Full rebuild/tests are required after source changes. To rerun the existing frozen suites:
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --no-build --project tests/AutoInviteTests -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --no-build --project tests/UpdateTests -c Release -- C:/Users/mail/Projects/VRCGT/installer/Output/VRCGroupTools-v1.1.21-win-x64.zip
python installer/verify-package.py
git diff --check
```

## Source provenance and remaining acceptance

Branch `handoff/fresh-hermes-20261030`, origin `RE-LITY/VRCGT`, unchanged HEAD `8d68b4b94717d0767cd244b3436d2194577d178c`. These binaries include preserved uncommitted features; informational HEAD is not a claim that old committed source matches them. The current verification directory's `source-manifest.json` records exact nonignored source paths/hashes separately from generated outputs. Parent must review/record the complete release source and choose an authorised commit/snapshot/tag plan before publishing. Do not tag old HEAD as if it contained these changes.

The **update consumer is 0xE69/VRCGT**, not git origin. The parent must publish/read back the approved stable release and compatible ZIP/checksum, download public assets and compare frozen hashes before calling the update discoverable. Installed 1.1.18 GUI upgrade acceptance remains separate. Live VRChat invitations, presence API availability/privacy/propagation behavior, permissions, current-log availability, secure setup, publication/readback, notification delivery and installed GUI operation remain untested. Broader all-occupant automatic coverage needs an architectural/user decision, not another log regex. Auto Invite fails closed for uncorroborated presence, unsupported/stale/ambiguous logs or incomplete membership cache. A request already accepted by VRChat cannot be recalled. See `AUTO-INVITE.md` and `UPDATE-RELEASE.md`.
