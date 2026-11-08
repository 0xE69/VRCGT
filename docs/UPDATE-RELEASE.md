# Update delivery and legacy compatibility — 1.1.21

## Actual consumer and root cause

`App.GitHubRepo` is **0xE69/VRCGT**, not this checkout's `RE-LITY/VRCGT` git origin. Installed 1.1.18 checks that public release feed and selects a ZIP; an unpublished local 1.1.20 installer cannot be discovered. Public release upload/readback is a separately authorised parent-agent operation. Until that readback succeeds, local packages alone are not a working public update feed.

The exact 1.1.18 updater source was retrieved read-only from `0xE69/VRCGT` at tag `v1.1.18` and retained at `tests/UpdateTests/Fixtures/LegacyUpdateService.cs.txt`:

- Git blob: `9ed376c8af4fcd1ae973760bf8723384ccc5b17e`
- UTF-8 source SHA256: `1bd2ca1801022fd796160f65f720f0876f77b39d1ed64c4f2afcfb4838171130`

The old code takes the first non-prerelease, first ZIP, then first recursively found EXE. Therefore the release must have exactly one portable ZIP with **only the intended root VRCGroupTools.exe**, never an installer or second EXE in that ZIP. Do not publish a newer stable release without its compatible ZIP.

## Artifacts

From the repository root, after all tests/full Release build:

```bash
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' publish src/VRCGroupTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-build --no-restore -o src/bin/Publish
installer/Output/tooling/InnoSetup/ISCC.exe installer/setup.iss
python installer/package-update.py
python installer/verify-package.py
```

Expected release assets:

- `installer/Output/VRCGroupTools-v1.1.21-win-x64.zip`
- `installer/Output/VRCGroupTools-v1.1.21-win-x64.zip.sha256`
- `installer/Output/VRCGroupTools_Setup_1.1.21.exe`
- Optionally the package/source-verification manifests and release notes after reviewing provenance/privacy.

Portable ZIP entries are exactly `VRCGroupTools.exe` and `SHA256SUMS.txt`. The sidecar verifies the ZIP; the internal manifest verifies the executable. Both are checked by the new updater when supplied. Checksums are integrity checks, not a publisher signature. Existing 1.1.18 ignores these manifests but still selects the sole correct executable. `verify-package.py` verifies source/PE versions, bundled Release DLL equality, self-contained runtime/WPF/SQLite content, ZIP-root equality with the same published EXE and hashes of actual artifacts.

A WPF rebuild may change assembly bytes. After any rebuild affecting frozen binaries, republish/recompile/repackage/recompute hashes. Independently rerun frozen suites with `run --no-build` when source has not changed.

## New updater hardening

- Sort usable stable releases by parsed version, ignoring drafts/prereleases/malformed tags and releases without the exact versioned win-x64 portable ZIP. Reject unrelated download origins; clear stale latest/download state on error/no candidate.
- Bound compressed download and expanded payload to 512 MiB, ZIP entry count to 64 and checksum text to 4 KiB. Reject traversal/absolute/ADS paths, duplicates, symlinks, other/nested executable names and missing exact root EXE. Only the expected executable is staged; no arbitrary archive files are installed.
- Verify supplied ZIP and executable SHA256; verify the selected executable's file version before starting replacement. Use unique staging directories.
- A Unicode-safe encoded PowerShell helper waits at most 60 seconds for an existing parent, tolerates an already-exited parent, verifies staged/copied bytes, and attempts atomic replacement at most twenty times with 500 ms waits. It retains the old executable as a unique backup, rolls it back on an immediate launch failure and records success/errors under `%LOCALAPPDATA%/VRCGroupTools/update-logs`. A visible error dialog is enabled in production. It does not delete a global shared .NET extraction cache.
- Backup/staging files are intentionally retained for recovery/inspection; clean up old ones only after verifying a successful installation. A later crash after a successful OS launch is not detected or automatically rolled back. Non-writable application directories, endpoint/security policy or disabled PowerShell may still block an update and require the installer/manual update.

## Offline acceptance evidence

`UpdateTests` runs the actual 1.1.18 source with isolation-only substitutions (namespace/version/repository, disposable temp/target paths, process-launch capture and app-shutdown capture). Its real GitHub client reads loopback release metadata; its real HttpClient downloads a fixture ZIP. The original staging and generated batch are exercised against a compiled disposable dummy EXE, and that EXE actually relaunches and writes a marker. No production App or installed executable is started or replaced.

The legacy self-deleting batch can return a nonzero cmd exit after successful copy/start, and redirected Windows `timeout` warns about noninteractive input. The test asserts actual destination bytes and a real relaunch marker, not an invented zero helper exit. The new helper has independent real-process success, Unicode/metacharacter, missing-parent, tamper-rejection and launch-failure rollback tests. Catalog/download/archive tests use local fixtures/fake HTTP, not live releases or API writes.

## Source provenance and publication handoff

The checkout includes preserved uncommitted 1.1.20 features plus this fix. An executable built from that dirty tree is **not** the contents of the old HEAD merely because informational metadata contains its SHA. Do not attach a new release tag to old HEAD and describe it as matching source. The parent must review the full candidate diff/untracked source, exclude runtime/user data/credentials, and choose an authorised release commit/source snapshot before public publication. This child does not commit, push, create tags or publish.

After review, commit the exact frozen source paths (including untracked production/tests/docs) to the authorized public release-source commit and tag that new commit, not old HEAD. Compare each committed blob's working-tree bytes or normalized checkout SHA256 against `installer/Output/verification-1.1.21-dispatch/source-manifest.json` before tagging; Git autocrlf can change blob line endings, so compare a checkout with the same attributes rather than naïvely comparing raw blob SHA256. No application rebuild is needed if the compile inputs remain identical: this release explicitly retains the build-time informational suffix `+8d68b4b94717d0767cd244b3436d2194577d178c`, while the tag/commit plus frozen manifest identifies the reviewed dirty-tree source actually compiled. The suffix is historical build metadata, not a claim that old HEAD contains the release source. Documentation-only publication receipts can be recorded separately. If code/build inputs change at review, rebuild/retest/repackage instead of reusing these hashes.

After the parent publishes, read back exact release/tag/stable/latest status and asset names from **0xE69/VRCGT**, download the public portable ZIP into a fresh scratch directory, and compare its ZIP and contained EXE hashes with the frozen package manifest. Public discoverability is not proof that a particular user's installed 1.1.18 GUI has completed the real upgrade; that final installed-app acceptance remains separate.
