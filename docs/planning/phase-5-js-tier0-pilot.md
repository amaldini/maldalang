# Phase 5 — JavaScript Tier 0 backend pilot

**Status:** Complete (2026-06-05)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) step 17

## Objective

Run a small, opt-in subset of Tier 0 conformance cases through `JsTranspiler` + Node.js and `malda-js-runtime.js`, without blocking CI when Node or the runtime is absent.

## Delivered

| Item | Output |
|------|--------|
| Runner | `Tier0JavaScriptRunner` — transpile, execute via Node wrapper, normalize stdout |
| Matrix | `Tier0BackendKind.JavaScript` in `Tier0ConformanceRunner`; JS stats on `Tier0BackendMatrixReport` |
| Tests | `Tier0MaldaConformanceTests.JavaScript_MatchesExpected` (skips when runtime unavailable) |
| Gate | `Tier0BackendMatrixTests` requires 100% JS pass on enabled pilot cases when Node + runtime are present |
| Manifest | `$jsPilot` in `scripts/sync-tier0-manifest.ps1` — **8** cases with `"javascript": true` |
| Docs | [tier0-backend-matrix.md](../spec/tier0-backend-matrix.md) updated for pilot semantics |

## Pilot cases (8 / 101)

| ID | File | Spec |
|----|------|------|
| T0-005 | `arithmetic-print.malda` | §7 |
| T0-027 | `equality-primitives.malda` | §7 |
| T0-029 | `float-literal-print.malda` | §7 |
| T0-034 | `greater-equals-int.malda` | §7 |
| T0-042 | `logical-not.malda` | §7 |
| T0-055 | `match-literal.malda` | §9 |
| T0-079 | `string-concat.malda` | §7 |
| T0-087 | `ternary-true-branch.malda` | §7 |

Add or remove cases by editing `$jsPilot` in `scripts/sync-tier0-manifest.ps1`, then re-sync the manifest. Only include cases that pass locally before enabling.

## Runtime requirements

- **Node.js** on `PATH`, or set `MALDA_NODE_PATH` to the executable.
- **`malda-js-runtime.js`** at `Examples/Web/wwwroot/malda-js-runtime.js`, or set `MALDA_JS_RUNTIME_PATH`.

When either is missing, JS theory tests are not discovered and the matrix test logs a skip reason instead of failing.

## Verify

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0MaldaConformanceTests.JavaScript_MatchesExpected"
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0BackendMatrixTests"
```

## Follow-up: batch 2 rollout

Expanded to **62** passing cases — see [phase-5-js-tier0-rollout.md](phase-5-js-tier0-rollout.md).

## Deferred

- Close 39 documented JS gaps (`typeOf`, `try`/`catch`, pipe/comprehensions, etc.)
- Enforce JS parity in CI (install Node + bundle runtime in pipeline)
