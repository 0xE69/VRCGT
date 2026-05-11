# VRCGT Feature Parity: v1 WPF → v2 Web

**Last updated:** 2026-05-11  
**Legend:**  
`✅ Done` · `🔄 Partial` · `📋 Planned` · `⛔ Not planned` · `➡ Merged`

---

## Table of Contents

1. [Authentication & 2FA](#1-authentication--2fa)
2. [Group Dashboard](#2-group-dashboard)
3. [User Search](#3-user-search)
4. [Invite to Group](#4-invite-to-group)
5. [Inviter Hub](#5-inviter-hub)
6. [Instance Inviter](#6-instance-inviter)
7. [Friend Inviter](#7-friend-inviter)
8. [Game Log Scanner](#8-game-log-scanner)
9. [Join Requests](#9-join-requests)
10. [Calendar Events](#10-calendar-events)
11. [Group Posts](#11-group-posts)
12. [Instance Creator](#12-instance-creator)
13. [Members List](#13-members-list)
14. [Bans List](#14-bans-list)
15. [Audit Logs](#15-audit-logs)
16. [18+ Badge Scanner](#16-18-badge-scanner)
17. [Kill Switch](#17-kill-switch)
18. [Member Backup & Recovery](#18-member-backup--recovery)
19. [Security Monitor](#19-security-monitor)
20. [Instance Auto Closer](#20-instance-auto-closer)
21. [Discord Webhooks](#21-discord-webhooks)
22. [App Settings](#22-app-settings)
23. [Auto Update](#23-auto-update)
24. [Local Cache / Database](#24-local-cache--database)
25. [Logs / Crash Reports](#25-logs--crash-reports)

---

## 1. Authentication & 2FA

| Field | Value |
|-------|-------|
| **v1 status** | ✅ WPF login form; credential/session stored in local DB; TOTP 2FA prompt in-app |
| **v2 status** | ✅ Done |

### Backend endpoints
| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/api/vrchat/login` | Submits username + password to VRChat API |
| `POST` | `/api/vrchat/2fa` | Submits TOTP/email code when VRChat responds with `requiresTwoFactorAuth` |
| `GET` | `/api/vrchat/session` | Returns current VRChat session state |
| `POST` | `/api/vrchat/logout` | Invalidates VRChat session |
| `POST` | `/api/auth/login` | Local VRCGT login (issues `vrcgt_session` cookie) |
| `POST` | `/api/auth/logout` | Clears local session cookie |
| `GET` | `/api/auth/me` | Returns current local user and permissions |

### Frontend route
`/login` — handles both VRCGT local login and VRChat session linking.  
`/setup` — first-run owner account creation.

### Database entities
- `LocalUser` — stores VRCGT app users (bcrypt password hash, role, enabled state)
- `VrchatSessionStore` — in-process singleton (VRChat auth cookies, not persisted to DB)

### Safety requirements
- VRChat credentials are **never stored on disk**; the session cookie is held in memory only.
- The `vrcgt_session` cookie must be `HttpOnly`, `SameSite=Strict`, 30-day sliding expiry.
- Failed login attempts should not reveal whether the username or password was wrong.
- 2FA code must be consumed in a single stateful flow tied to the in-progress session.

### Cache freshness
- VRChat session state is live; no caching of credentials.

### Multi-user permissions
- All users must authenticate independently.  
- The `ManageLocalUsers` permission is required to create or modify other user accounts.

### Migration notes
- v1 stored the VRChat session token directly in SQLite. v2 keeps it in memory only; users must re-authenticate after a server restart.

### Acceptance criteria
- A new user completing setup can log in to the web UI.
- 2FA prompt appears only when VRChat API requires it.
- After server restart, VRChat re-authentication is required (no stale credentials).
- Local VRCGT session expires after 30 days of inactivity.

---

## 2. Group Dashboard

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Summary card per managed group (member count, online count, group icon) |
| **v2 status** | ✅ Done |

### Backend endpoints
| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/groups/` | List all managed groups |
| `POST` | `/api/groups/` | Add a new group (by groupId) |
| `GET` | `/api/groups/{groupId}` | Get single group detail with freshness metadata |
| `POST` | `/api/groups/{groupId}/refresh` | Force re-sync from VRChat API |
| `DELETE` | `/api/groups/{groupId}` | Remove group from tracking |
| `GET` | `/api/v2/version` | App version + runtime info shown in footer |
| `GET` | `/health` | Liveness check |

### Frontend route
`/app/dashboard`

### Database entities
- `ManagedGroup` — GroupId, Name, ShortCode, IconUrl, BannerUrl, MemberCount, OnlineCount, IsActive, LastRefreshedAt

### Safety requirements
- Group adds require a valid VRChat group ID; the server verifies via the VRChat API before storing.
- Deleting a group removes all cached member data for that group.

### Cache freshness
- Group metadata is stale after 15 minutes; the UI shows a freshness indicator.
- Member counts shown on the dashboard card come from the cached `ManagedGroup` record, not a live API call.

### Multi-user permissions
- `ViewDashboard` required to see the dashboard.
- Adding/removing groups requires elevated permission (Admin+).

### Migration notes
- v1 stores group config in `GroupConfiguration` table. v2 uses `ManagedGroup`; no automated migration of v1 group list (user re-adds groups).

### Acceptance criteria
- Dashboard loads with all tracked groups.
- Refresh button re-syncs member count from VRChat API.
- Stale data age is shown to the user.

---

## 3. User Search

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Search VRChat users by name; view profile, trust rank, badges |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/vrchat/users/search?q={query}` |
| `GET` | `/api/vrchat/users/{userId}` |

### Frontend route needed
`/app/user-search`

### Database entities needed
None (results are live from VRChat API; no caching required).  
Optional: `UserSearchHistory` (recent searches, per-user) — not essential for MVP.

### Safety requirements
- Search results are read-only; no action endpoints on this route.
- Rate-limit search calls to avoid VRChat API bans.

### Cache freshness
- Results are live; no local caching.  
- Optional: cache individual user profiles for 5 minutes to reduce repeated fetches.

### Multi-user permissions
- `ViewMembers` — any user with this permission can search.

### Migration notes
- v1 UserSearchViewModel calls VRChat `/users` endpoint. v2 proxies the same endpoint through the local server.

### Acceptance criteria
- Searching by display name returns VRChat user results.
- Clicking a user shows their profile (trust rank, badges, date joined).
- Results do not persist after session.

---

## 4. Invite to Group

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Search user, send group invite via VRChat API |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `POST` | `/api/groups/{groupId}/invites` — body: `{ userId }` |
| `GET` | `/api/groups/{groupId}/invites` — list pending invites |
| `DELETE` | `/api/groups/{groupId}/invites/{inviteId}` — rescind invite |

### Frontend route needed
`/app/invite-to-group`

### Database entities needed
None (invite state lives in VRChat API; no local storage needed).

### Safety requirements
- Confirmation dialog before sending invites.
- Duplicate invite check: reject if user already has a pending invite or is already a member.

### Cache freshness
- Pending invite list is live from the VRChat API.

### Multi-user permissions
- `InviteUsers` permission required.

### Migration notes
- v1 InviteToGroupViewModel sends individual invites. v2 should support the same single-user flow.

### Acceptance criteria
- Can search for a user and send a group invite.
- Duplicate invites are rejected gracefully.
- Invite appears in VRChat's group invite list for the recipient.

---

## 5. Inviter Hub

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Bulk invitation management; send instance invites to multiple users |
| **v2 status** | 📋 Planned — may be merged with Friend Inviter (see note) |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `POST` | `/api/inviter/instance-invites` — body: `{ userIds[], instanceId }` |
| `GET` | `/api/inviter/queue` — status of pending invite jobs |

### Frontend route needed
`/app/inviter-hub`

### Database entities needed
- Optional: `InviteJob` — tracks bulk invite operations (progress, success/fail per user).  
  Can reuse `BackgroundJob` + `BackgroundJobItem` with `JobType = "inviter"`.

### Safety requirements
- Rate limiting: VRChat API has strict invite rate limits. Server must enforce delays between sends.
- User must confirm before bulk-sending to more than N recipients.
- Respect VRChat API terms: do not spam users.

### Cache freshness
- Instance list and friend list are live; no caching.

### Multi-user permissions
- `InviteUsers` permission required.
- Large bulk invites (> configurable threshold) may require Admin+ role.

### Migration notes
- v1 InviterHubViewModel combines instance inviter and friend inviter. v2 may split these into two routes or keep them on one page with tabs.

### Acceptance criteria
- Can select multiple users and send instance invites in sequence.
- Progress bar shows per-user invite status.
- Failed invites are reported individually without stopping the rest of the batch.

---

## 6. Instance Inviter

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Invite group members into a running VRChat instance |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/vrchat/instances/{instanceId}` — verify instance exists and is accessible |
| `POST` | `/api/groups/{groupId}/instance-invites` — body: `{ instanceId, memberIds[] }` |

### Frontend route needed
`/app/instance-inviter`

### Database entities needed
None (live operation). Reuse `BackgroundJob` for progress tracking if batch is large.

### Safety requirements
- Instance ID must be validated before sending invites.
- Rate limiting enforced server-side to avoid VRChat bans.

### Cache freshness
- Active instances are live; no caching.

### Multi-user permissions
- `InviteUsers` permission required.

### Migration notes
- v1 InstanceInviterViewModel and InstanceInviterService provide the same capability.

### Acceptance criteria
- Enter an instance ID, select group members, send invites.
- Members receive instance invites in VRChat.

---

## 7. Friend Inviter

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Invite VRChat friends to join the group |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/vrchat/friends` — list VRChat friends |
| `POST` | `/api/groups/{groupId}/friend-invites` — body: `{ friendIds[] }` |

### Frontend route needed
`/app/friend-inviter`

### Database entities needed
None (live from VRChat API).

### Safety requirements
- Respect VRChat API rate limits.
- Confirm before sending bulk invites.

### Cache freshness
- Friend list cached for up to 5 minutes to reduce API calls.

### Multi-user permissions
- `InviteUsers` permission required.

### Migration notes
- v1 FriendInviterViewModel fetches friends from VRChat and bulk-invites.

### Acceptance criteria
- Friend list is displayed and filterable.
- Selected friends receive group invites.
- Already-member friends are excluded from the invite list.

---

## 8. Game Log Scanner

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Reads VRChat local game logs for instance events, player joins/leaves, crash detection |
| **v2 status** | ⛔ Not planned for v2 (architectural reason: requires local file system access) |

### Notes
The Game Log Scanner reads `%LocalAppData%\VRChat\VRChat\*.txt` on the user's machine.  
v2's server runs as a local process and **could** access these files when deployed on the same machine as VRChat, but:

- Docker deployments have no access to the VRChat log directory by default.
- The web UI architecture does not guarantee the server is co-located with the VRChat client.
- The feature is inherently desktop-centric.

**If reinstated**, it should be implemented as an optional background service within the Launcher process (not the Server), writing events to a `GameLogEvent` table that the web UI can query.

### Backend endpoints needed (if reinstated)
| Method | Route |
|--------|-------|
| `GET` | `/api/game-log/events` |
| `GET` | `/api/game-log/status` |

### Frontend route needed (if reinstated)
`/app/game-log`

### Database entities needed (if reinstated)
- `GameLogEvent` — Timestamp, EventType, Message, RawLine

### Safety requirements
- Must never read files outside the VRChat log directory.
- PII in log lines (player names) must be treated with the same access controls as member data.

### Multi-user permissions
- `ViewDashboard` minimum; `ViewMembers` recommended.

### Migration notes
- v1 GameLogViewModel tails VRChat log files in real time using a `FileSystemWatcher`.

### Acceptance criteria
- N/A (not planned). Reopen as a separate issue if scope changes.

---

## 9. Join Requests

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Lists pending group join requests; approve or deny with optional message |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/join-requests` |
| `POST` | `/api/groups/{groupId}/join-requests/{requestId}/approve` |
| `POST` | `/api/groups/{groupId}/join-requests/{requestId}/deny` |
| `POST` | `/api/groups/{groupId}/join-requests/bulk-approve` |
| `POST` | `/api/groups/{groupId}/join-requests/bulk-deny` |

### Frontend route needed
`/app/join-requests`

### Database entities needed
None (live from VRChat API). If caching is desired: optional `JoinRequestCache` with 5-minute TTL.

### Safety requirements
- Bulk approve/deny requires a confirmation step.
- Deny action should support an optional reason.

### Cache freshness
- Join request list is live; poll every 30 seconds when the page is open.

### Multi-user permissions
- `ApproveJoinRequests` permission required for approve/deny.
- `ViewMembers` to list requests.

### Migration notes
- v1 GroupJoinRequestsViewModel polls and renders the join request list with VRChat API calls.

### Acceptance criteria
- All pending join requests are listed.
- Approve and deny actions reflect immediately in VRChat.
- Bulk operations complete without partial failure silently dropped.

---

## 10. Calendar Events

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Create, edit, delete VRChat group events; view upcoming events |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/events` |
| `POST` | `/api/groups/{groupId}/events` |
| `GET` | `/api/groups/{groupId}/events/{eventId}` |
| `PATCH` | `/api/groups/{groupId}/events/{eventId}` |
| `DELETE` | `/api/groups/{groupId}/events/{eventId}` — requires `ConfirmationGuard.DeleteEvent` token |

### Frontend route needed
`/app/calendar`

### Database entities needed
None required (live from VRChat API).  
Optional: `GroupEventCache` for offline/stale display.

### Safety requirements
- Delete requires typed confirmation (`DELETE_EVENT`).
- Editing an event with attendees should warn the user.

### Cache freshness
- Event list cached for up to 5 minutes.

### Multi-user permissions
- `ManageEvents` permission required for create/edit/delete.
- `ViewDashboard` to view the calendar.

### Migration notes
- v1 CalendarEventViewModel and CalendarEventService wrap the VRChat events API.

### Acceptance criteria
- Can create and delete a group event.
- Events appear in VRChat group event list.
- Calendar shows upcoming events in chronological order.

---

## 11. Group Posts

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Create, view, delete group posts (announcements); supports image attachments |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/posts` |
| `POST` | `/api/groups/{groupId}/posts` |
| `DELETE` | `/api/groups/{groupId}/posts/{postId}` — requires `ConfirmationGuard.DeletePost` token |

### Frontend route needed
`/app/posts`

### Database entities needed
None (live from VRChat API).

### Safety requirements
- Delete requires confirmation token `DELETE_POST`.
- Image upload size limit enforced server-side.
- Post content is passed through to VRChat API as-is; no server-side HTML rendering.

### Cache freshness
- Post list cached for up to 5 minutes.

### Multi-user permissions
- `ManagePosts` permission required for create/delete.
- `ViewDashboard` to view posts.

### Migration notes
- v1 GroupPostsViewModel wraps the VRChat group post API.

### Acceptance criteria
- Can create a text post and see it appear in the VRChat group feed.
- Can delete a post with confirmation.
- Posts list paginates correctly.

---

## 12. Instance Creator

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Create VRChat world instances with configurable access levels and region |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `POST` | `/api/vrchat/instances` — body: `{ worldId, instanceType, region, groupId? }` |
| `GET` | `/api/vrchat/instances/{instanceId}` |

### Frontend route needed
`/app/instance-creator`

### Database entities needed
None (live operation). Optional: `InstanceHistory` for recently created instances.

### Safety requirements
- Validate `worldId` and `instanceType` against VRChat's accepted values before sending.
- Group-access instances require `groupId` to match a managed group.

### Cache freshness
- World list lookup may be cached for up to 10 minutes.

### Multi-user permissions
- `InviteUsers` or a dedicated `CreateInstances` permission (to be defined).

### Migration notes
- v1 InstanceCreatorViewModel builds instance creation parameters and calls VRChat API.

### Acceptance criteria
- Can create a Friends+ or Group instance from a world ID.
- Created instance is usable in VRChat.

---

## 13. Members List

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Paginated member list with role badges, online status, join date; kick/ban from context menu |
| **v2 status** | ✅ Done (list + search); kick/ban actions planned |

### Backend endpoints
| Method | Route | Status |
|--------|-------|--------|
| `GET` | `/api/groups/{groupId}/members` | ✅ Exists (paginated, search, sort) |
| `GET` | `/api/groups/{groupId}/members/{userId}` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/members/refresh` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/members/{userId}/kick` | 📋 Planned |
| `POST` | `/api/groups/{groupId}/members/{userId}/ban` | 📋 Planned |
| `PATCH` | `/api/groups/{groupId}/members/{userId}/roles` | 📋 Planned |

### Frontend route
`/app/members` — ✅ exists; kick/ban UI 📋 planned

### Database entities
- `GroupMember` — ✅ exists (cached member records with `CachedAt`)

### Safety requirements
- Kick/ban require confirmation dialogs.
- Banning a member should offer reason input (forwarded to VRChat API).
- Cannot kick/ban yourself.

### Cache freshness
- Member list is considered stale after 15 minutes. Refresh button triggers re-fetch.
- `CachedAt` timestamp is shown in the UI.

### Multi-user permissions
- `ViewMembers` — list and view.
- `KickMembers` — kick action.
- `BanMembers` — ban action.

### Migration notes
- v1 MembersListViewModel provides the same paginated view. v2 adds server-side pagination and a persistent cache.

### Acceptance criteria
- Members list renders with roles, join dates, and online status.
- Kick and ban actions succeed and the member list updates immediately.
- Search by display name filters results client-side (cached) or server-side.

---

## 14. Bans List

| Field | Value |
|-------|-------|
| **v1 status** | ✅ List of banned users; unban action |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/bans` |
| `DELETE` | `/api/groups/{groupId}/bans/{banId}` — unban |

### Frontend route needed
`/app/bans`

### Database entities needed
None (live from VRChat API). Optional: `BanCache` with short TTL for faster page loads.

### Safety requirements
- Unban requires confirmation.
- Do not expose the ban reason to the banned user via the UI.

### Cache freshness
- Ban list cached for up to 5 minutes.

### Multi-user permissions
- `BanMembers` permission required for both viewing and unbanning.

### Migration notes
- v1 BansListViewModel fetches bans via VRChat API. v2 proxies the same endpoint.

### Acceptance criteria
- All current bans are displayed with banned user name and ban date.
- Unban action removes the user from the ban list and they can rejoin.

---

## 15. Audit Logs

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Displays VRChat group audit events (kick, ban, role change, invite, etc.) with filters |
| **v2 status** | ✅ Done |

### Backend endpoints
| Method | Route | Status |
|--------|-------|--------|
| `GET` | `/api/groups/{groupId}/audit-logs` | ✅ Exists (paginated, filter by action/date) |
| `POST` | `/api/groups/{groupId}/audit-logs/fetch-history` | ✅ Exists (backfill from VRChat) |
| `GET` | `/api/groups/{groupId}/audit-logs/export` | ✅ Exists (CSV download) |

### Frontend route
`/app/audit` — ✅ exists

### Database entities
- `AuditLogEntry` — ✅ exists (VRChat and local sources, `RawJson`, `FetchedAt`)

### Safety requirements
- Audit logs are append-only; no delete endpoint.
- Local VRCGT events (user management, Kill Switch) are written with `Source = "vrcgt"` and cannot be tampered with via the API.
- CSV export access requires `ExportData` permission.

### Cache freshness
- New VRChat audit entries are fetched on demand via `fetch-history`.  
- Entries older than the last fetch are served from the local database.

### Multi-user permissions
- `ViewDashboard` — read audit log.
- `ExportData` — export CSV.

### Migration notes
- v1 AuditLogViewModel queries VRChat API directly on each load. v2 stores entries locally for faster access and offline history.

### Acceptance criteria
- Last 100 audit events are visible on page load from cache.
- Fetch-history imports older events.
- CSV export downloads all stored events for the group.
- VRCGT-originated events (Kill Switch, user changes) appear in the log with correct source label.

---

## 16. 18+ Badge Scanner

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Scans group members for age-verified badge; can kick unverified users |
| **v2 status** | ✅ Done |

### Backend endpoints
| Method | Route | Status |
|--------|-------|--------|
| `POST` | `/api/groups/{groupId}/badge-scans` | ✅ Exists (start scan job) |
| `GET` | `/api/groups/{groupId}/badge-scans/latest` | ✅ Exists |
| `GET` | `/api/groups/{groupId}/badge-scans/{scanId}` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/badge-scans/{scanId}/kick-selected` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/badge-scans/{scanId}/kick-unverified` | ✅ Exists |
| `GET` | `/api/groups/{groupId}/badge-scans/{scanId}/export` | ✅ Exists (CSV) |

### Frontend route
`/app/badge-scanner` — ✅ exists

### Database entities
- `BadgeScan` — ✅ exists (scan job result)
- `BadgeScanMember` — ✅ exists (per-member verification result)

### Safety requirements
- Kick-all-unverified requires a typed confirmation phrase before execution.
- Scan results stored locally; do not re-expose raw VRChat user data unnecessarily.
- Scan is read-only until user explicitly triggers a kick action.

### Cache freshness
- Badge verification status reflects the member list at scan time.
- Users verified after the scan started are not re-evaluated until the next scan.

### Multi-user permissions
- `ViewMembers` — run scan and view results.
- `KickMembers` — kick actions.
- `ExportData` — CSV export.

### Migration notes
- v1 BadgeScannerViewModel performs a sequential scan with per-member API calls. v2 runs this as a background job with real-time progress via SignalR.

### Acceptance criteria
- Scan completes for a 500-member group without timeout.
- Results show verified/unverified/unknown counts.
- Kick-unverified removes all unverified members in a single operation.
- CSV export contains all scan results.

---

## 17. Kill Switch

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Removes all roles from selected/all group members in an emergency |
| **v2 status** | ✅ Done |

### Backend endpoints
| Method | Route | Status |
|--------|-------|--------|
| `GET` | `/api/groups/{groupId}/kill-switch/preview` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/kill-switch/refresh` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/kill-switch/execute` | ✅ Exists (confirmation: `REMOVE_ROLES`) |
| `GET` | `/api/groups/{groupId}/kill-switch/snapshots` | ✅ Exists |
| `POST` | `/api/groups/{groupId}/kill-switch/snapshots/{id}/restore` | ✅ Exists (confirmation: `RESTORE_ROLES`) |

### Frontend route
`/app/kill-switch` — ✅ exists

### Database entities
- `KillSwitchSnapshot` — ✅ exists (pre-execution member/role state backup)

### Safety requirements
- Execute requires typed phrase `REMOVE ROLES`.
- Restore requires typed phrase `RESTORE ROLES`.
- Both confirmation tokens validated server-side (not just client-side).
- Only users with `UseKillSwitch` permission can execute or restore.
- Every execution and restore is written to the local audit log.

### Cache freshness
- Preview must be based on a member list no older than 15 minutes; a `refresh` call forces re-fetch.
- The UI gates execution behind a freshness check.

### Multi-user permissions
- `UseKillSwitch` — execute and restore (Admin+ only by default).
- `ViewDashboard` — view snapshot history.

### Migration notes
- v1 KillSwitchViewModel uses the WPF dispatcher and a local DB. v2 uses background jobs and SignalR for progress reporting.

### Acceptance criteria
- Preview shows affected members and roles before execution.
- Execution removes all selected roles and writes a snapshot.
- Restore re-applies role assignments from a snapshot.
- Live progress is visible during execution and restore.

---

## 18. Member Backup & Recovery

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Manual snapshot of full member list; restore from snapshot |
| **v2 status** | 📋 Planned — partially covered by Kill Switch snapshots |

### Notes
Kill Switch snapshots (Stage 14) cover the role-assignment use case.  
Full member backup (with custom display names, join dates, export for external use) is a separate concern.

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `POST` | `/api/groups/{groupId}/member-backups` — create snapshot |
| `GET` | `/api/groups/{groupId}/member-backups` — list snapshots |
| `GET` | `/api/groups/{groupId}/member-backups/{id}/export` — download CSV/JSON |

### Frontend route needed
`/app/member-backup`

### Database entities needed
- `MemberBackup` — Id, GroupId, CreatedAt, MemberCount, Label
- `MemberBackupEntry` — BackupId, UserId, DisplayName, Roles (JSON), JoinedAt

### Safety requirements
- Backups are read-only after creation; no editing of snapshot contents.
- Export requires `ExportData` permission.

### Cache freshness
- Backup is a point-in-time snapshot; contents never change after creation.

### Multi-user permissions
- `ViewMembers` — create backup.
- `ExportData` — download backup.

### Migration notes
- v1 MemberBackupService serialises the member list to a JSON file on disk. v2 stores backups in the SQLite database.

### Acceptance criteria
- Creating a backup captures all current members and their roles.
- Downloading a backup produces a CSV with member ID, display name, roles, and join date.
- Snapshots are listed in reverse chronological order.

---

## 19. Security Monitor

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Watches for suspicious audit events (mass bans, role changes, unknown admins); sends alerts |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/security-monitor/config` |
| `PATCH` | `/api/groups/{groupId}/security-monitor/config` |
| `GET` | `/api/groups/{groupId}/security-monitor/alerts` |
| `POST` | `/api/groups/{groupId}/security-monitor/alerts/{alertId}/dismiss` |

### Frontend route needed
`/app/security` (or as a tab within `/app/settings`)

### Database entities needed
- `SecurityAlert` — Id, GroupId, Timestamp, AlertType, Description, ActorId, IsDismissed
- `SecurityMonitorConfig` — GroupId (PK), EnabledAlerts (JSON), ThresholdJson

### Safety requirements
- Alert thresholds must not allow false-positive storms (rate limiting per alert type).
- Dismissed alerts are retained in the log; never deleted.

### Cache freshness
- Security monitor is event-driven; it reacts to new audit log entries, not a polling interval.

### Multi-user permissions
- `ManageSecurity` — configure thresholds, dismiss alerts.
- `ViewDashboard` — view alerts (read-only).

### Migration notes
- v1 SecurityMonitorService watches audit events on a background timer. v2 hooks into the `AuditLogEntry` insert path to evaluate rules immediately.

### Acceptance criteria
- A configured threshold for "5+ kicks in 5 minutes from one actor" fires an alert.
- Alert appears in the UI within 1 minute of the triggering event.
- Dismissing an alert removes it from the active list but retains it in history.

---

## 20. Instance Auto Closer

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Automatically closes (deletes) a VRChat instance when a condition is met (time, empty, etc.) |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/auto-closers` |
| `POST` | `/api/groups/{groupId}/auto-closers` |
| `PATCH` | `/api/groups/{groupId}/auto-closers/{id}` |
| `DELETE` | `/api/groups/{groupId}/auto-closers/{id}` |

### Frontend route needed
`/app/auto-closer`

### Database entities needed
- `AutoCloserRule` — Id, GroupId, InstanceId, TriggerType (Time/Empty/Manual), TriggerValue, Status (Active/Triggered/Cancelled), CreatedAt, TriggeredAt

### Safety requirements
- Rules that trigger a mass close require confirmation.
- Auto-close events are written to the audit log.
- Rules are auto-deactivated after firing to prevent re-triggers.

### Cache freshness
- Instance state is polled on the configured interval (minimum 30 seconds to avoid VRChat API rate limits).

### Multi-user permissions
- `ManageSecurity` or a dedicated `ManageInstances` permission to create/edit rules.
- `ViewDashboard` to see active rules.

### Migration notes
- v1 AutoCloserViewModel and AutoCloserService manage a list of watched instances. v2 stores rules in the database so they survive server restarts.

### Acceptance criteria
- A time-based rule closes the instance at the configured time.
- An empty-instance rule fires when player count drops to 0.
- Fired rules are marked as triggered in the list.

---

## 21. Discord Webhooks

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Configurable webhook URLs; sends audit events, member join/leave, alerts to Discord |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/groups/{groupId}/webhooks` |
| `POST` | `/api/groups/{groupId}/webhooks` |
| `PATCH` | `/api/groups/{groupId}/webhooks/{id}` |
| `DELETE` | `/api/groups/{groupId}/webhooks/{id}` |
| `POST` | `/api/groups/{groupId}/webhooks/{id}/test` — send a test message |

### Frontend route needed
`/app/discord-settings` (or tab within `/app/settings`)

### Database entities needed
- `DiscordWebhook` — Id, GroupId, Url (encrypted at rest), Label, EnabledEvents (JSON flags), IsEnabled, CreatedAt, LastUsedAt

### Safety requirements
- Webhook URLs must be stored encrypted at rest (AES-256 or platform data-protection API).
- URLs are write-only in the UI after initial save; never returned in plaintext via `GET`.
- Test endpoint validates the webhook responds with HTTP 204 before saving.
- Failed deliveries must not retry more than 3 times to avoid Discord rate-limiting.

### Cache freshness
- Webhook config is read from DB on each outbound event; no caching.

### Multi-user permissions
- `ManageWebhooks` permission required for create/edit/delete.
- `ViewDashboard` to see webhook list (URLs masked).

### Migration notes
- v1 DiscordWebhookService stores URLs in plaintext in the SQLite DB. v2 must encrypt at rest using `Microsoft.AspNetCore.DataProtection`.

### Acceptance criteria
- Adding a webhook and clicking Test sends a message to the Discord channel.
- Audit events configured to forward appear in Discord within 30 seconds.
- Webhook URL is not returned in API responses after saving.

---

## 22. App Settings

| Field | Value |
|-------|-------|
| **v1 status** | ✅ WPF settings page: VRChat credentials, startup behaviour, theme, update channel |
| **v2 status** | 🔄 Partial — VRChat session settings done; server access (LAN mode) done; others planned |

### Backend endpoints
| Method | Route | Status |
|--------|-------|--------|
| `GET` | `/api/system/access` | ✅ Exists (LAN bind mode) |
| `PATCH` | `/api/system/access` | ✅ Exists |
| `GET` | `/api/system/storage` | ✅ Exists (data paths) |
| `GET` | `/api/v2/version` | ✅ Exists |
| `GET/PATCH` | `/api/settings/{key}` | 📋 Planned (general key-value settings) |

### Frontend route
`/app/settings` — ✅ exists; `/app/server-access` — ✅ exists  
Additional settings sub-pages 📋 planned.

### Database entities
- `AppSetting` — ✅ exists (key-value store for general preferences)

### Safety requirements
- Sensitive settings (webhook URLs, API tokens) must never be stored in `AppSetting` in plaintext.
- Settings changes should be audit-logged for the keys that affect security posture.

### Cache freshness
- Settings are read from DB on each request; the table is small enough that caching adds no value.

### Multi-user permissions
- General read: any authenticated user.
- Write: `ManageSecurity` for security-affecting settings; Owner for others.

### Migration notes
- v1 SettingsService stores settings in a WPF user settings file. v2 stores them in `AppSetting` table so they are shared across all local users of the web UI.

### Acceptance criteria
- Settings changes persist across server restarts.
- LAN mode change displays a restart-required banner.
- Non-privileged users cannot modify security settings.

---

## 23. Auto Update

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Checks GitHub releases; downloads and applies update on next launch |
| **v2 status** | 📋 Planned |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/updates/check` — returns latest release info |
| `POST` | `/api/updates/apply` — downloads and schedules update (if self-hosted) |

### Frontend route needed
Update notification banner on the dashboard or in Settings.  
Full update page: `/app/settings` (sub-section) or modal.

### Database entities needed
- `AppSetting` with key `updates.lastCheckedAt`, `updates.lastVersion` — reuse existing table.

### Safety requirements
- Downloaded binaries must be verified against a checksum or GitHub release asset hash before applying.
- The apply endpoint must require `Owner` role.
- Auto-apply must never run without explicit user confirmation.

### Cache freshness
- Update check result cached for 1 hour; force-check on Settings page open.

### Multi-user permissions
- `ViewDashboard` — see update notification banner.
- Owner — trigger update.

### Migration notes
- v1 UpdateService polls GitHub API for release tags. v2 can reuse this logic; the packaging format differs (v2 is a self-contained folder, not an installer).
- Docker deployments should not use the in-app updater; users rebuild the image.

### Acceptance criteria
- Update available notification appears in the UI when a newer GitHub release exists.
- Owner can trigger a download and scheduled restart.
- Non-owner users cannot trigger updates.

---

## 24. Local Cache / Database

| Field | Value |
|-------|-------|
| **v1 status** | ✅ SQLite via EF Core; data in `%AppData%\VRCGT\` |
| **v2 status** | ✅ Done |

### Backend
- EF Core 8 + SQLite.
- Migrations applied automatically at startup via `db.Database.MigrateAsync()`.
- Database path: `{VRCGT_DATA_DIR}/vrcgt-v2.db` (default: `%LocalAppData%\VRCGT\v2\data\vrcgt-v2.db`).
- `VRCGT_DATA_DIR` env var overrides the path (used in Docker).

### Database entities
All 10 entities exist: `LocalUser`, `ManagedGroup`, `GroupMember`, `AuditLogEntry`, `BackgroundJob`, `BackgroundJobItem`, `BadgeScan`, `BadgeScanMember`, `KillSwitchSnapshot`, `AppSetting`.

### Safety requirements
- Database file permissions: readable/writable only by the process user.
- No raw SQL in application code; all access through EF Core.
- Migrations are never rolled back automatically; reversions require manual steps.

### Cache freshness
- `GroupMember.CachedAt` tracks member list freshness.
- `AuditLogEntry.FetchedAt` tracks when an entry was retrieved from VRChat.

### Multi-user permissions
- Database is shared across all local VRCGT users; row-level security is enforced at the API layer, not the database layer.

### Migration notes
- v1 and v2 databases are **not** compatible. v2 creates a separate `vrcgt-v2.db` file.  
- No automated migration of v1 data to v2 is planned; users start fresh.

### Acceptance criteria
- Database is created automatically on first run.
- Migrations apply without errors on a clean install.
- Data persists across server restarts.
- `VRCGT_DATA_DIR` override works correctly in Docker.

---

## 25. Logs / Crash Reports

| Field | Value |
|-------|-------|
| **v1 status** | ✅ Serilog rolling file logs in `%AppData%\VRCGT\logs\`; crash dialogs in WPF |
| **v2 status** | 🔄 Partial — log directory provisioned; structured logging via ASP.NET Core; crash surfacing not yet done |

### Backend endpoints needed
| Method | Route |
|--------|-------|
| `GET` | `/api/system/logs` — list recent log files |
| `GET` | `/api/system/logs/{filename}` — download a log file (Owner only) |

### Frontend route needed
Log viewer sub-section in `/app/settings` or `/app/local-users`.

### Database entities needed
None — logs are written to the file system under `{DataPath}/logs/`.

### Safety requirements
- Log files may contain display names and user IDs; access restricted to Owner role.
- Log download endpoint must prevent path traversal (validate filename against the logs directory).
- Crash reports must not include VRChat passwords or session tokens (ensure these are never logged at any level).

### Cache freshness
- Log files are tailed from disk; no caching.

### Multi-user permissions
- Owner only — read and download log files.

### Migration notes
- v1 uses NLog with a rolling file sink. v2 uses Microsoft.Extensions.Logging; add a file sink (e.g. Serilog) to `LogsPath`.
- Crash report dialogs are a WPF concept; v2 surfaces unhandled exceptions as JSON 500 responses with a correlation ID.

### Acceptance criteria
- Application events are written to a rotating log file in `{DataPath}/logs/`.
- Unhandled server exceptions produce a structured error log entry with a request ID.
- Log files are readable only by the process user on the file system.
- A future log-viewer endpoint returns the last N lines of the current log file.

---

## Summary Table

| # | Feature | v1 | v2 | Priority |
|---|---------|----|----|----------|
| 1 | Authentication & 2FA | ✅ | ✅ | — |
| 2 | Group Dashboard | ✅ | ✅ | — |
| 3 | User Search | ✅ | 📋 | Medium |
| 4 | Invite to Group | ✅ | 📋 | Medium |
| 5 | Inviter Hub | ✅ | 📋 | Low |
| 6 | Instance Inviter | ✅ | 📋 | Low |
| 7 | Friend Inviter | ✅ | 📋 | Low |
| 8 | Game Log Scanner | ✅ | ⛔ | — |
| 9 | Join Requests | ✅ | 📋 | High |
| 10 | Calendar Events | ✅ | 📋 | Medium |
| 11 | Group Posts | ✅ | 📋 | Medium |
| 12 | Instance Creator | ✅ | 📋 | Low |
| 13 | Members List | ✅ | 🔄 | High (kick/ban remaining) |
| 14 | Bans List | ✅ | 📋 | High |
| 15 | Audit Logs | ✅ | ✅ | — |
| 16 | 18+ Badge Scanner | ✅ | ✅ | — |
| 17 | Kill Switch | ✅ | ✅ | — |
| 18 | Member Backup | ✅ | 📋 | Medium |
| 19 | Security Monitor | ✅ | 📋 | Medium |
| 20 | Instance Auto Closer | ✅ | 📋 | Low |
| 21 | Discord Webhooks | ✅ | 📋 | Medium |
| 22 | App Settings | ✅ | 🔄 | Medium (general settings remaining) |
| 23 | Auto Update | ✅ | 📋 | Low |
| 24 | Local Cache / Database | ✅ | ✅ | — |
| 25 | Logs / Crash Reports | ✅ | 🔄 | Low |

**Overall v2 parity: 7 / 25 fully done · 3 / 25 partial · 14 / 25 planned · 1 / 25 not planned**
