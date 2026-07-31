# Tier 0 language conformance

Semantic checks for **Malda Core** (no optional vertical packs).

See [phase-5-conformance.md](../../../docs/planning/phase-5-conformance.md) and [tier0-backend-matrix.md](../../../docs/spec/tier0-backend-matrix.md).

## File-driven suite (primary)

| Area | Example cases |
|------|----------------|
| `match` (literal, object, array, rest) | `match-literal.malda`, `match-object-simple.malda`, `match-array-rest.malda` |
| Dictionary missing key → `null` | `dict-missing-null.malda` |
| `typeOf` / `isTag` | `typeof-*.malda`, `is-tag-legacy.malda` |
| Sum types + `match` | `sum-type-match.malda`, `sum-type-divide-ok.malda` |
| `async` / `await` / `all` | `async-await.malda`, `all-variadic.malda` |
| `result` / `option` | `result-map-unwrap.malda`, `option-some-map.malda` |

| Pipe / comprehension / resources / `const` | `pipe-sort.malda`, `list-comprehension-filter.malda`, `defer-lifo.malda`, `using-dispose.malda`, `const-read.malda` |

**101** cases — run via `Tier0MaldaConformanceTests` and `Tier0BackendMatrixTests`. JavaScript pilot: **89** cases when Node + `malda-js-runtime.js` are available ([phase-5-js-tier0-rollout.md](../../../docs/planning/phase-5-js-tier0-rollout.md)).

## Spec anchors (`Tier0ConformanceTests`)

Eleven facts delegate to manifest file cases (former inline sources). Keeps spec §15 anchors and CI filter stable without duplicating Malda source in C#.

## Run

```powershell
.\scripts\run-tier0-conformance.ps1
.\scripts\report-tier0-parity.ps1
```

## Adding cases

1. Add `cases/<name>.malda` and `cases/<name>.expect`.
2. Run `scripts/sync-tier0-manifest.ps1` (or `generate-tier0-cases.ps1` for scripted batches).
3. Document semantics in [malda-language-1.0.md](../../../docs/spec/malda-language-1.0.md) §15 when introducing new construct coverage.
