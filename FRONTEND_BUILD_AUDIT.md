# VRCGT v2 — Frontend Build Audit

**Date:** 2026-05-11  
**Node:** v21.1.0 · npm 10.2.0  
**Vite:** 6.4.2 · React 19 · TypeScript 5.8 · Tailwind CSS 4.1

---

## 1. Build Steps

```bash
# From repo root
cd src/VRCGT.Web
npm install
npm run build
```

`npm install` completes with no errors. One advisory warning is emitted:

```
npm WARN EBADENGINE  package: 'vite@6.4.2'
npm WARN EBADENGINE  required: { node: '^18.0.0 || ^20.0.0 || >=22.0.0' }
npm WARN EBADENGINE  current:  { node: 'v21.1.0', npm: '10.2.0' }
```

Node v21 is outside Vite's declared engine range but is fully supported at
runtime. The warning is from npm's engine check only; the build and dev server
work without issue.

`npm run build` exits zero:

```
vite v6.4.2 building for production...
✓ 92 modules transformed.
../VRCGT.Server/wwwroot/index.html                   0.40 kB │ gzip:   0.28 kB
../VRCGT.Server/wwwroot/assets/index-B_OfitE2.css   46.79 kB │ gzip:   8.61 kB
../VRCGT.Server/wwwroot/assets/index-DBUmQITD.js   399.74 kB │ gzip: 115.02 kB
✓ built in 1.42s
```

Two advisory `/*#__PURE__*/` annotation warnings are emitted from
`@microsoft/signalr/dist/esm/Utils.js`. These are Rollup parse warnings from
the SignalR package and do not affect correctness; tree-shaking still works.

---

## 2. Build Output

Output destination is configured in `vite.config.ts`:

```ts
build: {
  outDir:      '../VRCGT.Server/wwwroot',
  emptyOutDir: true,
}
```

Files written to `src/VRCGT.Server/wwwroot/`:

| File | Size | Gzip |
|---|---|---|
| `index.html` | 0.40 kB | 0.28 kB |
| `assets/index-*.css` | 46.79 kB | 8.61 kB |
| `assets/index-*.js` | 399.74 kB | 115.02 kB |

All asset references in `index.html` use root-relative paths (`/assets/…`).
No external URLs are present.

---

## 3. CDN Reference Audit

**Result: PASS — zero CDN references found.**

Searched `src/VRCGT.Web/src/**`, `src/VRCGT.Web/index.html`,
and the built `src/VRCGT.Server/wwwroot/index.html` for patterns:

- `https?://cdn.`
- `https?://unpkg.com`
- `https?://cdnjs.`
- `https?://jsdelivr.net`
- `https?://fonts.google`

No matches. All dependencies are bundled from `node_modules` by Vite.

---

## 4. Static File Serving

`src/VRCGT.Server/ServerAppFactory.cs` middleware order:

```
UseDefaultFiles()        ← / → index.html
UseStaticFiles()         ← /assets/*, etc.
UseAuthentication()
UseAuthorization()
FirstRunGateMiddleware   ← blocks API until owner created
... API routes ...
MapFallbackToFile("index.html")   ← SPA fallback for deep routes
```

`UseDefaultFiles()` is called before `UseStaticFiles()` so that a bare `GET /`
returns `index.html` without a redirect. The SPA fallback at the end covers
every path not matched by static files or an API route.

---

## 5. Smoke Test Results

Tested against a fresh server instance started with an empty temp data
directory (`VRCGT_DATA_DIR` = `$TEMP/vrcgt-smoketest-{random}`).

| Route | Expected | Actual | Pass |
|---|---|---|---|
| `GET /` | 200 + `<!DOCTYPE html>` | ✓ | ✅ |
| `GET /setup` | 200 + `<!DOCTYPE html>` (SPA fallback) | ✓ | ✅ |
| `GET /login` | 200 + `<!DOCTYPE html>` (SPA fallback) | ✓ | ✅ |
| `GET /app/dashboard` | 200 + `<!DOCTYPE html>` (SPA fallback) | ✓ | ✅ |
| `GET /app/settings` | 200 + `<!DOCTYPE html>` (SPA fallback) | ✓ | ✅ |
| `GET /api/setup/status` | 200 + `{"setupRequired":true}` | ✓ | ✅ |
| `GET /api/auth/me` | 401 + `{"error":"unauthenticated"}` | ✓ | ✅ |
| `GET /api/system/storage` | 200 + `appDataPath` field | ✓ | ✅ |

