# Bulk Group Inviter

A feature in the **Inviter Hub** that lets you invite a whole list of VRChat users to your
group in one run — either by **pasting** a list or by **importing a text file** from disk.

## How to use

1. Open the **Inviter Hub** → **Bulk Inviter** tab.
2. Set your **Group ID** (same group you'd invite to from the other inviter tabs).
3. Load the list, two ways:
   - **📂 Load File** — pick a `.txt` (or `.csv`) file with one user ID per line.
   - **Paste** — drop the IDs straight into the text box (one per line, or comma/space separated).
4. Toggle the filters you want (see below).
5. Hit **🚀 Run Bulk Invite**. A results grid shows exactly what happened per user.

User IDs look like `usr-1a2b3c...`. Duplicates are removed automatically.

## Dedupe (built-in database)

The feature reuses the app's existing SQLite **`InvitedUsers`** table:

- **Skip already invited (DB)** (on by default) — any ID already in the database is skipped,
  so re-running the same list never double-invites.
- Every successful invite is **recorded** in the database (display name, trust level, 18+,
  invite time), so it's skipped on any future run — even from a different list.

## Filters (same semantics as the Instance Inviter)

| Filter | Default | Meaning |
|---|---|---|
| Skip already invited (DB) | on | Skip IDs already in the `InvitedUsers` database |
| Only invite 18+ verified | on | Skip users without VRChat age verification |
| Skip new users | off | Skip `system_trust_basic` (New User) |
| Skip users | off | Skip `system_trust_known` (User) |
| Skip known users | off | Skip `system_trust_trusted` (Known User) |

Trust level is read from the user's VRChat `system_trust_*` tags, classified exactly the way
the Instance Inviter does: **Visitor / New User / User / Known User / Trusted User**.

## Gotchas

- **Rate limiting** — there's a 500 ms delay between invites. Large lists can still hit VRChat
  rate limits; failed invites show as `✗ Failed` in the results grid (not fatal).
- **Invalid / deleted IDs** — an ID that can't be fetched shows as `✗ Failed` with a detail line.
- **File format** — `.txt` or `.csv`, one ID per line is the cleanest. The parser also splits
  on commas / spaces / tabs / semicolons, but keep the file to IDs only.
- **18+ filter uses `IsAgeVerified`** from the profile — if a user hasn't verified age, they're
  skipped when the filter is on.

## Files touched

- `src/ViewModels/BulkInviterViewModel.cs` — new. List parsing, **file import** (`LoadFromFile`),
  dedupe, filters, invite loop, per-user results.
- `src/Views/BulkInviterView.xaml` + `.xaml.cs` — new. UI (list box, **Load File** button, filters, results grid).
- `src/ViewModels/InviterHubViewModel.cs` — added the 5th tab (Bulk Inviter) to the hub.
- `src/Views/InviterHubView.xaml` — 5th tab column / button / data template.
- `src/App.xaml.cs` — DI registration for `BulkInviterViewModel`.
- `src/Services/VRChatApiService.cs` — `GetUserAsync` now also surfaces `FriendCount` and a
  best-effort `CreatedUtc` (used by the trust/new-user heuristics).

## Build

```
dotnet build src/VRCGroupTools.csproj -c Debug
```

Verified: **0 errors** (14 pre-existing null-reference warnings, unrelated to this feature).
