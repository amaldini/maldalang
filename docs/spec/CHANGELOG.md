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

Current line: **Draft 1.0** ([malda-language-1.0.md](malda-language-1.0.md), 2026-06-04).

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
| [22-grammar.html](../../ReferenceManual/22-grammar.html) aligned with parser | **PATCH** (spec 1.0 unchanged) |
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
4. If syntax changes: update [22-grammar.html](../../ReferenceManual/22-grammar.html) and `ReferenceManualGrammarCoverageTests`.  
5. Phase 2.4: `scripts/verify-spec-parser-drift.ps1` and `bitbucket-pipelines.yml` fail PRs that touch `Parser.cs` or `Lexer.cs` without spec/grammar/CHANGELOG update.

**Implementation precedence during Draft 1.0:** interpreter + Tier 0 tests → spec prose → Reference Manual.

---

## Release history (spec line)

### [Unreleased]

#### Added

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

#### Planned (documented in spec §15 and roadmap Phase 4)

- ~~**MINOR** (with deprecation): `typeOf` tags `int`, `dict`, `bool`, `variant`, `task`~~ — **shipped** (4.2).  
- ~~**MINOR**: `--strict-types` mode; exhaustive `match` diagnostics~~ — **shipped** (4.3).  
- ~~**MINOR**: `Result` / `Option` stdlib~~ — **shipped** (4.4).

### [1.0.0-draft] — 2026-06-04 (Phase 3 modules)

#### Added (MINOR — additive)

- Keywords `import` and `export`; file and package import with isolated module environments.  
- Spec §14 and [phase-3-modules-design.md](../planning/phase-3-modules-design.md).  
- Grammar: `ImportStmt`, `ExportableDecl` in [22-grammar.html](../../ReferenceManual/22-grammar.html).

#### Implementation (Phase 3.2)

- `ModuleLoader.LoadFileModuleAsync`, `ModuleExports` filtering, `ImportStatement` in parser/interpreter.

### [1.0.0-draft] — 2026-06-04

**Status:** Draft — Tier 0 conformance in progress (Phase 5).

#### Added (normative documentation)

- Initial [malda-language-1.0.md](malda-language-1.0.md): value model, null, truthiness, `match`, sum types, `async`/`await`/`all`, actors, `typeOf`/`isNumber`, dictionary missing-key → `null`.  
- Expanded [22-grammar.html](../../ReferenceManual/22-grammar.html) (Phase 2.2).  
- This CHANGELOG and semver policy (Phase 2.3).

#### Implementation alignment (already shipped in toolchain)

- **Pack isolation (2026-06-03):** optional vertical-pack symbols removed from `BuiltInRegistry`. Spec: out of Tier 0.  
- **Phase 1.1:** optional-pack bootstrap auto-globals removed from core.  
- **Phase 1.2:** `math`, `str`, `io` namespaces; flat globals deprecated (one-release policy).  
- **Interpreter:** `WrapCallAsTask` for `async userFn()` environment binding (2026-06-04).

#### P0 readiness notes (2026-08-12) — types / schema (not Final yet)

- **Call-site checking:** `--strict-types` / IDE warnings cover callee return hints for same-unit and imported functions (operators and built-in returns still not inferred).  
- **Nested schemas:** field types may name other schemas (`Other` / `Other[]`); unknown field types and cycles error on resolve; no silent map-to-`string`.  
- **Workflow (minimal):** WF1001 also denies `randomChoiceWeighted` and `randn`; durability remains single-box SQLite + deny-list (not Temporal replay detection).

#### Known draft gaps (not version bumps until fixed or declared Final)

- `typeOf(variant)` and `typeOf(task)` return `"unknown"`.  
- Concurrent `async` user calls with `sleep` between consecutive `var` bindings (documented limitation).  
- Multi-backend parity (C#/JS) not yet gated on spec Final.  
- Spec remains **Draft 1.0** until Tier 0 conformance + the P0 gates above stay green and Final is declared.

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
