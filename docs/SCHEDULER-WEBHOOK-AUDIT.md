# Scheduler and open-instance Discord audit

## Scope and safety boundary

Development verification is offline: no production App startup, login, real user database/session, VRChat mutation or Discord delivery. Packaging is now authorized: version 1.1.20, self-contained win-x64 publish and local installer build. No installed/running app or Desktop checkout replacement, commit, push or public release. Existing invite/moderation/instance work is preserved.

**Important behavior boundary:** calendar Save and news Save are **local drafts**, not implicit uploads/publication. Scheduled checks prepare durable approval-only drafts. The integrated Calendar UI now loads scheduled drafts into an editable news preview and offers separate explicit event/news approval commands; existing Group Posts create/update also uses that approval boundary. `ApprovalPublisher` pins exact content/image/account/group/action/payload, checks independent permissions and timezone, makes atomic durable per-effect claims, uploads only after approval, and reads back exact returned targets. Edits/session/group changes/expiry invalidate approval, partial success retains handles, and unknown/in-flight/unverified effects stay blocked. This workflow is offline-tested, **not live-accepted**. See `PUBLICATION-SETUP.md` for setup, contract evidence, restrictions and outcome handling. Draft preparation is never described as publication.

## Architecture and defects found

- There was no standalone post scheduler. `CalendarEventService` generated recurrence and ran event-linked post automation. Automation rules existed in the service/viewmodel but had no editor in CalendarEventView.
- Its constructor generated 60 days of recurrences, rewrote schedule files and started a repeating async timer. Merely resolving DI could activate persisted publishing rules. Construction now loads only; local draft checks start explicitly and restart paused.
- Timer callbacks could overlap; rule execution was only recorded after transport calls and a later save. A crash/timeout could repeat side effects; null post responses were still marked executed. Scheduled checks now have no external side effects; durable draft identities are serialized and checked under a process gate plus an exclusive cross-process file lock, and written atomically. Existing executed-rule history is respected.
- `SendToDiscord` was incorrectly reused as the VRChat sendNotification argument. The scheduler no longer invokes that publishing path. Existing rule values remain saved, but cannot opt into unattended publishing.
- Trigger checks used machine-local `ToUniversalTime`, ignoring the event's selected timezone. They now use the selected zone; DST gaps/folds and invalid zones fail closed. Old after-end rules could flood on restart; drafts older than 24 hours are withheld.
- Recurrence dedup ignored group, did not dedup the current generation batch, and copied enabled recurrence to every generated child. It now scopes by group/name/zone/time, dedups the batch, uses the selected zone/clock/horizon, skips DST-invalid/ambiguous occurrences and makes generated children non-seeds. Existing stored events are not deleted or migrated on startup.
- Save swallowed persistence errors, and the UI could report success after a failed publication. Local writes now propagate errors, use atomic replacement, refuse overwrite after corrupt load, and the UI reports local save failure and clears busy in finally.
- Draft and webhook checks support cancellation; logout/group selection pauses both. No scheduler or new webhook loop starts in a constructor or verification harness.

## Integrated editor completion

Saved calendar events now have an **Open draft** action that actually reaches the approval editor. The editor exposes exact group and timezone controls. Independent date and strict HH:mm clock bindings compose a wall-clock DateTime without replacing the picked date with today; invalid clocks and DST gaps/folds block approval. Timezone identity is also included in the approval digest and preview. Calendar includes a read-only durable per-effect history panel, so accepted/uncertain image/publication handles can be inspected without reapproval or sending. Draft cards do not incorrectly label previously attempted items as unpublished.

## Open-instance announcement behavior

Discord Settings has a separate masked destination/draft opt-in card. Select the exact group, paste the webhook into the PasswordBox, explicitly enable local drafts and save, then Start checks. **Background checks never send.** Refresh status shows ready previews. Review ready messages resolves the exact webhook/channel/server, shows exact content/account/group/audience/no-image/no-mention notification choice/time, and requires approval of EACH message in a modal. The instance and destination are read again after confirmation; changed context/destination, closure, expiry or cancellation prevents dispatch. Unchecking/Disable revokes opt-in; Pause is session-only. Group changes/logout stop checks and clear the unsaved password. Existing already-open public instances may become ready drafts, not automatic sends. Restart requires Start again.

