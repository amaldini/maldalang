# MALDA P0 maturity roadmap (3–6 months)

**Status:** Active  
**Created:** 2026-08-12  
**Horizon:** ~3–6 months from creation  
**Audience:** maintainers prioritizing the OSS core (language, runtime, toolchain)

This is the forward plan after the purity / expressiveness phases in
[`docs/planning/malda-language-purity-roadmap.md`](planning/malda-language-purity-roadmap.md)
(historical; Phases 0–7 largely complete). Prefer this file for *what to strengthen next*.

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
| **T1 Call-site / return inference** | Extend IDE/CLI checking beyond declared/`->` callees where cheap: common operators, selected Tier 1 builtins (`math` / `str` / `io` returns) | Filtered tests cover new sites; `docs/llm/malda-gotchas.md` updated if behavior changes |
| **T2 Strict default in tooling** | LSP + Desktop: type mismatches as **Errors** by default (opt-out setting); CLI keeps `--strict-types` explicit unless documented otherwise | Setting documented in LSP/Desktop READMEs; guard or smoke test that default severity is Error |
| **T3 Nested schema hardening** | Nested schema fields, unknown names, cycles already erroring — close remaining gaps (arrays of nested schemas, import of schema names, diagnostics quality) | `SchemaNested*` / validate example tests green; Reference Manual schema chapter matches resolve rules |
| **T4 Spec Final gate** | Promote Draft 1.0 → Final: Tier 0 conformance green; P0 type/schema notes in CHANGELOG closed or explicitly deferred with owner/version | **Landed 2026-08-12:** Spec **Final 1.0**; Tier 0 316 passed; gaps owned (`maintainers` / post-1.0) |

### Month 2–4 — Workflow ops + backend contracts

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **W1 Observability** | Instance timeline, step status, DLQ/requeue visibility beyond CLI one-liners (dashboard or structured `malda workflow …` report) | At least one Examples/Workflows ops smoke + Reference Manual § ops updated |
| **W2 HA / multi-worker path** | Documented model for more than one worker against durable store (even if v1 is “single writer + lease” or “read replica ops only”); identify SQLite limits and the migration story | Architecture note in `docs/` or workflows chapter: failure modes, lease/locking, what is *not* Temporal yet — **landed:** [`workflows-ha.md`](workflows-ha.md), WAL/`busy_timeout`, RM §32.10 |
| **W3 Determinism & replay** | Strengthen deny-list / replay detection beyond name list where feasible; document loop-in-step rules already in manual | Conformance or filtered workflow tests for new checks; gotcha for jobs vs workflows stays accurate — **landed:** `sleep` → WF1001; IDE static WF1001/WF1002; deny-list drift tests; RM + gotchas |
| **B1 Product capability matrix** | Extend [`backend-capability-matrix.md`](spec/backend-capability-matrix.md) with rows for schema/validate, typed prompts, HttpServer, UIHost, jobs — each marked yes/partial/no per backend | Matrix file + `BackendCapabilityMatrix` tests stay in sync — **landed:** split product rows + product-feature guard markers |
| **B2 Transpile smoke set** | Small curated `.malda` set (agents/web/workflow/schema) that must pass `malda compile --mode transpile` in CI smoke filter | Named filter or script; failures point at `build_errors.txt` paths as today — **landed:** `TranspileSmokeTests` (4 Examples) in CI Windows/Linux/macOS |

### Month 3–5 — AI, UI, packages

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **A1 Structured output + tools policy** | Document and sequence tools vs `response_format` exclusivity; keep validation/repair path for local models | **Landed 2026-08-12:** Modes A/B/C in gotchas/RM/matrix; `prompt_tools_mode.malda` + `prompt_tools_then_structured.malda` + existing structured example |
| **A2 Agent governance defaults** | Promote `@pure` / `@effects` / `validate()` in agent examples; optional CI example already exists — expand to one “golden” agent template | Template or Examples/Agents golden path uses validate + pure helpers |
| **U1 UI loop DX** | Reduce footguns: clearer APIs or diagnostics for “dispatch without pull/render”, document single model per surface (`@PAGE` vs `ui.*`) | Diagnostics or gotcha + Example that demonstrates correct loop; `docs/ui-framework.md` cross-links |
| **U2 State lifecycle** | Pin/TTL guidance enforced in docs + at least one diagnostic or runtime warning for poison defaults (`ui.state(id, k, null)`) | Manual + filtered test or documented warning |
| **P1 Selective imports** | `import { a, b } from "…"` and/or `export type` (from Phase 3 deferred list) | Design note + interpreter + transpile + `ModuleSymbolResolver` + filtered tests |
| **P2 Package story** | Local/workspace registry story without claiming a public npm-like hub yet: `malda` commands + docs for workspace `packages/` | CONTRIBUTING or start-here documents the supported workflow |

### Month 4–6 — Toolchain maturity

| Workstream | Concrete work | Done when |
|------------|---------------|-----------|
| **D1 Debug story** | Source-map or line mapping for transpile failures; document “how to debug” beyond GeneratedProgram spelunking | Doc section + one improved CLI hint on transpile failure |
| **D2 Benchmarks** | Publish a small bench suite (interpreter vs transpile) under `docs/benchmarks.md` / artifacts — numbers may be modest; absence is worse | Script + checked-in result template or CI-optional job |
| **D3 LSP ≈ language intelligence** | Close highest-impact Desktop-only language features in LSP (strict defaults, module symbols, schema/type hover) — not UIHost parity | LSP README capability table updated; Web IDE remains playground |
| **D4 Cross-platform honesty** | Keep Desktop WPF Windows-only; ensure CLI/LSP/Web CI smoke stays green on Linux; document contributor path without Desktop | CI + CONTRIBUTING already true; fix any drift |

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
| Types | IDE/LSP default: type mismatch = Error; call-site + selected builtin returns covered by tests |
| Workflows | Ops path documented + one observability surface; HA/multi-worker story written (even if limited) |
| Backends | Product capability matrix complete; transpile smoke set in CI filter |
| AI | **A1 done:** structured + tools + sequence examples; gotchas match exclusivity + await validate |
| UI | Correct event/state loop example + documented single-surface rule |
| Modules | At least one Phase-3 deferred item shipped (`import {…}` or `export type`) |
| Perf story | Public micro-benchmark numbers exist (not “absent”) |

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
| [`docs/architecture.md`](architecture.md) | Engine map; points here for open P0 |
| [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md) | Interpreter / C# / JS product surface |
| [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) | Spec semver + Final 1.0 + post-Final gaps |
| [`docs/planning/malda-language-purity-roadmap.md`](planning/malda-language-purity-roadmap.md) | Completed purity phases (historical) |
| [`docs/planning/phase-3-modules-summary.md`](planning/phase-3-modules-summary.md) | Deferred module items |
| [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) | Silent failures to shrink |
| [`docs/ui-framework.md`](ui-framework.md) | Server-driven UI host |
| [`ReferenceManual/31-durable-workflows.html`](../ReferenceManual/31-durable-workflows.html) | Workflow user reference |
| [`docs/workflows-ha.md`](workflows-ha.md) | W2 single-writer + read-only ops HA model |

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
| 2026-08-12 | T4 Spec Final landed: **Final 1.0**; Tier 0 green via `run-tier0-conformance.ps1` (316); next open theme **A1** |
| 2026-08-12 | A1 Structured output + tools policy landed: Modes A/B/C docs + `prompt_tools_mode.malda` / `prompt_tools_then_structured.malda` |
