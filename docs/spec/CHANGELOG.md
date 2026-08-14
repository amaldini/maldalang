# Malda language specification — CHANGELOG and versioning policy

**Document:** `docs/spec/CHANGELOG.md`  
**Applies to:** [malda-language-1.0.md](malda-language-1.0.md) and successor spec files under `docs/spec/`  
**Toolchain:** `malda` CLI, interpreter, and transpiler follow the active spec; their **package version** (e.g. assembly `1.x`) is independent but should cite the spec version in release notes.

---

## Versioning model

Malda uses **semantic versioning** for the **language specification** (`MAJOR.MINOR.PATCH`):

| Bump | When | Examples |
|------|------|----------|
| **MAJOR** | Breaking change to Tier 0 semantics, syntax removal, or incompatible builtin contract | Remove `fn` alias; change `dict` missing-key from `null` to error; drop flat `abs()` global |
| **MINOR** | Additive, backward-compatible contract | New keyword/syntax; new Tier 1 builtin; new `typeOf` tag while old tag still accepted during deprecation |
| **PATCH** | Clarification only; no observable behavior change in conformance tests | Spec prose fix; document de-facto parser rule; typo in grammar chapter |

**Spec status labels**

| Label | Meaning |
|-------|---------|
| **Draft X.Y** | Normative intent documented; Tier 0 conformance may still be incomplete |
| **Final X.Y** | Tier 0 conformance suite is the acceptance gate for that spec line |

Current line: **Final 1.0** ([malda-language-1.0.md](malda-language-1.0.md), declared 2026-08-12; Draft since 2026-06-04).

---

## What counts as breaking vs additive

### Tier 0 (language kernel)

| Change type | Tier |
|-------------|------|
| New keyword or statement form that existing programs can ignore | **MINOR** (if purely additive) |
| Stricter parse (previously accepted program becomes syntax error) | **MAJOR** |
| Stricter runtime (previously defined program changes result or errors) | **MAJOR** |
| `match` / `async` / actor semantics change | **MAJOR** |
| `null`, truthiness, or dictionary `d["missing"]` behavior change | **MAJOR** |

### Tier 1 (stdlib shipped with core distribution)

| Change type | Tier |
|-------------|------|
| New namespaced builtin (`math.foo`) | **MINOR** |
| New flat global mirroring namespaced API | **MINOR** during migration; removing flat global later is **MAJOR** after deprecation window |
| Moving builtin from core registry to optional pack | **MAJOR** for scripts that relied on zero-config global (pack migration uses deprecation policy below) |

### Tier 2 (optional packs)

Optional packs and platform hosts are versioned **separately** from Tier 0. Pack API breaks do not bump the Tier 0 spec MAJOR unless the core `loadNativeModule` contract changes.

### Documentation-only

| Change type | Tier |
|-------------|------|
| [34-grammar.html](../../ReferenceManual/34-grammar.html) aligned with parser | **PATCH** (spec 1.0 unchanged) |
| Reference Manual narrative | Not spec versioned; track in manual changelog if needed |

---

## One-release deprecation policy

**Default rule** for language surface, flat stdlib globals, and `typeOf` tag renames:

1. **Release N (deprecation release)**  
   - Old and new forms both work where possible.  
   - **IDE** emits `malda-style` or dedicated diagnostic (once per site).  
   - **Runtime** may log a single warning per process for hot paths.  
   - Spec and Reference Manual state the replacement and target removal version.

2. **Release N+1 (removal release)**  
   - Old form removed or hard-errors.  
   - Conformance tests updated; CHANGELOG records **MAJOR** if observable behavior removed.

**Exceptions** (require explicit CHANGELOG entry and roadmap approval):

- Security or correctness bugs (fix in **PATCH** or **MINOR**; if fix breaks programs, treat as **MAJOR**).  
- Optional-pack-only symbols (no core deprecation; use SDK `include` instead).

**Current deprecations (N = 2026-06 core distribution)**

| Surface | Replacement | Removal target |
|---------|-------------|----------------|
| Flat math builtins (`abs`, `sqrt`, …) | `math.*` | Next **MAJOR** core spec after flat-alias period |
| `Math.*` module alias | `math.*` | Same as flat math |
| Flat string builtins (`split`, `join`, …) | `str.*` | Same |
| Flat I/O builtins (`readFile`, `print`, …) | `io.*` | Same |
| `fn` / `def` function keywords | `function` | Not scheduled (aliases remain; IDE warns only) |

---

## How to propose a spec change

1. Update [malda-language-1.0.md](malda-language-1.0.md) (or fork `malda-language-1.1.md` for large drafts).  
2. Add a **Conformance** row and test in `MaldaLang.Tests/Conformance/Tier0/` when behavior is normative.  
3. Add an entry under `[Unreleased]` below with **MAJOR** / **MINOR** / **PATCH** label.  
4. If syntax changes: update [34-grammar.html](../../ReferenceManual/34-grammar.html) and `ReferenceManualGrammarCoverageTests`.  
5. Phase 2.4: `scripts/verify-spec-parser-drift.ps1` and `bitbucket-pipelines.yml` fail PRs that touch `Parser.cs` or `Lexer.cs` without spec/grammar/CHANGELOG update.

