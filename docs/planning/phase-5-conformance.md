# Phase 5 — Multi-backend conformance

**Status:** Complete (2026-06-05) — refinements done  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 5

## Delivered

| Item | Output |
|------|--------|
| 5.1 File suite | `conformance/tier0/cases/*.malda` + `.expect`, `manifest.json` (**101** cases) |
| 5.1 Runner | `Tier0ConformanceRunner`, `Tier0MaldaConformanceTests` |
| 5.2 Backend matrix | `Tier0BackendMatrixTests`, [tier0-backend-matrix.md](../spec/tier0-backend-matrix.md) |
| 5.3 `runProperty` | `run-property-stable.malda` in manifest |
| 5.4 CI hook | `scripts/run-tier0-conformance.ps1`, `verify-spec-guards.ps1` filter extended |
| 5.4 Parity report | `scripts/report-tier0-parity.ps1` → `artifacts/tier0/parity-report.{json,md}` (CI artifact) |
| Spec anchors | `Tier0ConformanceTests` delegates to file cases (no duplicated inline sources) |
| Pattern migration | Batch 3 object/destructuring match cases from `PatternMatchingTests` |

## Case count vs roadmap DoD

Roadmap target: **≥ 80** Tier 0 cases. Current manifest: **100** cases.

Add cases under `conformance/tier0/cases/`, run `scripts/sync-tier0-manifest.ps1`, and extend `scripts/generate-tier0-cases-batch*.ps1` when adding batches.

## C# parity

- **101** cases run on C# transpile in CI (0 documented skips).
- Gate: `Tier0BackendMatrixTests` requires 100% interpreter pass and ≥ 95% C# pass on the enabled subset.

## JavaScript pilot

- **89** cases with `"javascript": true` ([phase-5-js-tier0-rollout.md](phase-5-js-tier0-rollout.md); started at 8 in [phase-5-js-tier0-pilot.md](phase-5-js-tier0-pilot.md)).
- Gate: 100% pass on the enabled subset when Node.js and `malda-js-runtime.js` are available; otherwise skipped.
- Probe tool: `Tier0JavaScriptPilotProbeTests` with `MALDA_JS_PROBE=1`.

## Commands

```powershell
.\scripts\run-tier0-conformance.ps1
.\scripts\generate-tier0-cases.ps1      # batch 1 + 2 + 3 + sync manifest
.\scripts\generate-tier0-cases-batch2.ps1
.\scripts\generate-tier0-cases-batch3.ps1
.\scripts\sync-tier0-manifest.ps1
.\scripts\report-tier0-parity.ps1
```

## CI artifacts

`verify-spec-guards.ps1` sets `TIER0_PARITY_OUT=artifacts/tier0` before running `Tier0BackendMatrixTests`. Bitbucket Pipelines publishes `parity-report.json` and `parity-report.md`.
