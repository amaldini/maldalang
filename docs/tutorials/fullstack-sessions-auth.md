# Tutorial: fullstack sessions, auth, CSRF, and jobs

End-to-end path from scaffold to cookie login, CSRF-aware forms, and the lightweight job queue.
Requires .NET 8 (or a release zip with `malda` / `malda.bat` on your `PATH`).

## 1. Scaffold the fullstack starter

```bash
malda new fullstack my-app
cd my-app
malda test
malda backend/app.malda
```

Open (single port via `HttpServer.mount`):

- `http://localhost:8080/api/health`
- `http://localhost:8080/api/me` (Bearer JWT sample)
- `http://localhost:8080/components/TicketBoard`

Read the generated [`Templates/fullstack/README.md`](../../Templates/fullstack/README.md) (copied into the project as `README.md`) for sessions, CSRF, form helpers, and config placeholders.

Stop the server with Ctrl+C when finished.

## 2. Cookie login + session flash

From the repo root (or a distribution that includes `Examples/`):

```bash
malda Examples/Web/auth_cookie_login.malda
```

Open `http://localhost:8080/`. Demo credentials: **admin** / **password**.

What to notice:

- Password hashing + JWT cookie auth on `HttpServer`
- `enableSession` and flash messages for login errors (not query-string errors)
- CSRF-aware form posting

Related API-only path (Bearer JWT on `RestServer`):

```bash
malda Examples/Web/rest_bearer_jwt.malda
```

Follow the printed curl examples (default port **8081**).

## 3. Form → bindForm → validate → flash

```bash
malda Examples/Web/form_validate_flash.malda
```

Open `http://localhost:8082/`.

What to notice:

- CSRF cookie and form `_csrf` set to the **same** token (do not call `csrfField(secret)` alone)
- `bindForm` for field checks, then `validate("Contact", …)` for schema shape
- Failures use `req.session.flash` + redirect; success shows a thank-you page

## 4. Job queue (not durable workflows)

```bash
malda Examples/Web/job_queue_basic.malda
```

This uses `enqueueJob` / `claimJob` / `completeJob` / `listJobs` against `./.malda/jobs.db`.
It is a fire-and-forget worker queue. For step/retry/compensate persistence, use
`workflow { }` — see `Examples/Workflows/` and Reference Manual chapter 31.

## 5. Suggested learning order

1. `malda new fullstack …` — mount, sessions, form helpers in one app
2. `Examples/Web/auth_cookie_login.malda` — cookie + session flash
3. `Examples/Web/form_validate_flash.malda` — CSRF + bindForm + schema validate
4. `Examples/Web/rest_bearer_jwt.malda` — API Bearer JWT
5. `Examples/Web/job_queue_basic.malda` — background jobs

More on `schema` / `validate` and tagged errors: [`errors-and-validation.md`](errors-and-validation.md).

Catalog of all sample folders: [`Examples/README.md`](../../Examples/README.md).
Start-here routes: [`docs/start-here.md`](../start-here.md).
