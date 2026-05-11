# VRCGT.Web — React + TypeScript Frontend (Phase 0 Placeholder)

This folder will contain the Vite + React + TypeScript frontend for VRCGT v2.

## Status

**Phase 0 placeholder.** The React project will be initialized in Phase 1.

## Planned Stack

| Layer | Technology |
|---|---|
| Framework | React 18 + TypeScript |
| Build tool | Vite 5 |
| Component library | shadcn/ui (Radix UI + Tailwind CSS) |
| Data fetching | TanStack Query v5 |
| Client state | Zustand |
| Routing | React Router v7 |
| Real-time | @microsoft/signalr |
| Forms | React Hook Form + Zod |
| Tables | TanStack Table v8 (virtualized) |
| Icons | Lucide React |

## Build Integration

When initialized, `pnpm build` will output to `dist/`.
An MSBuild target in `VRCGT.Server.csproj` will copy `dist/` → `VRCGT.Server/wwwroot/`
so the React app is embedded into the server binary at publish time.

## Development Proxy

During development, Vite's dev server runs on `http://localhost:5173`.
`vite.config.ts` will proxy `/api` and `/hubs` to `http://127.0.0.1:57432`
so the frontend can communicate with the local server with hot module replacement.

## See Also

- `WEB_LOCAL_APP_MIGRATION_PLAN.md` §7 — Frontend Plan
- `docs/v2/architecture.md` — Overall local-first architecture
