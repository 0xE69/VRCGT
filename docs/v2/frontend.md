# VRCGT v2 — React Frontend (VRCGT.Web)

## Stack

| Tool | Version | Purpose |
|---|---|---|
| Vite | 6.x | Dev server + production bundler |
| React | 19.x | UI framework |
| TypeScript | 5.8.x | Type safety |
| Tailwind CSS | 4.x (Vite plugin) | Utility-first styles |
| React Router | 7.x | Client-side routing |

---

## Directory layout

```
src/VRCGT.Web/
├── index.html          Vite HTML entry point
├── package.json
├── tsconfig.json
├── vite.config.ts      outDir → ../VRCGT.Server/wwwroot
└── src/
    ├── main.tsx        ReactDOM entry
    ├── App.tsx         Boot logic + route definitions
    ├── index.css       Tailwind @import
    ├── types.ts        DTO types matching backend
    ├── api/
    │   └── client.ts   Typed fetch wrapper
    ├── pages/
    │   ├── SetupPage.tsx     First-run owner creation
    │   ├── LoginPage.tsx     Local login form
    │   ├── DashboardPage.tsx Server status + storage info
    │   └── SettingsPage.tsx  (placeholder — Phase 2)
    └── components/
        ├── Layout.tsx   Sidebar + TopBar + <Outlet>
        ├── Sidebar.tsx  Nav links
        └── TopBar.tsx   User info + logout
```

---

## Development workflow

### Prerequisites
- Node.js ≥ 18 (v20 or v22 recommended)
- The VRCGT.Server must be running on port 57432

### Start Vite dev server

```bash
cd src/VRCGT.Web
npm install      # first time only
npm run dev      # http://localhost:5173
```

The Vite dev server proxies `/api` and `/health` to `http://127.0.0.1:57432`.
Hot-module replacement is active — edits to `.tsx` / `.ts` reflect instantly
without a full page reload.

Start the backend in a separate terminal:

```bash
cd src/VRCGT.Server
dotnet run
```

### Type-check without building

```bash
npm run typecheck
```

---

## Production build

```bash
cd src/VRCGT.Web
npm run build
```

Vite outputs the bundle directly to `src/VRCGT.Server/wwwroot/` (`emptyOutDir: true`
wipes the directory first). After building, `dotnet run --project src/VRCGT.Server`
serves the React app at `/`.

### Integrated .NET build

The VRCGT.Server `.csproj` contains a `BuildWebFrontend` MSBuild target that runs
`npm run build` automatically before the .NET compilation:

```bash
dotnet build VRCGroupTools.sln          # builds frontend + backend
dotnet build /p:SkipWebFrontend=true    # skip npm build (backend only)
```

The `NpmInstall` target runs `npm install` automatically when `node_modules`
is absent (fresh clone, CI).

---

## Routes

| Path | Component | Guard |
|---|---|---|
| `/setup` | `SetupPage` | Only reachable when `setupRequired: true` |
| `/login` | `LoginPage` | Redirects to `/app/dashboard` if already authed |
| `/app/dashboard` | `DashboardPage` | Requires valid session cookie |
| `/app/settings` | `SettingsPage` | Requires valid session cookie |
| `*` | — | Redirects to the appropriate entry point |

The boot sequence in `App.tsx`:
1. `GET /api/auth/me` — if 200, go to `/app/dashboard`
2. `GET /api/setup/status` — if `setupRequired: true`, go to `/setup`
3. Otherwise go to `/login`

---

## API client

`src/api/client.ts` exports an `api` object with typed methods:

```ts
api.setup.status()                              // GET /api/setup/status
api.setup.createOwner(user, pass, displayName)  // POST /api/setup/owner
api.auth.login(user, pass)                      // POST /api/auth/login
api.auth.logout()                               // POST /api/auth/logout
api.auth.me()                                   // GET  /api/auth/me
api.system.storage()                            // GET  /api/system/storage
api.system.version()                            // GET  /api/v2/version
```

All requests use `credentials: 'same-origin'` so the `vrcgt_session` cookie
is automatically included. Errors surface as `ApiError` instances with `status`
and `code` properties.

---

## Phase 2 additions (planned)

- VRChat OAuth login attached to the local session
- Members list, ban management, calendar event panels
- Settings page (LAN mode toggle, account management)
- SignalR live-update integration
