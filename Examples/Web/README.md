# Web examples

HttpServer / RestServer, server-driven UI, browser JS target, cookie and Bearer auth, and the lightweight job queue.

## Run

```bash
# From repo root
malda Examples/Web/rest_api_server.malda
malda Examples/Web/auth_cookie_login.malda   # http://localhost:8080/  admin / password
malda Examples/Web/form_validate_flash.malda # http://localhost:8082/  CSRF + validate + flash
malda Examples/Web/rest_bearer_jwt.malda     # port 8081; see printed curls
malda Examples/Web/job_queue_basic.malda     # exits after enqueue/claim/complete
```

JS / PWA samples under `js/` need `malda compile … --mode js` (or the Desktop/Web IDE browser path). See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md).

## Dependencies

| Example | Port / notes |
|---------|----------------|
| Most `HttpServer` / `@PAGE` demos | Local HTTP (often 8080) |
| `auth_cookie_login.malda` | 8080; no external API key |
| `form_validate_flash.malda` | 8082; CSRF + bindForm + schema validate |
| `rest_bearer_jwt.malda` | 8081 |
| `job_queue_basic.malda` | Writes `./.malda/jobs.db`; offline |
| `ai_generated_*` / `@AIPAGE` | Needs LLM provider (`api-key`) |
| `crm_modern_sqlite.malda` | SQLite file (`db`) |

Fullstack scaffold with mount + sessions: `malda new fullstack my-app`.  
Walkthrough: [`docs/tutorials/fullstack-sessions-auth.md`](../../docs/tutorials/fullstack-sessions-auth.md).

Catalog fields: [`metadata.json`](metadata.json).
