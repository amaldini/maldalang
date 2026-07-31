# Tier 0 conformance suite (Phase 5)

File-driven semantic tests for Malda Core. Each case is a pair:

- `cases/<name>.malda` — program under test
- `cases/<name>.expect` — expected stdout (normalized line endings, trailing whitespace trimmed)

`manifest.json` lists case metadata, spec anchors, and per-backend eligibility.

## Run locally

```powershell
.\scripts\run-tier0-conformance.ps1
```

Or:

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0MaldaConformanceTests|FullyQualifiedName~Tier0BackendMatrixTests"
```

## Add a case

1. Add `.malda` / `.expect` under `cases/` (or extend `scripts/generate-tier0-cases.ps1`).
2. Run `scripts/sync-tier0-manifest.ps1` (or add the case to `scripts/sync-tier0-manifest.ps1` skip/spec maps first).
3. If C# transpile is not ready, add the file to `$csharpSkip` in `sync-tier0-manifest.ps1` and re-sync.
4. Run the conformance script above.

## Backends

| Backend | CI | Notes |
|---------|-----|-------|
| Interpreter | Yes | Default gate |
| C# transpile | Yes | Subset with documented skips |
| JavaScript | Pilot (89 cases) | Opt-in when Node + runtime present; see [phase-5-js-tier0-rollout.md](../../docs/planning/phase-5-js-tier0-rollout.md) |
