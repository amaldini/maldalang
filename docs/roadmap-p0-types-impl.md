# P0 types implementation plan (T1–T4)

**Status:** Implemented (2026-08-12)  
**Parent roadmap:** [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) (priority 1)  
**Spec checklist:** [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) — Final checklist section

## Design (fixed)

- **T2 = type severity only**, not full `--strict-types` suite.
- IDE/LSP default: type mismatches + unknown hints → **Error** (`StrictTypesOptions.Default`, `TypeErrors=true`).
- Opt-out: `StrictTypesOptions.Lenient`, VS Code `malda.types.strict=false`, Desktop **View → Type Errors as Errors**.
- Match / `@pure` / bounds / const remain CLI `--strict-types` / `StrictTypesOptions.Enabled` only.
- No runtime enforcement of type hints.

## Delivered workstreams

| ID | Deliverable | Primary code |
|----|-------------|--------------|
| T2 / Fase 0 | `TypeErrors` on `StrictTypesOptions`; `LanguageService.GetDiagnostics(..., options)`; LSP full path + settings; Desktop setting | `StrictTypesOptions.cs`, `LanguageService.cs`, `MaldaTextDocumentSyncHandler.cs`, `TypeAnalysisSettingsService.cs` |
| T1 / Fase 1 | Operator + curated Tier-1 builtin return inference | `TypeCompatibilityDiagnostics.cs`, `Tier1BuiltinReturnHints.cs` |
| T3 / Fase 2 | IDE `malda-schema` field diagnostics; import+validate test | `SchemaDeclarationDiagnostics.cs`, `SchemaNestedTests` |
| T4 / Fase 3 | Final checklist in CHANGELOG (Final **not** declared) | `docs/spec/CHANGELOG.md` |

## Filtered tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~TypeCompatibility|FullyQualifiedName~StrictTypesAnalysis|FullyQualifiedName~TypeHintDiagnostics|FullyQualifiedName~SchemaDeclaration|FullyQualifiedName~SchemaNested"
```

## Related docs

- [`MaldaLang.LanguageServer/README.md`](../MaldaLang.LanguageServer/README.md) — `malda.types.strict`
- [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) — silent type failures
