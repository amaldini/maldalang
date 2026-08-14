# MALDA P0 maturity roadmap (3–6 months)

**Status:** P0 workstreams complete (2026-08-12); next work is post–Final 1.0 / deferred  
**Created:** 2026-08-12  
**Horizon:** ~3–6 months from creation  
**Audience:** maintainers prioritizing the OSS core (language, runtime, toolchain)

This is the forward plan after the purity / expressiveness phases in
[`docs/planning/malda-language-purity-roadmap.md`](planning/malda-language-purity-roadmap.md)
(historical; Phases 0–7 largely complete). All ranked P0 themes below are landed; prefer
[`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) post-Final gaps, the deferred list below, and
[`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) for *what next* on
the language surface.

**Not in scope here:** vertical domain packs, product apps outside OSS, drive-by stdlib growth
(stdlib remains soft-frozen — deepen `math` / `str` / `io` / web helpers; do not add flat aliases).

---

## Guiding principle

Ship **measurable trust** before new syntax. Malda already has match, sum types, async,
modules, schema, workflows, and server-driven UI. The competitive gap is correctness,
backend parity, platform ops, and toolchain maturity — not more operators.

---

## Themes and priority

| Rank | Theme | Why |
|------|--------|-----|
| 1 | Gradual types → trustworthy contract | Cuts silent failures; unblocks Spec Final |
| 2 | Workflow ops / observability / HA path | Differentiator still local-MVP vs Temporal-class |
| 3 | Interpreter ↔ C# ↔ JS product contracts | Shipability of `.exe` / browser without footguns |
| 4 | AI structured I/O + agent governance | Core “AI-first” story still has hard exclusivity rules |
| 5 | UI render / event / state loop | Fullstack usable without queue/state gotchas |
| 6 | Packages + selective module imports | Scale beyond local `include` / `import` |
| 7 | Debugger / benchmarks / test DX | Perception of a mature language |

Cross-cutting constraints (from `AGENTS.md`): filtered tests only; no hand-edit of generated
artifacts; Desktop IDE = reference, Web IDE = playground (honest parity); dual `MIT OR Apache-2.0`.

---

## Quarter plan

Rough sequencing. Items can overlap when owners differ; gates below are the acceptance bar.

### Month 1–2 — Types + spec trust

**Implementation plan:** [`docs/roadmap-p0-types-impl.md`](roadmap-p0-types-impl.md)

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **T1 Call-site / return inference** | Extend IDE/CLI checking beyond declared/`->` callees where cheap: common operators, selected Tier 1 builtins (`math` / `str` / `io` returns) | **Landed 2026-08-12:** operator + Tier-1 builtin return hints (`TypeCompatibilityDiagnostics`, `Tier1BuiltinReturnHints`); see [`roadmap-p0-types-impl.md`](roadmap-p0-types-impl.md) |
| **T2 Strict default in tooling** | LSP + Desktop: type mismatches as **Errors** by default (opt-out setting); CLI keeps `--strict-types` explicit unless documented otherwise | **Landed 2026-08-12:** `StrictTypesOptions.Default` / `TypeErrors=true`; LSP `malda.types.strict`; Desktop View → Type Errors as Errors |
| **T3 Nested schema hardening** | Nested schema fields, unknown names, cycles already erroring — close remaining gaps (arrays of nested schemas, import of schema names, diagnostics quality) | **Landed 2026-08-12:** IDE `malda-schema` field diagnostics; `SchemaNested*` / import+validate tests; RM schema resolve rules |
| **T4 Spec Final gate** | Promote Draft 1.0 → Final: Tier 0 conformance green; P0 type/schema notes in CHANGELOG closed or explicitly deferred with owner/version | **Landed 2026-08-12:** Spec **Final 1.0**; Tier 0 316 passed; gaps owned (`maintainers` / post-1.0) |

