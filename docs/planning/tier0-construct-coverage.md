# Tier 0 construct coverage map (Phase 0)

**Date:** 2026-06-04  
**Purpose:** Map language constructs to existing tests and [malda-language-1.0.md](../spec/malda-language-1.0.md) before Phase 5 multi-backend conformance expansion.

## File-driven conformance (`conformance/tier0/`)

**101** cases in `manifest.json` (interpreter + C#). See [phase-5-conformance.md](phase-5-conformance.md) and [tier0-backend-matrix.md](../spec/tier0-backend-matrix.md).

| Construct | Example case file | Spec § |
|-----------|-------------------|--------|
| `match` | `match-literal.malda`, `match-object-*.malda`, `match-array-rest.malda` | §9 |
| Dictionary missing key → `null` | `dict-missing-null.malda` | §5.3 |
| `typeOf` / `isTag` | `typeof-*.malda`, `is-tag-legacy.malda` | §4.3 |
| Sum types + `match` | `sum-type-match.malda`, `sum-type-err-branch.malda` | §8–9 |
| `async` / `await` / `all` | `async-await.malda`, `all-variadic.malda` | §11 |
| `result` / `option` stdlib | `result-map-unwrap.malda`, `option-some-map.malda` | §8 |
| `?.` / `?[]` | `null-conditional-*.malda` | §5 |
| Tagged `catch` | `catch-io-filter.malda`, `catch-fallback-generic.malda` | §12 |
| Actors | `actor-send-order.malda` (interpreter) | §13 |
| Control flow | `foreach-sum.malda`, `for-loop-count.malda`, `ternary-true-branch.malda` | §7 |
| Pipe / comprehension | `pipe-sort.malda`, `list-comprehension-filter.malda`, `dict-comprehension-map.malda` | §18 |
| `using` / `defer` | `using-dispose.malda`, `defer-lifo.malda` | §18 |
| `const` | `const-read.malda` | §18 |

Run:

```powershell
.\scripts\run-tier0-conformance.ps1
```

## Spec anchors (`MaldaLang.Tests/Conformance/Tier0/Tier0ConformanceTests.cs`)

Eleven `[Theory]` facts delegate to file cases (spec §15 T0-01…T0-06 anchors). Add new behavior to `conformance/tier0/cases/` only.

| Construct | Test | Spec § |
|-----------|------|--------|
| `--strict-types` (static) | `StrictTypesAnalysisTests.Tier0ConformanceSnippets_PassStrictAnalysis` | Phase 4.3 |

## Registry / pack guards

| Concern | Test / script |
|---------|----------------|
| Optional-pack symbols not in core registry | `OptionalPackRegistryGuardTests` |
| Registry ↔ inventory sync | `BuiltInRegistryInventoryTests` |
| Pack symbols not in registry (shell) | `scripts/verify-optional-pack-registry.ps1` |
| Inventory ↔ registry (shell) | `scripts/verify-core-builtin-inventory.ps1` |

## Related coverage (not Tier 0 conformance suite)

| Area | Location | Phase |
|------|----------|-------|
| Pattern matching (extended edge cases) | `PatternMatchingTests.cs` | Supplemental; core in file suite |
| Destructuring | `match-object-*.malda`, variable tests | Tier 0 file suite |
| Actors parity | `ActorParityTests.cs` | Phase 5 |
| Property runner (`runProperty`) | `RunPropertyBuiltInTests.cs`, `PropertyRunnerTests.cs` | Platform QA, not Tier 0 semantics |
| Optional-pack emit / decoupling | `OptionalPackTranspileEmitTests`, `CompilerPackDecouplingGuardTests` | Core guards |

## Remaining (post–Phase 5)

- JavaScript Tier 0 gap closure (89/101 enabled — [phase-5-js-tier0-rollout.md](phase-5-js-tier0-rollout.md))
- Optional: migrate remaining `PatternMatchingTests` edge cases if not covered by file suite

## Regenerate builtin inventory

```powershell
.\scripts\generate-core-builtin-inventory.ps1
.\scripts\verify-core-builtin-inventory.ps1
```
