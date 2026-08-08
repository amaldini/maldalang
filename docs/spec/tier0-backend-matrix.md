# Tier 0 backend matrix (Phase 5)

**Status:** Active (2026-06-04)  
**Suite:** `conformance/tier0/`  
**Runner:** `MaldaLang.Tests/Conformance/Tier0/Tier0ConformanceRunner.cs`  
**Product-level features (agents, HTTP, DOM):** see [backend-capability-matrix.md](backend-capability-matrix.md)

## Backends in CI

| Backend | Enforced | Threshold |
|---------|----------|-----------|
| Interpreter | Yes | 100% of manifest cases with `"interpreter": true` |
| C# transpile | Yes | ≥ 95% of cases with `"csharp": true` (currently 100% on enabled subset) |
| JavaScript | Opt-in pilot | 100% of cases with `"javascript": true` when Node + runtime are available; otherwise skipped |

## JavaScript pilot (101 cases — full Tier 0)

All Tier 0 cases run via `JsTranspiler` + Node.js + `Examples/Web/wwwroot/malda-js-runtime.js`. See [phase-5-js-tier0-pilot.md](../planning/phase-5-js-tier0-pilot.md) and [phase-5-js-tier0-rollout.md](../planning/phase-5-js-tier0-rollout.md).

- Enable a case: add its `.malda` filename to `$jsPilot` in `scripts/sync-tier0-manifest.ps1`, re-sync manifest.
- Pilot cases omit `jsSkipReason`; all other cases keep:

`jsSkipReason`: `JavaScript Tier 0 backend is not part of CI; see docs/spec/tier0-backend-matrix.md`

**CI behavior:** If Node or the runtime is absent, `JavaScript_MatchesExpected` tests are not discovered and the matrix gate does not require JS parity. When both are present (local dev), the gate requires **100%** pass on enabled pilot cases.

## C# transpile skips (documented)

**Current:** none — all **100** manifest cases have `"csharp": true` (burn-down completed 2026-06-05; Phase 7 cases added 2026-06-05).

To exclude a case from C# CI, add it to `$csharpSkip` in `scripts/sync-tier0-manifest.ps1` and re-sync the manifest.

## Property tests (`runProperty`)

Tier 0 file case: `run-property-stable.malda` (`runProperty` with fixed seed/iterations). Extended property packs remain in `RunPropertyBuiltInTests` / `PropertyRunnerTests`.

## Reporting

`Tier0BackendMatrixTests.BackendMatrix_MeetsTier0ParityThresholds` prints pass rates to test output. When `TIER0_PARITY_OUT` is set (CI via `verify-spec-guards.ps1`), it also writes:

- `parity-report.json` — machine-readable pass rates and failures
- `parity-report.md` — human-readable summary

```powershell
.\scripts\run-tier0-conformance.ps1
.\scripts\report-tier0-parity.ps1
```