### Month 2–4 — Workflow ops + backend contracts

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **W1 Observability** | Instance timeline, step status, DLQ/requeue visibility beyond CLI one-liners (dashboard or structured `malda workflow …` report) | **Landed 2026-08-12:** `malda workflow report`; `Examples/Workflows/ops_report.malda`; RM §32.6 |
| **W2 HA / multi-worker path** | Documented model for more than one worker against durable store (even if v1 is “single writer + lease” or “read replica ops only”); identify SQLite limits and the migration story | Architecture note in `docs/` or workflows chapter: failure modes, lease/locking, what is *not* Temporal yet — **landed:** [`workflows-ha.md`](workflows-ha.md), WAL/`busy_timeout`, RM §32.10 |
| **W3 Determinism & replay** | Strengthen deny-list / replay detection beyond name list where feasible; document loop-in-step rules already in manual | Conformance or filtered workflow tests for new checks; gotcha for jobs vs workflows stays accurate — **landed:** `sleep` → WF1001; IDE static WF1001/WF1002; deny-list drift tests; RM + gotchas |
| **B1 Product capability matrix** | Extend [`backend-capability-matrix.md`](spec/backend-capability-matrix.md) with rows for schema/validate, typed prompts, HttpServer, UIHost, jobs — each marked yes/partial/no per backend | Matrix file + `BackendCapabilityMatrix` tests stay in sync — **landed:** split product rows + product-feature guard markers |
| **B2 Transpile smoke set** | Small curated `.malda` set (agents/web/workflow/schema) that must pass `malda compile --mode transpile` in CI smoke filter | Named filter or script; failures point at `build_errors.txt` paths as today — **landed:** `TranspileSmokeTests` (4 Examples) in CI Windows/Linux/macOS |

### Month 3–5 — AI, UI, packages

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **A1 Structured output + tools policy** | Document and sequence tools vs `response_format` exclusivity; keep validation/repair path for local models | **Landed 2026-08-12:** Modes A/B/C in gotchas/RM/matrix; `prompt_tools_mode.malda` + `prompt_tools_then_structured.malda` + existing structured example |
| **A2 Agent governance defaults** | Promote `@pure` / `@effects` / `validate()` in agent examples; optional CI example already exists — expand to one “golden” agent template | **Landed 2026-08-12:** `Examples/Agents/agent_governance_golden.malda` + README/metadata; RM `@pure`/`@effects` + validate callout |
| **U1 UI loop DX** | Reduce footguns: clearer APIs or diagnostics for “dispatch without pull/render”, document single model per surface (`@PAGE` vs `ui.*`) | **Landed 2026-08-12:** UI1001/UI1002 + `ui_event_loop.malda` + `docs/ui-framework.md` event-loop / one-model sections |
| **U2 State lifecycle** | Pin/TTL guidance enforced in docs + at least one diagnostic or runtime warning for poison defaults (`ui.state(id, k, null)`) | **Landed 2026-08-12:** UI1003 + `ui_state_lifecycle.malda` + ui-framework / RM / gotchas |
| **P1 Selective imports** | `import { a, b } from "…"` and/or `export type` (from Phase 3 deferred list) | **Landed 2026-08-12:** `import { … } from` (file + package); **post-Final:** `export type` / `export schema` + selective expand; design note; interpreter + transpile + `ModuleSymbolResolver` |
| **P2 Package story** | Local/workspace registry story without claiming a public npm-like hub yet: `malda` commands + docs for workspace `packages/` | **Landed 2026-08-12:** `packages/malda-demo-math`, `malda install <path>` / `list --workspace`, offline `list`/`init`/`uninstall`, [`docs/workspace-packages.md`](workspace-packages.md), CONTRIBUTING + start-here |

