# Approval-first publication and secure setup (1.1.20)

## Secure setup

Use the app's own secure login/2FA UI. Do not paste passwords, one-time codes, auth cookies or webhook URLs into chat, source, command lines or issue reports. Select the exact group in the sidebar; the approval dialog pins the account ID and exact group ID, not a guessed group name or branding profile. This implementation accepts user-authored content/artwork and never invents approved branding.

No production startup, session lookup, real database migration or live action was performed during development verification. The packaged build does not replace the installed app or the Desktop checkout. Review the artifact manifest before choosing to install/run it yourself. Existing update/repository settings elsewhere in the app have not been retargeted or publicly released.

## Calendar events

1. Open Calendar. Use **Open draft** on a saved event, or create a new **local** event, choose the exact group, explicit timezone and local start/end, platforms, category, languages/tags, audience and notification choice.
2. Choose an optional local PNG. Image browsing is local; it never uploads. Supported artwork is a single-frame PNG, at most 10 MiB and 4096×4096. Other formats must be converted outside this workflow. No permanent group header is changed.
3. **Save local event draft** remains local, including recurrence generation. Each recurring occurrence has its own identity and needs its own approval. Refreshing/syncing/checking never publishes.
4. Load a saved event into the editor and choose **Preview and approve ONE calendar event**. It is a create operation, not an update. Already-linked remote events cannot be recreated by this command. To create a genuinely new occurrence, use Duplicate and review it independently.
5. Review the whole modal: account/group/action, full title/body, exact PNG, audience, unrestricted role audience, requested member notification, and exact UTC dates/payload. Correct DST gaps/folds before proceeding; a timezone is not inferred from an instance. Check the review acknowledgement and approve THIS ONE item, or Cancel.

The news approval does not approve an event, and event approval does not approve news/Discord. There is no combined automatic publisher. Deleting an event from Calendar now removes the local copy only; it does not delete a remote calendar event.

## Scheduled and manually authored news

- Rules are disabled by default. Enable a rule for local draft preparation only, then explicitly Check due or Start local checks. Restart is paused. No timer publishes VRChat news or events.
- Ready drafts appear in the Calendar scheduler expander. **Review in news editor** loads the exact draft/group locally and sends nothing. The news editor also supports a blank user-authored draft and **Save news locally**; reopen from the ready list after restart.
- Choose group/public audience and notification choice explicitly (defaults: group, no notification). An old remote artwork ID is not treated as a visual preview: choose a local PNG or explicitly check the omit-artwork option. Clear PNG makes the preview artwork-free.
- **Preview and approve ONE news post** opens the same exact-content modal. The existing Group Posts create/update editor also uses that modal; updates pin the existing post ID and explicitly replace content/artwork with the preview. Its text-only update preview has no artwork and therefore removes any old image. Use Calendar's news editor for new posts with a PNG.
- Edits to content, image bytes, selected account/session/group, audience, notifications or event payload invalidate approval. Consent expires after five minutes. Permissions are read independently (`group-announcement-manage` for news, `group-calendar-manage` for calendar), then revalidated after pacing immediately before each mutation. Group owners and wildcard roles are supported.

## Separate durable effects and outcomes

`%LocalAppData%/VRCGroupTools/publication-effects.db` is a lazy-created local SQLite ledger. A unique atomic claim precedes each upload and each news/event mutation; multiple app processes cannot bypass an active/uncertain claim for the same exact group + draft + action. It stores digest/state/target handles, not credentials or full draft bodies. Keep this file when backing up/restoring the app; deleting it defeats duplicate protection. The Calendar **Durable publication history** expander reads per-effect state/digest/handles for the exact selected group without opening approval or sending anything. Ready draft cards do not infer "not published" when an earlier effect may already exist.

