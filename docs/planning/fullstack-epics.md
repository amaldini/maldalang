# MALDA Full-Stack Epics and Issue Backlog

This file provides a GitHub-style planning backlog for turning MALDA into a production-ready full-stack platform.

## Labels

Suggested labels:
- `type:epic`
- `type:story`
- `area:web`
- `area:security`
- `area:data`
- `area:cli`
- `area:testing`
- `area:docs`
- `priority:P0`
- `priority:P1`
- `priority:P2`

## Milestones

- `M1 Web Foundation`
- `M2 Security + Validation`
- `M3 Data + Testing`
- `M4 Scaffolding + Deploy`

---

## Epic FS-001: Web Runtime Foundation

- **Type:** `type:epic`
- **Priority:** `priority:P0`
- **Area:** `area:web`
- **Milestone:** `M1 Web Foundation`
- **Depends on:** none
- **Primary files:** `MaldaLang/BuiltIns/RestServer.cs`, `MaldaLang/BuiltIns/HttpServer.cs`, `MaldaLang/Interpreter/RouteRegistry.cs`

### Goal
Add composable middleware and first-class request/response handling while keeping existing decorators backward compatible.

### Acceptance
- [ ] Middleware chain supports `next` and short-circuit behavior
- [ ] Unified request context is available in REST/HTTP handlers
- [ ] Response helpers exist (`status`, `json`, `text`, `html`, `redirect`)
- [ ] Existing route decorators continue to work unchanged

### Stories
- [ ] **FS-002** Add web middleware chain and execution model
- [ ] **FS-003** Implement request context object (path/query/headers/body/cookies)
- [ ] **FS-004** Implement response helper API
- [ ] **FS-005** Add global error handling + correlation ID
- [ ] **FS-006** Extend `RouteRegistry` metadata for middleware/binding
- [ ] **FS-007** Expand tests for middleware/request/response behavior

---

## Epic FS-010: Validation and API Contracts

- **Type:** `type:epic`
- **Priority:** `priority:P0`
- **Area:** `area:web`
- **Milestone:** `M2 Security + Validation`
- **Depends on:** `FS-001`
- **Primary files:** `MaldaLang/BuiltIns/RestServer.cs`, `MaldaLang/Interpreter/RouteRegistry.cs`

### Goal
Introduce schema-style request validation and consistent API error payloads.

### Acceptance
- [ ] Path/query/body validation runs before handler execution
- [ ] Invalid requests return standardized 400 responses
- [ ] Swagger output includes validation constraints where possible

### Stories
- [ ] **FS-011** Add validation schema DSL/object format
- [ ] **FS-012** Wire validation into route invocation path
- [ ] **FS-013** Standardize error response model
- [ ] **FS-014** Add validation test suite (`RestValidationTests`)
- [ ] **FS-015** Update REST manual with validation examples

---

## Epic FS-020: Security and Auth Baseline

- **Type:** `type:epic`
- **Priority:** `priority:P0`
- **Area:** `area:security`
- **Milestone:** `M2 Security + Validation`
- **Depends on:** `FS-001`, `FS-010`
- **Primary files:** `MaldaLang/BuiltIns/BuiltInFunctions.cs`, `MaldaLang/BuiltIns/RestServer.cs`, `MaldaLang/BuiltIns/HttpServer.cs`

### Goal
Provide production-usable auth and security primitives as first-class MALDA capabilities.

### Acceptance
- [ ] Password hashing and verification built-ins are available
- [ ] JWT create/verify built-ins are available
- [ ] Auth middleware can protect routes
- [ ] CSRF and cookie security helpers are available
- [ ] Rate limiting is available for API endpoints

### Stories
- [ ] **FS-021** Add password hash/verify built-ins
- [ ] **FS-022** Add JWT create/verify built-ins
- [ ] **FS-023** Add auth middleware integration in `RestServer`
- [ ] **FS-024** Add CSRF + secure cookie helpers
- [ ] **FS-025** Integrate/reuse rate limiter for web endpoints
- [ ] **FS-026** Add tests: `AuthBuiltInsTests`, `SecurityMiddlewareTests`
- [ ] **FS-027** Add security section to REST and built-ins docs

---

## Epic FS-030: Data Platform (Migrations + SQLite)

- **Type:** `type:epic`
- **Priority:** `priority:P1`
- **Area:** `area:data`
- **Milestone:** `M3 Data + Testing`
- **Depends on:** `FS-001`
- **Primary files:** `MaldaLang/Program.cs`, `MaldaLang/BuiltIns/SqlServerClient.cs`, `MaldaLang/BuiltIns/PostgresClient.cs`

### Goal
Enable reliable schema lifecycle and local-first development experience.

### Acceptance
- [ ] `malda db migrate|rollback|seed` commands exist
- [ ] SQLite client support exists
- [ ] Migration history is tracked
- [ ] Migration and SQLite tests are passing

### Stories
- [ ] **FS-031** Add CLI command group: `malda db ...`
- [ ] **FS-032** Implement migration runner + metadata store
- [ ] **FS-033** Implement migration scaffolding
- [ ] **FS-034** Add `SqliteClient` built-in
- [ ] **FS-035** Add tests: `MigrationsTests`, `SqliteClientTests`
- [ ] **FS-036** Update database docs and examples

---

## Epic FS-040: Test Platform (`malda test`)

- **Type:** `type:epic`
- **Priority:** `priority:P1`
- **Area:** `area:testing`
- **Milestone:** `M3 Data + Testing`
- **Depends on:** `FS-001`
- **Primary files:** `MaldaLang/Program.cs`, `MaldaLang.Tests/TestBase.cs`, `MaldaLang.Tests/TranspiledTestRunner.cs`