- Destination is stored in `instance-announcements.db` under the local VRCGroupTools application-data directory as Windows CurrentUser DPAPI ciphertext with group-specific entropy. It is not a settings field, event export field, log message or displayed saved URL. Legacy audit-webhook settings are separate and are not migrated by this feature.
- Only exact `https://discord.com/api/webhooks/{snowflake}/{token}` URLs are accepted. No query, fragment, userinfo, arbitrary host, non-HTTPS, version variant, thread destination or redirects. The production transport has redirects disabled, bounded buffered responses and a timeout.
- Reads current `GET groups/{groupId}/instances`, then `GET instances/{worldId}:{fullInstanceId}` using the existing authenticated API transport and pacing. The old lossy group-instance DTO is not used. Listing summaries alone cannot prove open/closed/access state.
- Group-public only: exact group marker and owner, group access public, full matching location, active=true, explicit closedAt=null and roleRestricted=false. Missing/inconsistent/stale/restricted/member/plus/private states are withheld. Duplicate/conflicting group/access modifiers fail closed. Normal unowned public instances are not inferred to belong to the group.
- Background checks are at least 2 minutes apart, including Pause/Start toggles; a poll cannot overlap itself. Explicit Review may refresh immediately but still honors durable 429 backoff and needs new per-item approval. Pausing, logout, account/group/configuration changes and cancellation are revalidated before dispatch. An already accepted request cannot be recalled.
- Durable SQLite primary key is exact group + world + full instance identity + destination fingerprint. An atomic Unknown claim precedes sending. Restart/concurrent services cannot resend it. Timeout/5xx/malformed successful response stays Unknown; failed readback retains accepted ID as SentUnverified. No automatic retry of either.
- Send uses `wait=true` and disallows everyone/users/roles/reply mentions. The returned message ID is stored before GET readback; matching ID/content/channel/webhook establishes Confirmed.
- A proven 429 requires another explicit per-item approval after max(minimum polling interval, Retry-After, retry_after). Backoff persists across restart. Deleted/unauthorized/forbidden webhook destinations are disabled until explicitly reconfigured. Redirects and other rejections are not replayed.
- Absence/error/incomplete data never means closed. No closure messages are sent and dedup history is not erased when an instance disappears or closes. Even a later reopening of the exact same full identity does not reannounce to the same destination. A different full identity/world/destination gets a separate delivery identity.
- Instances that cannot be authoritatively verified are intentionally not announced. Group lists over 50 entries are withheld as bounded/incomplete work, not advertised as empty. Forum/thread webhooks are unsupported by design.

## Offline verification

SDK: `C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe`.
Run from `C:/Users/mail/Projects/VRCGT`:

```bash
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --project tests/PublicationTests -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --project tests/SchedulerWebhookTests -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --project tests/GroupInviteTests -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --project tests/InstanceInviteTests -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' run --project tests/GroupMetadataRegression -c Release
'C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe' build src/VRCGroupTools.csproj -c Release
```

The new harness uses scratch directories, fake HTTP handlers, injected clock/context and temporary SQLite only. It exercises the real scheduler service, actual VRChat GET adapter, Discord send/readback adapter, DPAPI roundtrip, cancellation/restart/concurrency/backoff and compiled Calendar/post-scheduler/Discord settings bindings under a plain WPF Application. It never constructs production App. The STA dispatcher is shut down on its owning thread to avoid WPF weak-event finalizer failures. Binding errors are captured and asserted absent. The compiled approval window and Calendar event/news/save/review commands, plus Discord per-item review binding, are exercised. MaterialDesign expander animations are allowed to complete before template-button assertions; an idle-only pump was intermittently too early.

Observed RED cases before fixes: missing isolated scheduler construction; missing durable approval-only draft capability; missing open-instance workflow; recurrence batch duplicate count 8 rather than 6; calendar Save reached transport without approval; save failure left IsBusy set; conflicting access modifiers were accepted; deleted webhook retried for another instance; Pause/Start bypassed pacing. Final counts are reported by actual command output, not assumed in this document.

Existing nullable warnings in legacy `VRChatApiService.cs` group-instance parsing can appear on a cold rebuild; they predate the announcement adapter. Incremental build may report zero warnings. Do not conflate that with warning cleanup.

## Source evidence (retrieved during audit)

- Discord official webhook resource: https://docs.discord.com/developers/resources/webhook
- Discord official rate limits: https://docs.discord.com/developers/topics/rate-limits
- Official source used when web extraction was unavailable: https://github.com/discord/discord-api-docs/blob/main/developers/resources/webhook.mdx and https://github.com/discord/discord-api-docs/blob/main/developers/topics/rate-limits.mdx
- **Unofficial/community-maintained VRChat API schema/SDK**, not official VRChat API support: https://github.com/vrchatapi/vrchatapi-python/blob/main/docs/GroupInstance.md and https://github.com/vrchatapi/vrchatapi-python/blob/main/docs/Instance.md
- Community-generated route evidence: https://github.com/vrchatapi/vrchatapi-python/blob/main/vrchatapi/api/groups_api.py (`get_group_instances`) and https://github.com/vrchatapi/vrchatapi-python/blob/main/vrchatapi/api/instances_api.py (`get_instance`).

These sources establish documented contracts, not live acceptance. No selected group/destination was supplied, so secure in-app configuration, read-only live detail availability, actual Discord channel permissions/delivery/readback and any separately approved VRChat publication remain live acceptance gates.
