# __PROJECT_NAME__ (MALDA Fullstack Template)

This starter is the fastest way to explore MALDA Web and MALDA Workflow/Cloud together: one MALDA app file, one small UI placeholder, baseline tests, and deploy/security config examples.

## Run This First

From the project root:

```bash
malda test
malda backend/app.malda
```

To scaffold the same starter with SQLite/local-first groundwork:

```bash
malda new fullstack sales-portal --local-first
```

Then open:

- API health check: `http://localhost:8080/api/health`
- API auth sample: `http://localhost:8080/api/me`
- Component UI sample: `http://localhost:8081/components/TicketBoard`

If you want machine-readable test output for CI or editor tooling:

```bash
malda test --format ci
```

## What Matters First

- `backend/app.malda`: main MALDA entry point; contains the REST endpoints, auth middleware, component sample, and SSE/live update example
- `frontend/index.html`: static frontend placeholder for teams that want to grow the starter into a broader web app
- `tests/auth.test.malda`: baseline auth/JWT test example for `malda test`
- `tests/security_helpers.malda`: reusable test helpers for security-focused checks
- `config/security.example.json`: example JWT, CSRF, and rate-limit settings to adapt before real deployment
- `config/data.example.json`: local-first/SQLite contract placeholder for teams that want a repeatable data path
- `config/deploy.example.json`: deploy contract placeholder used by `malda deploy`
- `config/observability.example.json`: logs/metrics/health contract placeholder

## What This Starter Includes Today

- A REST API server on port `8080`
- A separate HTTP/component server on port `8081`
- Health, readiness, and metrics endpoints
- A bearer-token-protected `/api/me` example
- A small server-rendered ticket board sample using `component`, `@ACTION`, and `@LIVE`
- Test defaults that work with `malda test`
- Security, environment, deploy, and observability example config files
- Optional `--local-first` scaffold mode that adds SQLite bootstrap, a migration registry seam, and a persisted ticket-board sample

## Web Runtime Surface

- API and web servers support global middleware via `api.use(fn)` or `server.use(fn)`
- Middleware uses the `function middleware(req, res, next)` shape and can either terminate the response or call `next()`
- Existing named parameter binding still works for path/query/body conventions
- You can also receive first-class `req` and `res` objects in handlers

`req` currently exposes:

- Properties: `method`, `path`, `url`, `queryString`, `query`, `params`, `headers`, `cookies`, `body`, `correlationId`, `ip`, `host`, `contentType`, `auth`
- Helpers: `req.header(name, default?)`, `req.queryParam(name, default?)`, `req.param(name, default?)`, `req.cookie(name, default?)`
- Middleware can attach request-scoped values such as `req.user`, `req.pageTitle`, or `req.tenant`

`res` currently exposes:

- `status(code)`, `json(value)`, `text(value)`, `html(value)`, `send(value)`
- `header(name, value)`, `cookie(name, value, options?)`, `clearCookie(name, options?)`, `redirect(location, status?)`

{{#LOCAL_FIRST}}
## Local-First Mode

This project was scaffolded with `--local-first`.

- `backend/data/local_first.malda` bootstraps SQLite, enables WAL/foreign keys, and records applied migrations
- `backend/data/local_first.malda` now includes small idempotent helper seams such as `tableExists()`, `columnExists()`, `ensureColumn()`, `ensureIndex()`, and `ensureLifecycleColumns()`
- The ticket board sample persists tickets in SQLite instead of in-memory component state
- `/api/data/status` exposes the current local data-platform status for development
- `config/data.example.json` documents the intended contract for future DB/migration tooling

CLI workflow today:

- `malda db status` inspects the scaffolded SQLite file and migration registry without mutating it, including latest applied migration, seed/rollback support, and source-vs-database drift
- `malda db migrate` applies the scaffolded local-first migrations using the generated bootstrap seam
- `malda db seed` applies migrations and then inserts idempotent starter rows from `seedLocalDataPlatform()`
- `malda db rollback` rolls back only the latest applied scaffolded migration via `rollbackLocalMigration<ID>()`

Migration discipline for the generated local-first path:

- Add schema changes through `localMigrationRegistry` and matching `runLocalMigration<ID>()` functions only
- Prefer additive, idempotent changes with `ensureColumn()`, `ensureIndex()`, and `ensureLifecycleColumns()` instead of ad hoc startup SQL
- Keep `rollbackLocalMigration<ID>()` for the latest scaffolded migration if you want `malda db rollback` to stay available

Current limits:

- `seed` and `rollback` currently follow the generated local-first module conventions instead of arbitrary layouts
- `rollback` needs `openLocalDataPlatform()` plus a matching `rollbackLocalMigration<ID>()` function in the module
- `rollback` only targets the latest applied migration that still exists in `localMigrationRegistry`
{{/LOCAL_FIRST}}

## Current Baseline Vs. Next Steps

This scaffold is a production-minded baseline, not a complete production app.

Baseline today:

- The sample demonstrates MALDA routing, auth context wiring, CSRF enablement, rate limiting, and server-driven UI fragments
- The generated config files show where security, profile, deploy, and observability settings belong
- The ticket board is intentionally small so you can replace it quickly with your own domain logic

Still up to you:

- Replace `change-me-*` secrets before any shared or deployed environment
- Add real authentication flows, persistent data storage, and domain-specific authorization
- Expand the placeholder frontend or replace it with the UI approach your app needs
- Fill in deploy and observability contracts with environment-specific values
- If you use `--local-first`, evolve the generated migration registry rather than editing schema ad hoc

## Sample Endpoints

- API server: `http://localhost:8080`
- Component UI server: `http://localhost:8081/components/TicketBoard`
- Live updates endpoint: `http://localhost:8081/components/tickets/live?channel=tickets`

## Notes On The UI Sample

- Fragment actions return `X-Malda-Fragment: true` and `X-Malda-Fragment-Target: <target-id>` with fragment HTML
- The sample page includes a very small AJAX helper that submits forms with `FormData` and swaps only the target element when fragment headers are present
- LIVE updates are channel-scoped via query string, for example `?channel=tickets`