- `Confirmed`: the exact returned target was read back and matched. Upload verification checks exact file ID, acting owner, complete FileVersion and API MD5 of approved PNG bytes. Publication compares exact ID/group, title/body, image, audience, role restrictions and event payload/date fields.
- `SentUnverified`: an accepted target ID exists but exact readback failed/mismatched. The ID is retained; do not create again.
- `Unknown`: timeout, disconnect, malformed response, potentially side-effecting HTTP error, or uncertain completion. No automatic retry and no retry by reopening the same draft.
- `InFlight`: a durable claim is active or was interrupted. It remains blocking across restart; it is not an expiring lease.
- `Rejected`: a proven rejection (including 429). The publisher does not automatically retry this either. This conservative release keeps attempted draft identities blocked; fix permissions/rate/storage and manually reconcile in VRChat before authoring a genuinely new item. Do not copy an uncertain draft to bypass this protection.
- `NotSent`: cancelled/invalidated/permission-denied before the mutation; separate from an API rejection. If a claim already existed, the ledger conservatively keeps it rather than silently clearing it.
- `NotRequired`: no artwork upload requested.

A confirmed upload and rejected/unknown publication is **partial success**. The image handle remains visible in the report and ledger; it is not deleted, uploaded again or treated as a confirmed post. Changed drafts after attempted creation stay blocked. News updates may accept a new explicitly approved digest only after a previously Confirmed update; uncertain updates block further changes to that target. No implicit rollback is performed.

Notification flags are write-only in the readback contracts: the preview shows exactly what is requested; `Confirmed` does **not** prove member notification delivery. Group news has no documented single-post GET: bounded pagination of `groups/{groupId}/posts` finds the exact returned notification ID; absence, missing fields or normalization mismatch remains unverified. Readback limits are conservative, not permission to retry.

## Optional open-instance Discord announcements

This is a separate disabled-by-default feature, not a scheduler publishing action. In Discord Settings select the exact group, paste the destination in the masked box, explicitly opt in and save; start only when ready. Pause is session-only; disabling revokes opt-in. Group switch/logout stops checks. Checks prepare previews of already-open verified group-public instances, never send. Refresh status shows ready content; Review ready messages resolves the exact webhook/channel/server and opens a separate explicit approval modal for EACH message. After approval, the instance, destination and account/group/session are rechecked. Cancel/expiry/closure/destination change blocks sending. Restart is paused; 429 requires another fresh approval after backoff. Configure the destination securely in-app; the token is stored under CurrentUser DPAPI with group-specific entropy, outside event/settings exports. Do not enable it until separately checking that the chosen public audience and destination are appropriate. Missing private/member/plus/closed/stale details are withheld. Delivery uses wait=true, no mentions, exact message readback and durable dedup. See the audit for privacy, retry and scope details.

## Contract evidence and live gates

Retrieved from the **unofficial, community-maintained** `vrchatapi/specification`, commit `ce2712a61e20e4349110d4061b78667bf59d75c3`:

- `openapi/components/paths/files.yaml`: `POST /file/image`, `GET /file/{fileId}`; `requests/UploadImageRequest.yaml` requires PNG `file` and explicit `tag` (`gallery`). No guessed alternate upload endpoints.
- `paths/calendar.yaml`: `POST /calendar/{groupId}/event` then `GET /calendar/{groupId}/{calendarId}`. `requests/CreateCalendarEventRequest.yaml`: explicit `isDraft=false`, notification choice, dates/audience/category/platforms/languages/tags; schema category names include `exploration` and `film_media`.
- `paths/groups.yaml`: create news via `POST /groups/{groupId}/posts`, update via `PUT /groups/{groupId}/posts/{notificationId}`, read group posts via GET and match the exact returned ID.
- `schemas/GroupPermissions.yaml`, `schemas/CalendarEvent.yaml`, `schemas/GroupPost.yaml`, `schemas/ImagePurpose.yaml` supply the permission/payload/readback fields.
- Source root: https://github.com/vrchatapi/specification/tree/ce2712a61e20e4349110d4061b78667bf59d75c3/openapi/components

These are source contracts, not official VRChat support or live acceptance. Offline fake-transport and plain-WPF tests exercise the actual integrated workflow, but actual secure login, selected group's effective rights, upload normalization/quotas, live create/update/readback, notification behavior, Discord channel delivery and installed GUI startup remain separately approved live acceptance gates. Never run a test upload/post/event/invite/ban/webhook without specific live authorization.
