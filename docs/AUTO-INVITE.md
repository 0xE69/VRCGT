# Instance Inviter Auto Invite — 1.1.22

Current-client marker compatibility and bounded scan diagnostics are documented in `releases/1.1.22-LOG-MARKERS.md`. Logs remain untrusted hints; the API corroboration and final dispatch guards below are unchanged. Initial scans are synchronous and can pause the UI; no freeze-free claim is made.

This feature sends **group invitations only to API-corroborated, eligible occupants of the current VRChat instance**. It does not cover all occupants: non-friend/private/hidden/unavailable locations are skipped, even if a log claims they joined. Logs are untrusted discovery hints, not presence authority. Manual explicit recipient review remains available. It does not invite friends to an instance, ban anyone, upload artwork or publish posts/events.

## Use

1. Sign in securely, select the exact target group, join a permitted current VRChat instance, and refresh the group's complete member cache. A missing/partial/untimestamped cache fails closed; cached non-membership is not live certainty.
2. Open Instance Inviter. Set age/trust/search/minimum-time filters, then select **Auto Invite (every 5 seconds)**.
3. Review the confirmation's exact group ID, acting account, complete current instance and eligibility filters. Approve once to begin this narrowly scoped session. There are no per-recipient dialogs in this explicitly enabled mode.
4. Local detection runs every five seconds without overlapping polls. One candidate preparation/request may run at a time; local checks continue while detail lookup or HTTP waits. Group sends are at least thirteen seconds apart, and central API pacing/Retry-After still applies.
5. Unchecking Auto Invite stops it. Account/logout/group/client-session/instance changes (including rejoining the same location), editing filters, invalid filter text, leaving the view or shutting down revoke consent. Re-enable and confirm again; no automatic restart/resume or saved true setting exists.

## Eligibility and local history

- Exact valid logged user IDs only. Names are never guessed or resolved by selecting a first search match. Missing IDs remain unresolved and skipped.
- Self, cached members, unknown membership, filtered-out users and unknown/too-short join times when a minimum-time filter is enabled are skipped.
- Shared SQLite `GroupInviteLedger` records exact `(group ID, recipient user ID)`, state and UTC timestamp centrally **before** HTTP. Default location is `%LOCALAPPDATA%/VRCGroupTools/vrcgrouptools.db`; this is the existing group-wide ledger, not a parallel automatic-only database.
- Sent, InFlight and Unknown suppress duplicate group invitations across restarts and all manual/automatic entrypoints. An automatic claim also refuses Failed. Explicit known-Failed manual retry remains separate. No uncertain request is automatically retried.
- A rejection/uncertain outcome stops Auto Invite for review, rather than retrying every five seconds. Cancellation after dispatch is Unknown because the server may already have accepted it. Cancellation/context loss before dispatch is NotSent.
- Current row status, the always-visible status line, Auto Invite activity panel and existing Game Log local history expose outcomes. The on-screen activity panel is bounded; durable recipient outcomes are retained in the shared ledger.

## Efficiency and limitations

The session reader validates one running process, fresh logs and a completed join, then consumes appended complete lines. These rows are only hints: multiline Udon text can forge even a timestamped logger envelope. Membership/history/filters run before presence lookup. For each new candidate in the explicitly confirmed session, a read-only `GET users/{id}` through the existing authorized client must return that exact `id`, `state=online`, and `location` exactly equal to the pinned world and full instance ID, with no nonempty travel destination. No profile/bio/status claim, guessed identity, last-known location, count or log record substitutes for that evidence.

Presence reads are at least thirteen seconds apart (independent from send pacing), bounded by a ten-second timeout. Evidence is valid at most twenty seconds from lookup start, including API pacing/network time. Context, authentication generation, local departures and evidence freshness are rechecked after send pacing immediately before HTTP. Too-old evidence fails closed instead of being refreshed in a send loop. Each candidate is checked once per explicitly confirmed session, including unknown/error results; unchanged polls make no API requests. Multiple new candidates are serviced over successive paced ticks, not blanket N+1 reads.

The documented User location is optional/privacy-limited, with `offline` returned for non-friends; `active` means web-only, not in VRChat. Thus automatic coverage is limited to API-visible online locations, normally visible friends. Hidden/private/non-friend/unavailable/expired evidence stays skipped, with pending/unknown row explanations and explicit manual review remaining available. There is no all-occupant claim. The community Instance schema documents a `users` roster only for instances created by the requesting user; that restricted alternative is not implemented or assumed. For broader automatic coverage, a separately reviewed creator-roster architecture or different trusted source is required. See `releases/1.1.21-PRESENCE-FIX.md` for pinned schema evidence (community, not official).

Local departure hints can conservatively remove occupants; they cannot independently authorize a send. Filter metadata is separately cached and fetched only when enabled filters require it, at most one new lookup per automatic cycle. Explicit manual refresh still resolves missing filter details without enabling Auto Invite; subsequent refreshes reuse the cache. The stricter presence policy does not alter the shared profile-filling contract or explicit/manual invitation eligibility.

Another group's instance is blocked in automatic mode, including when an unverified world-owner exception might otherwise apply. Quiet/stale logs (over two minutes), unsupported join/user formats, missing IDs, multiple clients or incomplete cache prevent invitations rather than using historical evidence. The five-second schedule is local detection, not a promise of five-second HTTP sends or hard real-time Windows scheduling. API membership can change after the cache snapshot. A request already accepted by VRChat cannot be recalled.

Offline fake HTTP/temp SQLite/log/clock tests and compiled WPF/DI checks do not establish live invitation delivery or installed-app acceptance. No real session, production App startup, installed binary replacement or live invitation was used for verification.
