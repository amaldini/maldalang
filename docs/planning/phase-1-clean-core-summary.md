# Phase 1 — Clean core (complete)

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md)

## Tasks

| # | Task | Summary |
|---|------|---------|
| 1.1 | Pack hardening | [phase-1.1-pack-hardening.md](phase-1.1-pack-hardening.md) |
| 1.2 | Stdlib namespaces | [phase-1.2-stdlib-namespaces.md](phase-1.2-stdlib-namespaces.md) |
| 1.3 | Syntax canon | IDE `malda-style` on `fn`/`def` |
| 1.4 | Manual alignment | [phase-1.4-manual-alignment.md](phase-1.4-manual-alignment.md) |

## Definition of done

| Criterion | Status |
|-----------|--------|
| `malda publish` default does not require optional pack DLLs | Met (2026-06-03) |
| No optional-pack symbols in `BuiltInRegistry` | Met — `OptionalPackRegistryGuardTests`, `verify-optional-pack-registry.ps1` |
| Core examples under `Examples/` run without optional pack DLLs | Met — static scan + curated smoke list |
| CI guard prevents re-adding optional-pack builtins to core registry | Met (Phase 0 + inventory tests) |

## Verification

```powershell
.\scripts\verify-optional-pack-registry.ps1
.\scripts\verify-core-examples.ps1

dotnet test MaldaLang.Tests --filter "FullyQualifiedName~CoreExamplesGuardTests|FullyQualifiedName~OptionalPackRegistryGuardTests"
```

## Interpreter / examples (2026-06-04)

- `WrapCallAsTask` prevents `async userFn()` from leaving `_environment` on a child function frame when binding task variables.
- `Examples/Basics/async_all_example.malda` uses immediate-return callees so interpreter smoke is deterministic (concurrent `async` + `sleep` before the next `var` binding is a follow-up).
- Regression: core example guard tests (static scan + curated smoke list).

## Deferred (not blocking Phase 1)

- Full interpreter smoke of every example that needs external env (LLM/Web/DB)
- Interpreter isolation for overlapping `async` user tasks with `sleep` between consecutive `var` bindings

## Handoff to Phase 2

**Phase 2 complete:** Spec [1.0](../spec/malda-language-1.0.md), grammar [34-grammar.html](../../ReferenceManual/34-grammar.html), [CHANGELOG](../spec/CHANGELOG.md), CI `verify-spec-guards.ps1`. Next: Phase 3 (modules) or Phase 5 (conformance matrix).
