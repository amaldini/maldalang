# Phase 4.3 — `--strict-types` and exhaustive `match`

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 4.3

## Goal

Optional static checks for teams that want stricter correctness without changing default (dynamic) behavior.

## Shipped

- CLI flag **`--strict-types`** on script execution (`malda script.malda --strict-types`, `malda -e "..." --strict-types`).
- **`StrictTypesAnalysis`** — runs before interpreter when flag is set; exit code 1 on errors.
- **Unknown type hints** → `malda-types` **Error** (default IDE mode stays **Info**).
- **Non-exhaustive `match` on sum types** → `malda-match` **Error** when a typed sum variable is matched without `default`, catch-all (`_` / identifier), or all variant constructors.
- **`SumTypeIndex`**, **`MatchExhaustivenessDiagnostics`**, tests in `StrictTypesAnalysisTests`.

## Usage

```powershell
malda Examples/Basics/hello_world.malda --strict-types
```

IDE/LSP continues to show informational type-hint diagnostics unless a future client enables strict mode.

## Next

- ~~Phase 4.4 — `Result` / `Option` stdlib~~ — [phase-4.4-result-option.md](phase-4.4-result-option.md)
- IDE setting to surface strict diagnostics as errors in the editor
