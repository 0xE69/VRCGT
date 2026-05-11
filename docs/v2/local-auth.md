# VRCGT v2 — Local Authentication

## Overview

VRCGT v2 has its own local user account system that is entirely separate from
VRChat.  A local account is required to access the web console.  The first
account (the Owner) is created during first-run setup.

**Nothing here talks to the internet.**  Credentials are stored and verified
locally only.  VRChat login (OAuth / cookie-based) is a separate flow added in
Phase 2 and builds on top of a local session.

---

## Account roles

| Role | Access |
|---|---|
| `Viewer` | Read-only access to all views |
| `EventManager` | Viewer + manage calendar events and instance invites |
| `Moderator` | EventManager + access to moderation tools (bans, kick, warn) |
| `Admin` | Moderator + manage group members, manage local accounts |
| `Owner` | Full access; can promote/demote other accounts; exactly one per install |

The Owner role is assigned to the first account created during setup and cannot
be removed or transferred (until a future "transfer ownership" feature is added).

---

## First-run setup flow

On every startup `ServerAppFactory.CreateAsync()` runs the database migrations.
When the database is empty the `FirstRunGateMiddleware` intercepts all
`/api/*` requests except `/api/setup/*`, `/api/v2/*`, and `/api/system/*`,
and returns `503 { error: "setup_required" }`.

```
GET  /api/setup/status   → { setupRequired: true|false }
POST /api/setup/owner    → { username, password, displayName }
                           creates Owner; 409 if Owner already exists
```

Once the Owner is created `setupRequired` is `false` and all endpoints become
accessible (subject to normal authentication).

---

## Authentication flow

```
POST /api/auth/login    body: { username, password }
                        response: 200 LocalUserDto + sets vrcgt_session cookie
                                  401 on bad credentials

GET  /api/auth/me       requires cookie → 200 LocalUserDto or 401
POST /api/auth/logout   clears cookie → 200
```

### Cookie properties

| Property | Value | Reason |
|---|---|---|
| Name | `vrcgt_session` | — |
| HttpOnly | `true` | Prevents JavaScript from reading the cookie (XSS mitigation) |
| SameSite | `Strict` | Cookie is never sent in cross-site requests (primary CSRF protection) |
| Secure | `false` (localhost) | localhost is plain HTTP; browsers exempt localhost from the Secure requirement |
| Expiry | 30 days, sliding | Persistent session; refreshed on each authenticated request |

SameSite=Strict is sufficient CSRF protection for a localhost-only server
because any external page trying to hit `127.0.0.1` would be a cross-origin
request and the browser would not attach the cookie.

---

## Password hashing

`Microsoft.AspNetCore.Identity.PasswordHasher<LocalUser>` (v3 format):

- **Algorithm:** PBKDF2 with HMAC-SHA512
- **Iterations:** 100 000 (OWASP-recommended minimum as of 2024)
- **Salt:** 128-bit cryptographically random, embedded in the hash string
- **Output:** 97-character base-64 string, self-describing (no separate salt column)

Plain-text passwords are never stored or logged.  The hash is never returned
in any API response.

---

## Security constraints

| Constraint | Detail |
|---|---|
| Bind address | `127.0.0.1` only — no LAN or public access by default |
| No HTTPS | Localhost HTTP is standard for local dev tools; browser exempts localhost from mixed-content rules |
| Generic login errors | "Invalid username or password" — does not reveal whether the username exists |
| No rate limiting (yet) | Planned for Phase 2; acceptable at localhost where only the local user can attempt login |
| No secrets in DB | VRChat cookies/tokens are in-memory only; platform credential stores planned for Phase 2 |

---

## Planned Phase 2 additions

- Rate limiting on `/api/auth/login` (even for localhost, as a best practice)
- VRChat OAuth / cookie login attached to a `LocalUser` session
- `server.lock` file for duplicate-instance detection (open existing session instead of starting a second server)
- Optional per-user 2FA (TOTP) for Admin/Owner accounts
