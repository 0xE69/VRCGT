# Changelog

## [1.1.14] - 2026-10-30

### Added

- **Bulk text-file Inviter** (new `Bulk Inviter` tab in the Inviter Hub)

  Import a list of VRChat user IDs from a text/CSV file on disk (or paste), dedupe against the built-in SQLite `InvitedUsers` DB (skip-already-invited on by default), apply the app's new/user/known/18+ and trust-level filters, and send group invites in one run. See `BULK_INVITER.md`.
  - `📂 Load File` button opens a file dialog (`.txt` / `.csv`) and loads the IDs into the same pipeline as paste.
  - `VRChatApiService`: added `FriendCount` + `CreatedUtc` to user details for richer per-user info.

### Notes

- Bump version to 1.1.14 across `App.xaml.cs`, `VRCGroupTools.csproj`, `setup.iss`.

---

## [1.1.13] - 2026-10-28

### Fixed

- **Group Posts / write requests sent without the API key** (`VRChatApiService.cs`)

  `CreateGroupPostAsync`, `UpdateGroupPostAsync`, `SendGroupInviteAsync` and `CreateInstanceAsync` all POSTed/PUT to the VRChat API **without** the `?apiKey=*** query parameter. The working read methods (`GetGroupPostsAsync`, `DeleteGroupPostAsync`, `CreateGroupEventAsync`) append it, but these four write methods did not — so the request went out unauthenticated and VRChat rejected it. This is why "make a post" silently failed.

  *Fix:* each write method now appends `?apiKey=*** to its request URL, matching the working methods. `CreateGroupPostAsync` no longer sends a `null` `roleIds` array when none are supplied (mirroring the existing `CreateInstanceAsync` guard).

- **Group Posts UI reported success on failure** (`GroupPostsViewModel.cs`)

  `CreatePostAsync` fired the create request without awaiting/inspecting the result, so the status bar always showed "Post created!" even when the API returned `null` (rejected). It now awaits the result and shows a clear "Post failed - see log" message when the API returns nothing.

- **Post / invite / create-instance requests now logged to the file** (`VRChatApiService.cs`)

  The post, invite and create-instance write methods now call `LogHttp` so the raw request URL and API response body are written to `%LOCALAPPDATA%\VRCGroupTools\Logs` — the same path the working methods already used. Makes it easy to diagnose any future write failure.

- **Auto Closer: public (and other) instances never closed — access type parsed as "unknown"** (`VRChatApiService.cs`)

  The group-instances endpoint does **not** surface the group access type as a top-level field — it only carries it inside the `location` string (e.g. `wrld_x:123~group(grp_y)~groupAccessType(public)~region(us)`). The old parser only read `item["groupAccessType"]` / `item["accessType"]`, so every group instance fell through to `null` → displayed as **"unknown"** → the Auto Closer's safe default (keep unknown) meant a **public** instance was never closed even when only Group / Group+ was allowed.

  *Fix:* new `ExtractAccessType` helper checks the top-level `groupAccessType`/`accessType` field, then regex-parses `groupAccessType(...)` out of the `location` string, then falls back to the `type` field. A parsed value is now logged per instance (`GROUP-INSTANCES`) for easy verification.

- **Auto Closer: region always "unknown" — region now parsed from the location string** (`VRChatApiService.cs`)

  Same root cause as above: `region` is only present inside the `location` string (`~region(us|eu|jp)`), not as a top-level field, so `instance.Region` was always `"unknown"`. Previously harmless (region left empty = allow all), but it would have broken the new region checkboxes. New `ExtractRegion` helper checks the top-level `region` field, then regex-parses `region(...)` from `location`, then defaults to `"unknown"`.

- **Auto Closer: Allowed Regions + Allowed Access Types are now checkboxes** (`AutoCloserView.xaml`, `AutoCloserViewModel.cs`)

  Replaced the two comma-separated text boxes with clear checkbox groups — **Access Types** (Group / Group+ / Public / Friends / Private) and **Regions** (US / EU / JP). Checked boxes are the allowed set; anything else is closed. Leaving all unchecked = allow all. The ViewModel bridges between the checkboxes and the existing comma-separated settings, so saved settings stay backward-compatible (no `SettingsService` changes).

---

## [1.1.12] - 2026-10-28

### Added

- **Auto Closer: allowed access types rule** (`AutoCloserService.cs`, `AutoCloserViewModel.cs`, `AutoCloserView.xaml`)

  New "Allowed Access Types" option in the Auto Closer. Enter a comma-separated list of access levels to allow (e.g. `members,plus` for Group / Group+ only) — any instance with a different access level (e.g. **public**) is automatically closed, with the same logging and Discord webhook notification as the existing rules.

  - Accepts both the app's vocabulary (`members`, `plus`) and VRChat's raw values (`group`, `group_fof`) interchangeably; also supports `friends` and `private`.
  - Leave empty to allow all access levels (rule off) — fully backward compatible.
  - Instances with an unknown/missing access type are never closed by this rule.
  - The instance list now shows a color-coded **Access** badge on every instance (Public = red, Group / Group+ = green) so you can see exactly what the Auto Closer will target at a glance.

---

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