**Implementation precedence for Final 1.0:** interpreter + Tier 0 tests → spec prose → Reference Manual.

---

## Release history (spec line)

### [Unreleased]

#### Added (MINOR — primary constructors)

- **`class Name(params)`:** parameter list after the class name desugars to public fields plus a synthesized constructor. Body optional (`class Point(x, y);` or `{ methods }`). Cannot combine with `extends` or an explicit `function Name(...)`. Grammar: [`34-grammar.html`](../../ReferenceManual/34-grammar.html); narrative: [`10-classes-objects.html`](../../ReferenceManual/10-classes-objects.html) §10.11.

#### Added (MINOR — additive module syntax)

- **Selective imports:** `import { a, b } from "path.malda"` / `from package` — merge only named export-surface bindings; missing names error. Design: [`docs/selective-imports.md`](../selective-imports.md); example `Examples/Modules/selective_import.malda`.
- **`export type` / `export schema`:** same export surface as values; `export type T` includes constructors; selective import expands type↔ctors; IDE/transpile gate types/schemas when the module uses any `export`. Example: `Examples/Modules/export_type_schema.malda`.

#### Added (MINOR — schema / sum-type validate)

- **L1a:** `validate("Intent", value)` resolves sum-type names against the existing tagged `oneOf` schema. Schema fields may name a sum type (`intent: Intent` / `Intent[]`). Success still returns the original dict (no variant coercion). Exclusive names unchanged. Example: `Examples/Basics/schema_sumtype_validate.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Clarified (PATCH — docs / tracking only)

- **`typeOf(variant)` / `typeOf(task)`:** already return `"variant"` / `"task"` (Tier 0 T0-096/T0-097); removed stale post-Final gap bullet. Concurrent `async` + `sleep` between `var` bindings remains doc-only (gotchas + RM §6.14).
- **Post-Final language constructs plan:** ranked workstreams L1–L6 (schema/sum-type unification, gather-then-extract prompts, `@budget`, workflow call-graph determinism, grounded values, deferred capabilities). Tracking only — no Tier 0 semantic change. See [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Clarified (PATCH — product / Tier-2 docs only; no Tier 0 semantic change)

- **A1 tools vs `response_format`:** exclusivity = no OpenAI `response_format` and no `MALDA_OUTPUT_SCHEMA` appendix when the prompt lists tools; `await` + `-> Type` still validates/repairs. Supported modes A/B/C documented in [`docs/llm/malda-gotchas.md`](../llm/malda-gotchas.md), [`ReferenceManual/09-functions.html`](../../ReferenceManual/09-functions.html) §8.8.3.1, and `Examples/Prompts/prompt_tools_*.malda`.

### [1.0.0] — 2026-08-12 (Final)

**Status:** Final 1.0 declared 2026-08-12. Tier 0 conformance green on interpreter + C# (`scripts/run-tier0-conformance.ps1`: 316 passed, 0 failed). JavaScript Tier 0 remains a separate matrix subset and is **not** Final-gated.

#### Added (shipped under Draft; absorbed into Final without Tier 0 semantic change)

- **CI:** `scripts/verify-spec-parser-drift.ps1`, `scripts/verify-spec-guards.ps1`, `SpecParserDriftGuardTests`, `bitbucket-pipelines.yml` (Phase 2.4).
- **Phase 4.2:** Canonical `typeOf` tags (`int`, `bool`, `dict`, `variant`, `task`, …); `isTag()` with legacy alias matching; `Tier0TypeTags` — see [phase-4.2-type-tags.md](../planning/phase-4.2-type-tags.md).
- **Phase 4.3:** `malda run` / script execution flag `--strict-types`; unknown type-hint errors; non-exhaustive sum-type `match` errors (`malda-match`) — see [phase-4.3-strict-types.md](../planning/phase-4.3-strict-types.md).
- **Phase 4.4:** `result.*` and `option.*` stdlib (`map`, `unwrapOr`, tag tests); null-conditional `?.` / `?[]` — see [phase-4.4-result-option.md](../planning/phase-4.4-result-option.md).
- **Phase 4.5:** Tagged catch `catch (e if condition)` with ordered clause matching — see [phase-4.5-tagged-catch.md](../planning/phase-4.5-tagged-catch.md).

#### Deprecated (Release N = 2026-06 core distribution)

| Surface | Replacement | Removal target |
|---------|-------------|----------------|
| `typeOf` comparison to `"integer"` / `"boolean"` | `"int"` / `"bool"` or `isTag(x, "integer")` during window | Next **MAJOR** after deprecation release |
| Expecting `typeOf(dict)` → `"object"` | `"dict"` or `isTag(x, "dict")` | Same |

#### Final checklist (completed 2026-08-12) — Draft 1.0 → Final

- [x] Tier 0 conformance green on interpreter + C# (`scripts/run-tier0-conformance.ps1` / matrix thresholds) — verified 2026-08-12 (316 passed)
- [x] T1 operator + selected Tier-1 builtin return inference shipped (IDE analysis)
- [x] T2 IDE/LSP type Errors by default + opt-out (`malda.types.strict` / Desktop menu)
- [x] T3 nested schema resolve + IDE field diagnostics (`malda-schema`)
- [x] Remaining draft gaps below marked defer-post-Final with owner/version

Implementation plan: [`docs/roadmap-p0-types-impl.md`](../roadmap-p0-types-impl.md).

#### Known gaps (defer post-Final — do not block Final 1.0)

- Concurrent `async` user calls with `sleep` between consecutive `var` bindings (documented limitation) — **defer post-Final**; owner **maintainers**; target **post-1.0** (doc-only until a design exists). See gotchas + RM §6.14.
- Multi-backend product parity (agents/HTTP/workflows on JS) — **not Final-gated**; owner **maintainers**; Tier 0 JS tracked separately via the backend matrix.

#### Closed post-Final (already shipped at Final)

- `typeOf(variant)` / `typeOf(task)` return canonical kind tags `"variant"` / `"task"` (not `"unknown"`); Tier 0 T0-096 / T0-097. Constructor tags stay in `match`, not `typeOf`.

### [1.0.0-draft] — 2026-06-04 (Phase 3 modules)

#### Added (MINOR — additive)

- Keywords `import` and `export`; file and package import with isolated module environments.  
- Spec §14 and [phase-3-modules-design.md](../planning/phase-3-modules-design.md).  
- Grammar: `ImportStmt`, `ExportableDecl` in [34-grammar.html](../../ReferenceManual/34-grammar.html).

#### Implementation (Phase 3.2)

- `ModuleLoader.LoadFileModuleAsync`, `ModuleExports` filtering, `ImportStatement` in parser/interpreter.

### [1.0.0-draft] — 2026-06-04

**Status:** Historical Draft entry. Superseded by **[1.0.0] Final** (2026-08-12).

#### Added (normative documentation)

- Initial [malda-language-1.0.md](malda-language-1.0.md): value model, null, truthiness, `match`, sum types, `async`/`await`/`all`, actors, `typeOf`/`isNumber`, dictionary missing-key → `null`.  
- Expanded [34-grammar.html](../../ReferenceManual/34-grammar.html) (Phase 2.2).  
- This CHANGELOG and semver policy (Phase 2.3).

#### Implementation alignment (already shipped in toolchain)

- **Pack isolation (2026-06-03):** optional vertical-pack symbols removed from `BuiltInRegistry`. Spec: out of Tier 0.  
- **Phase 1.1:** optional-pack bootstrap auto-globals removed from core.  
- **Phase 1.2:** `math`, `str`, `io` namespaces; flat globals deprecated (one-release policy).  
- **Interpreter:** `WrapCallAsTask` for `async userFn()` environment binding (2026-06-04).

#### P0 readiness notes (2026-08-12) — types / schema (landed before Final)

- **Call-site checking:** IDE/LSP default elevates type mismatches to **Error** (`StrictTypesOptions.Default` / `malda.types.strict`); covers literals, hinted ids, `new`, call `-> T` (same unit + imports), operators (when both sides inferable), and selected Tier-1 builtin returns (`math` / `str` / `io`). CLI `--strict-types` remains explicit and also enables match/`@pure`/bounds/const.  
- **Nested schemas:** field types may name other schemas (`Other` / `Other[]`); unknown field types and cycles error on resolve; IDE `malda-schema` diagnostics on unknown field types; import + `validate` covered by tests.  
- **Workflow (minimal):** WF1001 denies `now` / `random*` / `randn` / `sleep`; WF1002 denies filesystem/process/HTTP built-ins outside `step`; IDE/LSP static WF1001/WF1002 on direct calls; durability remains single-box SQLite + fixed deny-list (not Temporal replay detection).

### Pre-spec baseline (reference only)

| Date | Event | Spec impact |
|------|--------|-------------|
| 2026-06-04 | Phase 0 inventory + Tier 0 test skeleton | Informed Draft 1.0 |
| 2026-06-04 | Phase 1 clean core DoD | Reinforced Tier 0 / Tier 1 boundary |
| 2026-06-03 | Optional vertical packs moved out of core registry | Tier 2 split |

---

## Revision history (this file)

| Date | Change |
|------|--------|
| 2026-06-04 | Initial CHANGELOG and semver policy (Phase 2.3) |
| 2026-06-04 | Phase 2.4: parser/spec drift CI script and Bitbucket pipeline |
| 2026-08-12 | P0 readiness notes: call-site return hints, nested schemas, WF1001 aliases |
| 2026-08-12 | Final checklist + T1/T2/T3 type maturity updates; link `roadmap-p0-types-impl.md` |
| 2026-08-12 | Declared **Final 1.0**; Tier 0 green (316); post-Final gaps owned (maintainers) |
| 2026-08-12 | A1: tools vs `response_format` Modes A/B/C clarified (PATCH docs; Unreleased) |
| 2026-08-14 | Link post-Final language constructs plan (`docs/roadmap-language-constructs.md`; PATCH docs) |
| 2026-08-14 | L1a: `validate` + nested schema fields resolve sum-type names (MINOR) |