### Month 4–6 — Toolchain maturity

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **D1 Debug story** | Source-map or line mapping for transpile failures; document “how to debug” beyond GeneratedProgram spelunking | **Landed 2026-08-12:** [`docs/debugging-transpile.md`](debugging-transpile.md); CLI hint via `Compiler.TranspileFailureDebugHint`; `#line` already emitted |
| **D2 Benchmarks** | Publish a small bench suite (interpreter vs transpile) under `docs/benchmarks.md` / artifacts — numbers may be modest; absence is worse | **Landed 2026-08-12:** `scripts/run-micro-benchmarks.ps1` + [`docs/benchmarks-sample-results.json`](benchmarks-sample-results.json) sample table |
| **D3 LSP ≈ language intelligence** | Close highest-impact Desktop-only language features in LSP (strict defaults, module symbols, schema/type hover) — not UIHost parity | **Landed 2026-08-12:** schema/type hover + schema outline/workspace symbols; LSP README capability table; Web IDE stays playground |
| **D4 Cross-platform honesty** | Keep Desktop WPF Windows-only; ensure CLI/LSP/Web CI smoke stays green on Linux; document contributor path without Desktop | **Landed 2026-08-12:** CONTRIBUTING Linux/macOS build + “contribute without Desktop”; README CLI binary name note |

---

## Explicitly deferred (do not start as P0)

- New top-level builtins / flat global aliases
- Full static type system with runtime enforcement of all hints
- Distributed Temporal-equivalent cluster in-core
- Web IDE feature parity with Desktop (virtual sections, MCP UI, UIHost preview, local model browser)
- Public package registry hosted by the project
- Actor supervision trees at Elixir/Akka depth (unless pulled in by a concrete user need)

---

## Success metrics (end of horizon)

| Metric | Target |
|--------|--------|
| Spec | **Final 1.0 declared** (2026-08-12); post-Final gaps owned in CHANGELOG |
| Types | **T1–T3 done:** IDE/LSP default type mismatch = Error; call-site + Tier-1 builtin returns covered by tests |
| Workflows | **W1–W3 done:** ops report + HA note + determinism/replay checks |
| Backends | **B1+B2 done:** product capability matrix + transpile smoke set in CI filter |
| AI | **A1+A2 done:** structured + tools + sequence examples; governance golden with validate + `@pure`/`@effects` |
| UI | **U1+U2 done:** event-loop + UI1001/UI1002; state lifecycle + UI1003 poison defaults |
| Modules | **P1+P2 done:** selective imports + workspace `packages/` story; `export type` / `export schema` shipped post-Final |
| Perf story | **D2 done:** public sample micro-benchmark numbers in `docs/benchmarks.md` / `benchmarks-sample-results.json` |

---

## Working agreements

1. Verify against code, [`ReferenceManual/`](../ReferenceManual/), and [`docs/spec/`](spec/) — not against old `docs/planning/` status lines.
2. Prefer filtered `dotnet test MaldaLang.Tests --filter "…"` for the area touched.
3. When adding builtins (rare under soft freeze), complete the checklist in `AGENTS.md`.
4. Update [`docs/llm/`](llm/) gotchas/syntax when agent-facing behavior changes.
5. Keep Desktop vs Web IDE docs honest about parity.

---

## Related documents

| Doc | Role |
|-----|------|
| [`docs/architecture.md`](architecture.md) | Engine map; points here for P0 status + post-Final gaps |
| [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md) | Interpreter / C# / JS product surface |
| [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) | Spec semver + Final 1.0 + post-Final gaps |
| [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) | Post-Final language constructs (L1–L6) |
| [`docs/planning/malda-language-purity-roadmap.md`](planning/malda-language-purity-roadmap.md) | Completed purity phases (historical) |
| [`docs/planning/phase-3-modules-summary.md`](planning/phase-3-modules-summary.md) | Deferred module items |
| [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) | Silent failures to shrink |
| [`docs/ui-framework.md`](ui-framework.md) | Server-driven UI host |
| [`ReferenceManual/21-durable-workflows.html`](../ReferenceManual/21-durable-workflows.html) | Workflow user reference |
| [`docs/workflows-ha.md`](workflows-ha.md) | W2 single-writer + read-only ops HA model |
| [`docs/selective-imports.md`](selective-imports.md) | P1 `import { … } from` design + semantics |
| [`docs/workspace-packages.md`](workspace-packages.md) | P2 workspace `packages/` + offline CLI |
| [`docs/debugging-transpile.md`](debugging-transpile.md) | D1 transpile `#line` / build_errors debug guide |
| [`docs/roadmap-interpret-debug.md`](roadmap-interpret-debug.md) | Interpret-mode DAP / shared debug core (plan; not the D1 transpile story) |
| [`docs/benchmarks.md`](benchmarks.md) | D2 micro-benchmarks + sample results |
| [`docs/planning/phase-3-modules-summary.md`](planning/phase-3-modules-summary.md) | Historical Phase 3; selective import now shipped in P1 |

