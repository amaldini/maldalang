# Phase 0 baseline — completion summary

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md)

## Deliverables

| Task | Artifact | Status |
|------|----------|--------|
| Builtin inventory | [core-builtin-inventory.txt](core-builtin-inventory.txt) (303 symbols) | Done |
| Parser vs manual audit | [parser-manual-drift-audit.md](parser-manual-drift-audit.md) | Done |
| Optional packs moved out of core | Done (2026-06-03); registry guards keep them out |
| Tier 0 test map | [tier0-construct-coverage.md](tier0-construct-coverage.md) | Done |
| Pack regression guard | [verify-optional-pack-registry.ps1](../../scripts/verify-optional-pack-registry.ps1), `OptionalPackRegistryGuardTests` | Done |
| Registry inventory guard | [verify-core-builtin-inventory.ps1](../../scripts/verify-core-builtin-inventory.ps1), `BuiltInRegistryInventoryTests` | Done |
| Tier 0 conformance | `MaldaLang.Tests/Conformance/Tier0/` (6 cases) | Done |

## Verification commands

```powershell
.\scripts\verify-optional-pack-registry.ps1
.\scripts\generate-core-builtin-inventory.ps1
.\scripts\verify-core-builtin-inventory.ps1

dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0ConformanceTests|FullyQualifiedName~OptionalPackRegistryGuardTests|FullyQualifiedName~BuiltInRegistryInventoryTests"
```

## Handoff to Phase 1

- P0 manual fixes merged to `master` (parser-manual drift).
- **Phase 1** complete (2026-06-04) — [phase-1-clean-core-summary.md](phase-1-clean-core-summary.md).
- **Phase 2** complete (spec, grammar, CHANGELOG, CI drift guard).
- **Next:** Phase 3 (modules) or Phase 5 (conformance expansion).
