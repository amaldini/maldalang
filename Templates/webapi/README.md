# __PROJECT_NAME__ (MALDA Web API Template)

## Run This First

```bash
malda test
malda app.malda
```

To scaffold the same starter with SQLite/local-first groundwork:

```bash
malda new webapi my-api --local-first
```

If you want machine-readable output for CI or tooling:

```bash
malda test --format ci
```

## What Matters First

- `app.malda`: main MALDA entry point with the generated API baseline
- `tests/*.test.malda`: starter tests that work with `malda test`
- `tests/security_helpers.malda`: reusable helpers for security-focused tests
- `config/security.example.json`: JWT/rate-limit baseline to adapt before real use
- `config/data.example.json`: local-first/SQLite contract placeholder for teams that want a repeatable data path
- `config/deploy.example.json`: deploy contract placeholder used by `malda deploy`
- `config/observability.example.json`: logs/metrics/health contract placeholder

## What This Starter Includes Today

- A runnable Web API baseline
- Test defaults for `malda test`
- Security, deploy, and observability example config files
- Optional `--local-first` scaffold mode that adds SQLite bootstrap and an in-app migration registry seam

## Web Runtime Surface

- Global middleware is registered with `server.use(fn)` and follows the `function middleware(req, res, next)` shape
- Middleware can short-circuit with `res.status(...).json(...)` or continue with `next()`
- Route handlers remain backward-compatible with named path/query/body parameters
- Handlers and middleware can also take first-class `req` and `res` objects

`req` currently exposes:

- Properties: `method`, `path`, `url`, `queryString`, `query`, `params`, `headers`, `cookies`, `body`, `correlationId`, `ip`, `host`, `contentType`, `auth`
- Helpers: `req.header(name, default?)`, `req.queryParam(name, default?)`, `req.param(name, default?)`, `req.cookie(name, default?)`
- Middleware can attach request-scoped values such as `req.user` or `req.tenant` for later handlers

`res` currently exposes:

- `status(code)`, `json(value)`, `text(value)`, `html(value)`, `send(value)`
- `header(name, value)`, `cookie(name, value, options?)`, `clearCookie(name, options?)`, `redirect(location, status?)`

{{#LOCAL_FIRST}}
## Local-First Mode

This project was scaffolded with `--local-first`.

- `data/local_first.malda` bootstraps SQLite, enables WAL/foreign keys, and records applied migrations
- `data/local_first.malda` now includes small idempotent helper seams such as `tableExists()`, `columnExists()`, `ensureColumn()`, `ensureIndex()`, and `ensureLifecycleColumns()`
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

This scaffold is a practical starting point, not a finished production service.

Honest scope of the language underneath: type annotations are IDE/LSP hints (runtime stays dynamic unless you `malda compile --mode transpile`, which refuses hint Errors; `--lenient-types` escapes). Durable workflows are single-writer SQLite, not a cluster. The JavaScript backend is a browser subset (no agents or HTTP servers).

Baseline today:

- The starter gives you a MALDA API entry point plus testing and config structure
- The config files show where security, deploy, and observability settings belong

Still up to you:

- Replace placeholder secrets and environment values
- Add your domain routes, data access, and authorization rules
- Fill in deploy and observability contracts for real environments
- If you use `--local-first`, evolve the generated migration registry rather than editing schema ad hoc
