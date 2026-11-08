# Current-instance friend invite tests

Run from the repository root on Windows with the .NET 8 SDK:

```bash
dotnet run --project tests/InstanceInviteTests -c Release
dotnet build src/VRCGroupTools.csproj -c Release
dotnet publish src/VRCGroupTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o src/bin/Publish
```

This is a package-free console assertion harness with a project reference to the actual application, not a second implementation of the feature. Exit code 1 means at least one test failed. The application's existing NuGet dependencies are still restored normally. Build outputs and logs are ignored by Git.

## Isolation

- No VRChat credentials, live log files, or live invites are used.
- Synthetic log fixtures are created under the harness output directory and deleted after each test.
- All tested HTTP dispatch uses `RecordingHandler`, never a real network transport.
- View-model dependencies use `DispatchProxy` fakes. Tests exercise real generated MVVM commands and the real workflow.
- The last test uses a **plain WPF `Application`**, resources, the compiled Friend Manager view, and actual DI registration with storage/API dependencies replaced by fakes. It does not construct `App` or enter application startup. Keep this test last because WPF allows only one Application per process.
- **Do not instantiate `App` for a test and then pump its Dispatcher.** Its constructor schedules normal startup, even without explicitly calling `Run`. An initial smoke-test iteration exposed this; the final harness avoids that path.

## TDD record

The first behavior test was executed before production implementation. Actual first RED output:

```text
FAIL Only explicitly selected recipients are invited after confirmation: Current-instance friend invite workflow is missing
0 passed, 1 failed
```

It then passed with the minimal workflow. Subsequent behavior slices were added and observed RED before their production changes: confirmation-time instance change, transport outcome accounting, cancellation/pacing, overlapping batches, empty selection, validated log reading, offline/stale sessions, transitions/disconnects, service integration, mutable confirmation destination, final HTTP guard, guard-refusal accounting, Friend Manager integration, production factory, compiled view/DI integration, busy-action exclusion, per-friend results, duplicate recipients, session-reader failure, and pre-cancellation. Additional regression cases verify already-supported behavior: HTTP payload/failure semantics, missing destination, declined confirmation, and instance changes during a send.

## Published artifact probe (no application startup)

After building the harness and publishing, the following Bash command executes the actual published Windows apphost and its bundled runtime, then exits from a test-only startup hook **before application Main**:

```bash
DOTNET_STARTUP_HOOKS="$(cygpath -m "$PWD")/tests/InstanceInviteTests/bin/Release/net8.0-windows/win-x64/InstanceInviteTests.dll" VRCGT_ARTIFACT_SMOKE=1 ./src/bin/Publish/VRCGroupTools.exe
```

On Windows Bash, use an absolute `C:/...` hook path if `pwd` returns an MSYS `/c/...` path. Expected marker: `ARTIFACT_SMOKE_PASS`. The hook is present only in the test assembly, not in the published application. This verifies executable/runtime loading, not a full login-window or live VRChat session.

## Runtime limitations

The current API user-details model does not expose an authoritative current-user location. This workflow therefore fails closed unless one VRChat process is running and its current log is fresh (last write within two minutes), belongs to that process session, and contains a completed join (`OnJoinedRoom`) after the latest joining location. Unsupported log formats, silent sessions, uncertain process identity, rotation, and transitions can intentionally make the destination unavailable.

The confirmed world/instance identifiers are pinned; changes abort the rest of the batch. Revalidation happens after pacing and again immediately before HTTP dispatch. There is no atomic transaction with the VRChat client: an instance can change after dispatch, and an in-flight request cannot be recalled. Transport exceptions mean **Unknown**, not a proven rejection; uncertain recipients are deselected and are never retried automatically. Invites do not bypass instance access restrictions.

Real VRChat integration and the modal confirmation interaction require a separate, explicitly authorized manual test. The harness verifies compiled-view bindings without sending invites or showing that modal.
