# Phase 5 — JavaScript Tier 0 rollout

**Status:** Complete (2026-06-05) — **101/101** Tier 0 cases  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) steps 18–21  
**Pilot baseline:** [phase-5-js-tier0-pilot.md](phase-5-js-tier0-pilot.md)

## Objective

Expand the opt-in JavaScript conformance subset from 8 primitive cases to all Tier 0 programs that pass interpreter + C#.

## Method

1. `Tier0JavaScriptPilotProbeTests` — set `MALDA_JS_PROBE=1` to run every non-pilot case through `Tier0JavaScriptRunner`.
2. Add passing filenames to `$jsPilot` in `scripts/sync-tier0-manifest.ps1`.
3. Re-sync manifest and run `JavaScript_MatchesExpected` + matrix gate.

## Result

| Metric | Count |
|--------|------:|
| Tier 0 cases | 101 |
| JS enabled | **101** |
| JS passing (local) | **101** |
| Documented gaps | **0** |

## Batch 5 — expressiveness, resources, stdlib, properties (2026-06-05)

| Item | Change |
|------|--------|
| Runtime | `pushDeferFrame` / `registerDefer` / `runAndPopDeferFrame`, `disposeResource`, `rangeBuiltin`, `joinBuiltin`, `sortBuiltin`, `result.*`, `option.*`, `runProperty` |
| Transpiler | `defer`, `using`, classes, `new`, lambdas, pipe `\|>`, list/dict comprehensions, property registry, `await` for `sleep`/async calls |
| Tier 0 | +12 cases (remaining gaps) |
| Tests | `Tier0JavaScriptBatch5Tests` |

## Enabled construct coverage (batch 2)

- Arithmetic, comparisons, logic, ternary, strings
- `if` / `while` / `for`, `break` / `continue`
- Dict literals, bracket/dot access, missing key → `null`
- Arrays: index, `length`, empty length
- `match` (literal, array, object, wildcard, default, nested, block expression)
- Sum types + variant constructors
- `async` / `await` (literal task)
- `const` read
- Small recursion (`fibonacci-small`)

## Batch 3 — type introspection + `all()` (2026-06-05)

| Item | Change |
|------|--------|
| Runtime | `typeOf`, `isTag`, `isNumber`, `all`, `markDict` in `malda-js-runtime.js` |
| Transpiler | `JsTranspiler` maps builtins; `dict { }` literals emit `mlRuntime.markDict(...)` |
| Tier 0 | +16 cases (`typeof-*`, `is-tag-*`, `is-number`, `all-*`) |

## Batch 4 — collections, control flow, exceptions (2026-06-05)

| Item | Change |
|------|--------|
| Runtime | `arrayAppend`, `getMemberNullSafe`, `getIndexNullSafe`, `throwMalda`, `unwrapMaldaException` |
| Transpiler | `for-in`, `try`/`catch` (filtered), `throw`, `.append()`, null-conditional `?.` / `?[]` |
| Continue fix | Desugared `for` → `while` loops: `continue` emits increment before `continue` (matches interpreter) |
| Tier 0 | +11 cases (`array-append-length`, `for-continue-skip`, `foreach-sum`, `null-conditional-*`, `try-catch-*`, `catch-*`, `match-no-default-error`) |
| Tests | `Tier0JavaScriptBatch4Tests` smoke suite for batch 4 cases |

## Verify

```powershell
$env:MALDA_JS_PROBE = "1"
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0JavaScriptPilotProbeTests"
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0JavaScriptBatch5Tests"
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0MaldaConformanceTests.JavaScript_MatchesExpected"
```

## Deferred

- CI: install Node + pin runtime in pipeline
- Optional-pack emit plugin split (compiler housekeeping)
