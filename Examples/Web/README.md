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

### UI event loop (offline)

Correct `ui.*` loop: `mount` → `dispatchEvent` → `pullEvent` → state → rebuild → `render`.

```bash
malda Examples/Web/ui_event_loop.malda
```

### UI state lifecycle (offline)

Peek vs get-or-create, `ui.pinState` for process-lifetime data, safe defaults (IDE **UI1003** on `ui.state(..., null|{})`).

```bash
malda Examples/Web/ui_state_lifecycle.malda
```

Longer showcase: `ui_counter_dashboard.malda`. Engine notes: [`docs/ui-framework.md`](../../docs/ui-framework.md).

JS / PWA samples under `js/` need `malda compile … --mode js` (or the Desktop/Web IDE browser path). See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md).

```bash
malda compile Examples/Web/js/three_shader_path_tunnel.malda --mode js -o Examples/Web/js/three_shader_path_tunnel.js
malda compile Examples/Web/js/maldadash.malda --mode js -o Examples/Web/js/maldadash.js
```

`maldadash.malda` is a Boulder Dash-style cave (dirt, gravity rocks, diamonds, fireflies) on `game.*`. Open it with `Examples/Web/js/maldadash_runtime_smoke_test.html` after compiling.

That tunnel demo is a CC-BY-NC-SA-4.0 conversion of [Frostbyte’s path march](https://fragcoord.xyz/s/tbe1g319); it is not under the repository MIT OR Apache-2.0 dual licence.

## Dependencies

| Example | Port / notes |
|---------|----------------|
| Most `HttpServer` / `@PAGE` demos | Local HTTP (often 8080) |
| `auth_cookie_login.malda` | 8080; no external API key |
| `form_validate_flash.malda` | 8082; CSRF + bindForm + schema validate |
| `rest_bearer_jwt.malda` | 8081 |
| `job_queue_basic.malda` | Writes `./.malda/jobs.db`; offline |
| `ai_generated_*` / `@AIPAGE` | Needs LLM provider (`api-key`) |

Fullstack scaffold with mount + sessions: `malda new fullstack my-app`.  
Walkthrough: [`docs/tutorials/fullstack-sessions-auth.md`](../../docs/tutorials/fullstack-sessions-auth.md).

Catalog fields: [`metadata.json`](metadata.json).