---

## Revision history

| Date | Change |
|------|--------|
| 2026-08-12 | Initial active P0 maturity roadmap (types, workflows, parity, AI, UI, packages, toolchain) |
| 2026-08-12 | W1 Observability landed: `malda workflow report`, `Examples/Workflows/ops_report.malda`, RM §32.6 |
| 2026-08-12 | W2 HA / multi-worker landed: `docs/workflows-ha.md`, SQLite WAL + `busy_timeout`, RM §32.10 |
| 2026-08-12 | W3 Determinism & replay landed: `sleep` WF1001, IDE `WorkflowDeterminismDiagnostics`, deny-list guard tests, RM + gotchas |
| 2026-08-12 | B1 Product capability matrix landed: schema/validate, HttpServer, UIHost, jobs rows + guard tests |
| 2026-08-12 | B2 Transpile smoke landed: `TranspileSmokeTests` for schema/agents/workflow/jobs Examples in CI |
| 2026-08-12 | T4 Spec Final landed: **Final 1.0**; Tier 0 green via `run-tier0-conformance.ps1` (316) |
| 2026-08-12 | T1–T3 landed (table sync): call-site/Tier-1 inference, strict type Errors default, nested schema IDE diagnostics — see [`roadmap-p0-types-impl.md`](roadmap-p0-types-impl.md) |
| 2026-08-12 | P0 maturity horizon closed: all ranked workstreams landed; next = post-Final gaps in [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) + explicitly deferred list |
| 2026-08-12 | A1 Structured output + tools policy landed: Modes A/B/C docs + `prompt_tools_mode.malda` / `prompt_tools_then_structured.malda` |
| 2026-08-12 | A2 Agent governance landed: `agent_governance_golden.malda`, README/metadata, RM `@pure`/`@effects`, gotcha for unvalidated tool JSON |
| 2026-08-12 | U1 UI loop DX landed: `UiLoopDiagnostics` UI1001/UI1002, `ui_event_loop.malda`, ui-framework event-loop + one-model cross-links |
| 2026-08-12 | U2 State lifecycle landed: UI1003 poison `ui.state` defaults, `ui_state_lifecycle.malda`, RM §19.2.9 + gotchas + ui-framework |
| 2026-08-12 | P1 Selective imports landed: `import { a, b } from`, `docs/selective-imports.md`, `Examples/Modules/selective_import.malda` |
| 2026-08-12 | Post-Final: `export type` / `export schema` + selective expand; hygiene for stale `typeOf` gap + async race gotcha/RM |
| 2026-08-12 | P2 Package story landed: `packages/malda-demo-math`, local install/`list --workspace`, offline PM, `docs/workspace-packages.md`, CONTRIBUTING/start-here/RM §2.3 |
| 2026-08-12 | D1 Debug story landed: `docs/debugging-transpile.md`, CLI `TranspileFailureDebugHint`, AGENTS/start-here links |
| 2026-08-12 | D2 Benchmarks landed: checked-in `docs/benchmarks-sample-results.json` + sample table in `docs/benchmarks.md` |
| 2026-08-12 | D3 LSP intelligence landed: schema/type hover (incl. imports), schema document/workspace symbols, LSP README capabilities |
| 2026-08-12 | D4 Cross-platform honesty landed: CONTRIBUTING Linux/macOS + no-Desktop path; README `malda` vs `malda.exe` note |
| 2026-08-14 | Pointer to post-Final language constructs plan (`docs/roadmap-language-constructs.md`) |