### Goal
Ship a first-class test command with test discovery and clear output.

### Acceptance
- [ ] `malda test` command exists with filters
- [ ] Test discovery is deterministic
- [ ] Output format is CI-friendly and readable

### Stories
- [ ] **FS-041** Add `malda test` CLI command
- [ ] **FS-042** Implement test discovery rules
- [ ] **FS-043** Add report formatter (console + CI)
- [ ] **FS-044** Add tests: `TestCommandTests`, `TestDiscoveryTests`
- [ ] **FS-045** Add docs section for testing workflow

---

## Epic FS-050: Frontend DX and UI Components

- **Type:** `type:epic`
- **Priority:** `priority:P1`
- **Area:** `area:web`
- **Milestone:** `M4 Scaffolding + Deploy`
- **Depends on:** `FS-001`
- **Primary files:** `MaldaLang/BuiltIns/HttpServer.cs`, `MaldaLang/BuiltIns/BuiltInFunctions.cs`

### Goal
Improve maintainability and speed for MALDA web UI development.

### Acceptance
- [ ] Reusable component/template conventions exist
- [ ] Form binding and validation helpers are available
- [ ] Hot-reload/dev iteration improvements are available

### Stories
- [ ] **FS-051** Add component/template conventions for server pages
- [ ] **FS-052** Add form binding + validation helpers
- [ ] **FS-053** Improve hot reload/cache invalidation behavior
- [ ] **FS-054** Add tests: `TemplateComponentsTests`, `UIGenerationTests` updates
- [ ] **FS-055** Update Web UI manual chapter

### Phase A implementation snapshot
- ✅ Decorator/syntax surface added for server components:
  - `component Name(...) { ... }` syntax sugar (desugars to `@COMPONENT`)
  - `@ACTION(path)` for fragment/form postbacks
  - `@LIVE(path)` for live stream endpoints
- ✅ Runtime helper built-ins added:
  - `renderTemplate(...)`
  - `componentFragment(...)`
  - `componentLiveEmit(...)`
  - `componentStateGet/Set/Object/Clear`
- ✅ HTTP runtime wired to register `COMPONENT`, `ACTION`, and `LIVE` routes.
- ✅ Fullstack template and Web examples updated with a canonical ticketing flow.
- ✅ Manual updated with Phase A guidance and migration path from `@PAGE`.

---

## Epic FS-060: Scaffolding and Project Templates

- **Type:** `type:epic`
- **Priority:** `priority:P1`
- **Area:** `area:cli`
- **Milestone:** `M4 Scaffolding + Deploy`
- **Depends on:** `FS-001`, `FS-010`, `FS-020`, `FS-030`, `FS-040`
- **Primary files:** `MaldaLang/Program.cs`, `MaldaLang/PackageManager/PackageManager.cs`

### Goal
Provide one-command project bootstrap with production-oriented defaults.

### Acceptance
- [ ] `malda new webapi` exists
- [ ] `malda new fullstack` exists
- [ ] Generated project includes test and config baseline

### Stories
- [ ] **FS-061** Add template engine/scaffolder support
- [ ] **FS-062** Add `Templates/webapi` starter
- [ ] **FS-063** Add `Templates/fullstack` starter
- [ ] **FS-064** Add tests: `ScaffoldingTests`
- [ ] **FS-065** Update README quick-start for scaffolding

---

## Epic FS-070: Deployment and Observability

- **Type:** `type:epic`
- **Priority:** `priority:P2`
- **Area:** `area:cli`
- **Milestone:** `M4 Scaffolding + Deploy`
- **Depends on:** `FS-001`, `FS-060`
- **Primary files:** `MaldaLang/Program.cs`, `MaldaLang/Runtime/Tracing/Tracing.cs`, `MaldaLang/BuiltIns/RestServer.cs`, `MaldaLang/BuiltIns/HttpServer.cs`

### Goal
Provide operational readiness by default (health, logs, metrics, trace correlation).

### Acceptance
- [ ] `malda deploy` command exists with at least one target preset
- [ ] Health/readiness endpoints are available
- [ ] Structured logging is available
- [ ] Basic metrics endpoint is available

### Stories
- [ ] **FS-071** Add `malda deploy` command skeleton
- [ ] **FS-072** Add health/readiness endpoints
- [ ] **FS-073** Add structured logging helpers
- [ ] **FS-074** Add basic metrics endpoint
- [ ] **FS-075** Add tests: `DeployCommandTests`, `ObservabilityTests`
- [ ] **FS-076** Add ops/deploy docs section

---

## Suggested Sprint-to-Issue Mapping

### Sprint 1
- `FS-002`, `FS-003`, `FS-004`, `FS-007`

### Sprint 2
- `FS-005`, `FS-006`, `FS-011`, `FS-012`, `FS-014`

### Sprint 3
- `FS-013`, `FS-021`, `FS-022`, `FS-023`, `FS-026`

### Sprint 4
- `FS-024`, `FS-025`, `FS-031`, `FS-032`, `FS-035`

### Sprint 5
- `FS-033`, `FS-034`, `FS-041`, `FS-042`, `FS-044`

### Sprint 6
- `FS-043`, `FS-051`, `FS-052`, `FS-053`, `FS-054`

### Sprint 7
- `FS-061`, `FS-062`, `FS-063`, `FS-064`, `FS-065`

### Sprint 8
- `FS-071`, `FS-072`, `FS-073`, `FS-074`, `FS-075`, `FS-076`

---

## Notes for Implementation Hygiene

- Keep interpreter and transpiler behavior aligned for all new built-ins.
- Add docs updates as part of each epic, not as a final phase.
- Avoid breaking existing decorator-based routes and examples.
- Prefer incremental feature flags if rollout risk increases.
