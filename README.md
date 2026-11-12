# VRC Group Tools (VRCGT)

A Windows desktop application for VRChat group owners and moderators. VRCGT brings group information, member moderation, invitations, events, audit logs, security tools and optional Discord notifications into one WPF interface.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0) [![Windows x64](https://img.shields.io/badge/Platform-Windows%20x64-0078D6)](https://github.com/0xE69/VRCGT/releases) [![MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE) [![Build](https://img.shields.io/github/actions/workflow/status/0xE69/VRCGT/build.yml?branch=main)](https://github.com/0xE69/VRCGT/actions)

**Latest published release: [v1.1.23](https://github.com/0xE69/VRCGT/releases/tag/v1.1.23) (8 November 2026).** See [the complete released changelog](CHANGELOG.md) for version-by-version changes. This README describes the published 1.1.23 feature set; it does **not** announce unreleased development work.

> **Source/version distinction:** The published `v1.1.23` binaries correspond to [the `v1.1.23` release source](https://github.com/0xE69/VRCGT/tree/v1.1.23), commit `0e24d2a74038ce7326efa3448b572fb8dd5ef100`. The default `main` branch currently has a `1.1.18` project version and is **not** the source tree used for that release. Check out the release tag to build or inspect the exact published version.

## Download and install

Choose an artifact from [v1.1.23 Releases](https://github.com/0xE69/VRCGT/releases/tag/v1.1.23):

| Artifact | Purpose |
| --- | --- |
| `VRCGroupTools_Setup_1.1.23.exe` | Windows installer |
| `VRCGroupTools-v1.1.23-win-x64.zip` | Portable, self-contained Windows x64 application |
| `VRCGroupTools-v1.1.23-win-x64.zip.sha256` | Integrity checksum for the ZIP |

1. Download from the release page. If using the ZIP, extract it first and run `VRCGroupTools.exe`; otherwise launch the installer.
2. Sign in to VRChat and complete any required two-factor authentication.
3. Select the correct VRChat group. Group-owner or moderator permissions are needed for actions that change group membership, roles or content.
4. Review permissions, group settings and invitation filters before starting any automated action.

**Windows requirements:** Windows 10 or 11, 64-bit. Release packages are self-contained for .NET 8; a separate .NET Desktop Runtime installation should not be required for those assets. Windows and security software may warn about the **unsigned** installer/executable. Check that the file came from the intended GitHub release and verify its checksum before deciding whether to run it. A checksum verifies file integrity, **not** publisher identity or safety.

**Updating:** VRCGT checks the public GitHub release feed for stable updates. Close the application before replacing files. If an automatic replacement is blocked by permissions or endpoint policy, install the release manually. An update check or successful offline test does not guarantee every Windows installation will update successfully.

## Features

### Group management and moderation

- **Dashboard:** Group information, membership statistics, roles, rules, gallery and scheduled events.
- **User search and profiles:** Look up users by username or user ID, inspect profile details and perform permission-checked group actions.
- **Members and bans:** Review members, manage roles, kick or ban users, and unban where authorised. User Search also supports banning a non-member from a group.
- **Join requests and invitations:** Review join requests, look up recipients, and use available age-verification and trust-level filters.
- **18+ Badge Scanner:** Review age-verification status, filter/export results and initiate explicit moderation actions.
- **Kill Switch and recovery:** Snapshot role assignments before bulk removal and use the restore controls for saved role snapshots.
- **Member backups:** Save group membership snapshots, compare changes and re-invite missing users. Re-invitation is not a guarantee that VRChat will restore a previous member or their roles.
- **Security Monitor:** Configurable moderation thresholds, trusted-user exemptions, incident records and optional security-alert webhooks.

### Inviter Hub and Game History (v1.1.23)

- **Manual group invitations:** Invite selected users from retained current or past Game History encounters, even after a room change, VRChat client exit or when recipients are offline/private. These are **group invitations**, not invitations to join an instance.
- **Auto GROUP Invite:** An explicitly enabled option with confirmation of the acting account, target group, history/filter scope and optional future local discovery. It starts **disabled after every restart** and stops when consent or relevant account/group/context is changed.
- **Pacing and duplicate protection:** Local discovery checks run every five seconds, but API operations and invitation sends are independently paced. A shared persistent group/recipient ledger suppresses duplicate and uncertain invitations; failed/unknown membership or identity checks do not silently authorise sending.
- **Local discovery limits:** The application retains up to 1,000 encounter records, using a bounded scan of supported VRChat log formats. Logs provide untrusted discovery hints, **not** authenticated proof that a user was encountered. Imported lists do not automatically expand the consented Auto GROUP Invite scope.
- **Other invite tools:** Instance Inviter, Friend Inviter, Game Log Scanner and text/CSV bulk import support manual selection and relevant eligibility filters. Inviting someone **to a specific VRChat instance** still requires a valid, confirmed destination; friend-to-instance batches are cancellable and paced.

For the precise behaviour and constraints, read the [v1.1.23 Auto GROUP Invite documentation](https://github.com/0xE69/VRCGT/blob/v1.1.23/docs/AUTO-INVITE.md) and [release notes](https://github.com/0xE69/VRCGT/releases/tag/v1.1.23).

### Events, instances and communications

- **Calendar and group events:** Create, edit and schedule events with timezone and recurrence controls; review publication requests before publishing.
- **Group posts and announcements:** View and manage posts, with approval-oriented publishing for supported event/news workflows.
- **Instance Creator and Auto Closer:** Create instance links and configure age-gate, region and group-access rules for monitoring/closing non-compliant group instances.
- **Audit logs:** Review moderation and group activity, filter/search records and export available data.
- **Discord integration:** Optional category-based webhooks for group/member/role/instance and other events, security alerts and opt-in instance-announcement previews.
- **Settings and update checks:** Manage group-specific settings, local data options and GitHub release update checks.

## Known limitations and security

- **Log compatibility:** Unsupported VRChat output-log headers can prevent encounter discovery. The unresolved 1.1.22 log-header incident is **not claimed fixed** by v1.1.23. Some initial log scans may temporarily pause the UI.
- **Live acceptance:** Release verification included offline tests with fake transports and disposable data; those results do **not** establish successful live invites, real-client log detection or every installed-app update scenario.
- **Invitation coverage:** Private/hidden identifiers, invalid or ambiguous records, API failures and VRChat permissions can limit actions. VRCGT does not guarantee discovery of every person in a room, invitation delivery or automatic recovery of every group member.
- **Outstanding dependency advisory:** The dependency review identified [CVE-2025-6965 / GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) in the baseline transitive SQLite dependency graph. **This documentation change and the Dependabot cleanup do not remediate it.** Review the advisory and dependency updates before treating a future release as fixed.
- **Unsigned builds:** Installer and executable checksums prove integrity of downloaded bytes but do not replace code signing.

Report problems or check outstanding work in [GitHub Issues](https://github.com/0xE69/VRCGT/issues). Do not publish credentials, tokens, unredacted VRChat logs or group-member personal data in issue reports.

## Build from source

**Prerequisites:** Windows 10/11 x64, Git, and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The project targets `net8.0-windows` and uses WPF.

To build **the published 1.1.23 source**, rather than whatever is currently on `main`:

```powershell
git clone https://github.com/0xE69/VRCGT.git
cd VRCGT
git fetch --tags
git checkout v1.1.23

dotnet restore src/VRCGroupTools.csproj
dotnet build src/VRCGroupTools.csproj -c Release --no-restore
dotnet publish src/VRCGroupTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src/bin/Publish
```

The published application will be built at `src/bin/Publish/VRCGroupTools.exe`. You may also use `build.bat` from the repository root. To produce an installer, publish the application first, then compile `installer/setup.iss` with [Inno Setup](https://jrsoftware.org/isinfo.php).

Building from the same source does **not** by itself reproduce a byte-identical signed or verified release artifact. Compare source/tag provenance and the release-provided hashes as appropriate.

The repository has GitHub Actions for builds; the published v1.1.23 installer/portable ZIP and its verification evidence are described on the [release page](https://github.com/0xE69/VRCGT/releases/tag/v1.1.23).

## Local data and privacy

VRCGT uses the application data folder `%LOCALAPPDATA%\VRCGroupTools\` for settings, its SQLite database, logs and crash reports. Examples include `settings.json`, `vrcgrouptools.db`, `Logs/` and `CrashReports/`.

Some data stays in these local files, but application actions can also contact the **VRChat API**, **GitHub** for updates, and **Discord** if you configure webhooks. Therefore, "no data is sent to third parties" would be inaccurate. Treat the local database, history, logs and webhook configuration as sensitive, and review what each external integration sends before enabling it.

## Contributing, licence and disclaimer

Contributions and bug reports are welcome through [pull requests](https://github.com/0xE69/VRCGT/pulls) and [issues](https://github.com/0xE69/VRCGT/issues). Changes intended for a release should be reviewed and tested separately from development-only notes. Released user-facing changes belong in [`CHANGELOG.md`](CHANGELOG.md), not a second README changelog.

VRCGT is licensed under the [MIT License](LICENSE).

**Unofficial project.** VRCGT is not affiliated with or endorsed by VRChat Inc. Use it in compliance with VRChat's policies and the permissions of the groups you manage.
