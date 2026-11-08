# Offline group invitation and moderation regression harness

Run from the VRCGT checkout (Windows/.NET 8):

```bash
dotnet run --project tests/GroupInviteTests -c Release
dotnet run --project tests/InstanceInviteTests -c Release
dotnet build src/VRCGroupTools.csproj -c Release
```

On the implementation host, `dotnet` was not on PATH. The SDK used was
`C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe`.
`TMPDIR` must point to the Hermes scratch directory for isolated SQLite fixtures.

The harness uses the real API service with fake HTTP handlers and real SQLite in
an isolated fixture database. It never logs in, starts production `App`, or sends
live invitations/bans. The compiled-view check constructs a plain WPF
`Application` with controlled resources, not the production application.

## Coverage and test-first slices

Observed RED then GREEN during implementation for missing central persistence,
unknown membership being selected as a non-member, non-member filtering,
strict UUID routing, group/identity-pinned ban confirmation, caller exclusion
from group member lists, account changes at dispatch, stale hidden selection,
malformed/whitespace IDs, undocumented conflict outcomes and completion storage
failure. The harness additionally exercises:

- Persistence across new ledger/API service instances, exact-group scoping,
  cross-entrypoint central suppression and concurrent in-flight claims.
- Sent, Failed, Unknown, InFlight and NotSent distinctions; no automatic retry
  for transport loss, 5xx, conflict or rate limiting. Explicit retries are allowed
  only for documented rejection outcomes or proven unsent requests.
- Selected/visible/eligible recipients only, explicit exact-group/identity
  confirmation, cancellation, group changes during confirmation, no invitations
  on refresh/reload, and immediate history/eligibility updates after sends.
- Complete versus partial/missing membership caches, zero membership N+1 calls,
  persisted snapshots and the member-list endpoint's excluded caller.
- Exact `usr_<UUID>` and bare UUID lookup routing, input preservation and
  validation before transport, independence from loaded member pagination,
  and distinct not-found/API-error results.
- Ban permission denial, pinned user/group identity, cancellation and group
  changes during confirmation. Clicking a Game Log user only opens actions.
- Compiled WPF command/filter/history bindings, visible scan/filter controls,
  and zero HTTP during Game Log view loading.

Some cross-entrypoint integration assertions inspect production call sites;
service/ledger, Game Log selection/confirmation and ban workflow assertions
exercise production behavior. No live-service acceptance is claimed.

## Data and safety decisions

- New tables `GroupInviteLedger` and `GroupMembershipSnapshots` live in the
  existing `%LOCALAPPDATA%/VRCGroupTools/vrcgrouptools.db`. They are created lazily
  with additive `CREATE TABLE IF NOT EXISTS`; no legacy tables are changed.
- The ledger key is the exact group ID + recipient ID. It deliberately spans
  sending accounts: changing accounts is not a reason to duplicate a group
  invitation. Instance invitations never use this ledger.
- Atomic SQLite upsert claims are persisted before dispatch. An interrupted
  InFlight claim remains blocked across reloads; an unknown outcome is never
  automatically retried. History is read-only: there is deliberately no unsafe
  reset/retry button for uncertain requests. Check VRChat to resolve them.
- Legacy `InvitedUsers` rows contain no destination group, and may describe
  instance invitations or failed attempts. They are preserved, not guessed into
  this exact-group ledger. Historical invites before this feature cannot be
  retroactively deduplicated reliably.
- Non-member status means absent from the last successfully completed bulk
  membership snapshot, not a live membership guarantee. The snapshot timestamp
  is displayed. Existing unmarked JSON/database caches prove presence only;
  absent users remain Unknown until a complete explicit member sync. Failed or
  partial fetches do not produce non-member claims. The API excludes its caller
  from list results, so that identity is not inferred to be a non-member.
- Game Log loading only reads local membership/history and resolves profiles;
  group invitations require manual eligible selection and confirmation. The old
  instance-user auto-group-invite-on-refresh path was removed.
- All group invitation UI paths use the central dispatch guard and expose typed
  suppression/uncertainty. Bulk Inviter no longer records group sends into the
  unscoped instance/history table or permits disabling duplicate protection.

## API evidence

Existing write endpoints/payloads were reused; no write endpoint was probed.
The current community-maintained specification (unofficial, not VRChat's official
API contract) was read from its authors' repository:

- https://github.com/vrchatapi/specification/blob/main/openapi/components/paths/groups.yaml
- https://github.com/vrchatapi/specification/blob/main/openapi/components/schemas/GroupPermissions.yaml

It describes group invites as POST `/groups/{groupId}/invites`, group bans as POST
`/groups/{groupId}/bans`, `group-bans-manage`/`*` permissions, and member listings
as excluding the caller. UUID lookups reuse GET `/users/{userId}`. Ban workflows
reuse the existing moderation reason dialog, service and API dispatch.