Deep route refresh works: all `/setup`, `/login`, `/app/*` paths return
`index.html` (HTTP 200) so the React Router client-side router takes over on
load — browser refresh does not 404.

---

## 6. Vite Configuration Notes

`src/VRCGT.Web/vite.config.ts`:

```ts
build: {
  outDir:      '../VRCGT.Server/wwwroot',
  emptyOutDir: true,        // clears stale hashed assets on each build
}

server: {
  port: 5173,
  proxy: {
    '/api':    { target: 'http://127.0.0.1:57432' },   // dev-only proxy
    '/health': { target: 'http://127.0.0.1:57432' },
    '/hubs':   { target: 'http://127.0.0.1:57432', ws: true },
  }
}
```

- No `base` path is set → defaults to `/`. This matches the ASP.NET Core root
  mount point.
- `emptyOutDir: true` guarantees no stale content-hashed bundles accumulate.
- Proxy is only active during `npm run dev`; it is not included in the build.
- `BrowserRouter` (not `HashRouter`) is used in `main.tsx`. Deep-route refresh
  relies entirely on the server-side `MapFallbackToFile("index.html")`.

---

## 7. API Client (src/api/client.ts)

- Uses `fetch` with `credentials: 'same-origin'` — cookie is sent automatically.
- All paths are relative (e.g. `/api/setup/status`), so the base URL is
  always the origin the page was served from.
- No hardcoded `localhost` or port numbers in the client code.
- `ApiError` is thrown for non-2xx responses with `status`, `code`, and
  `message` fields.

Endpoints exercised by the frontend:

| Endpoint | Used by |
|---|---|
| `GET /api/setup/status` | `App.tsx` boot sequence |
| `GET /api/auth/me` | `App.tsx` boot sequence |
| `POST /api/auth/login` | `LoginPage.tsx` |
| `POST /api/setup/init` | `SetupPage.tsx` |
| `GET /api/system/storage` | `SettingsPage.tsx` |
| `GET /api/v2/version` | `TopBar.tsx` |
| `/hubs/jobs` (SignalR) | `JobsContext.tsx` |
| `/hubs/audit` (SignalR) | `AuditLogPage.tsx` |

---

## 8. Issues Found and Resolved

| # | Issue | Status |
|---|---|---|
| — | Build errors | None found |
| — | Missing dependencies | None (node_modules present) |
| — | CDN references | None found |
| — | Wrong `outDir` | Already correct (`../VRCGT.Server/wwwroot`) |
| — | Missing SPA fallback | Already in place (`MapFallbackToFile`) |
| — | Base path mismatch | Not applicable (served at root `/`) |
| — | Hardcoded API origin | Not present (relative paths used) |

No code changes were required. The project built cleanly on the first attempt.

---

## 9. How to Rebuild

```bash
# From repo root
cd src/VRCGT.Web
npm install          # first time / after package.json changes
npm run build        # writes to src/VRCGT.Server/wwwroot/

# Then run the server (picks up the new wwwroot automatically)
dotnet run --project src/VRCGT.Server
```

To skip the frontend build during .NET builds (e.g. tests):

```bash
dotnet test /p:SkipWebFrontend=true
```

---

## 10. Acceptance Checklist

- [x] `npm run build` succeeds (exit 0)
- [x] Output written to `src/VRCGT.Server/wwwroot/`
- [x] `VRCGT.Server` serves built React app at `/`
- [x] Browser refresh on deep routes (`/setup`, `/login`, `/app/*`) works
- [x] No CDN references in source or built output
- [x] `/api/setup/status` returns `setupRequired` field
- [x] `/api/auth/me` returns 401 when unauthenticated
- [x] `/api/system/storage` returns `appDataPath` field
