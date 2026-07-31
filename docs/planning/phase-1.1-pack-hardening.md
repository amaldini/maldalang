# Phase 1.1 — Optional pack hardening (complete)

**Date:** 2026-06-04 / 2026-06-05  
**Status:** Historical summary. Vertical domain packs are out of this OSS repository.

## Outcome

- Core no longer auto-registers optional-pack globals when external DLLs happen to be present.
- `MaldaLang.Compiler` does not ProjectReference vertical pack assemblies; optional-pack transpile emit uses string-only plugins under `MaldaLang.Compiler/OptionalPack/`.
- CI guards (`CompilerPackDecouplingGuardTests`, `OptionalPackRegistryGuardTests`, `OptionalPackTranspileEmitTests`) keep core free of reintroduced pack coupling.

## Verify (core guards)

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~CompilerPackDecouplingGuardTests|FullyQualifiedName~OptionalPackRegistryGuardTests|FullyQualifiedName~OptionalPackTranspileEmitTests"
```
