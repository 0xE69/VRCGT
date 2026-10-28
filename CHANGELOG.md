# Changelog

## [1.1.11] - 2026-10-28

### Fixed

- **In-app version string drift causing perpetual update loop** (`App.xaml.cs`)

  The v1.1.10 release shipped with the hardcoded `App.Version` string still set to "1.1.8" — the version bump existed only as an uncommitted working-tree change. The auto-updater compares the in-app string against the latest release tag, so every v1.1.10 install reported itself as v1.1.8 and perpetually saw an "available update" to v1.1.10, i.e. itself.

  *Fix:* `App.Version` now matches the actual release (1.1.11), and all version locations (`App.Version`, csproj `Version` / `AssemblyVersion` / `FileVersion`) are committed together so the string can never silently drift again.

---

## [1.1.10] - 2026-05-11

### Fixed

- **Auto-updater causing 0xc0000142 crash after update** (`UpdateService.cs`)

  The batch script written by the updater used Unix `\n` line endings. Windows `cmd.exe` does not reliably parse Unix line endings, causing the `copy` command to be skipped or malformed — leaving the destination executable empty or corrupted. When that corrupted exe was launched, Windows reported error 0xc0000142 (DLL initialization failed / invalid image).

  Additional hardening applied in the same fix:
  - The batch script is now written with ASCII encoding and Windows CRLF line endings.
  - A 4-second initial delay is added before the first copy attempt, ensuring the old process has fully exited before the file is touched.
  - The `.NET` single-file bundle extraction cache (`%TEMP%\.net\VRCGroupTools\`) is deleted before the new exe is launched, preventing stale/mismatched native DLLs from a previous version causing startup failures.
  - A minimum-size check on the downloaded ZIP validates the file before extraction.

---

## [1.1.9] - 2026-05-11

### Fixed

- **Discord webhooks not sending after saving settings** (`DiscordWebhookService.cs`)

  Three bugs were causing Discord webhook notifications to be silently dropped even when valid webhook URLs were configured in Discord Settings.

  1. **`IsConfiguredForGroup` only checked the legacy field.**  
     `GroupConfiguration.DiscordWebhookUrl` is a legacy migration field. The Discord Settings UI saves webhook URLs to the per-category fields (`DiscordWebhookMemberEvents`, `DiscordWebhookRoleEvents`, etc.). Because `IsConfiguredForGroup` only checked the legacy field, it always returned `false` for groups configured through the current UI, causing `SendModerationActionAsync` to exit silently.  
     *Fix:* Now checks all per-category webhook fields, the legacy field, and the global webhook as a last resort.

  2. **Global webhook fallback was gated on `groupId` being empty.**  
     In `SendMessageAsync`, the fallback to `Settings.DiscordWebhookUrl` was guarded by `&& string.IsNullOrEmpty(groupId)`. Since every group event carries a `groupId`, the global webhook was never reached as a fallback, silently dropping messages when no per-group webhook was set.  
     *Fix:* The global webhook is now tried whenever no group-level webhook resolves, regardless of whether a `groupId` was supplied.

  3. **`GetWebhookUrlForEventType` returned `null` instead of the global webhook.**  
     When neither the per-category nor the legacy per-group webhook was set, the method returned `groupConfig.DiscordWebhookUrl` (null), rather than falling through to the global settings webhook. `SendAuditEventAsync` then logged "Webhook not configured" and returned without sending.  
     *Fix:* The method now chains: category webhook → legacy per-group webhook → global settings webhook.
