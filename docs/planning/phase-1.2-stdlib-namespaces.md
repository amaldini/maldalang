# Phase 1.2 — Stdlib namespaces (complete)

**Date:** 2026-06-04

## Canonical modules

| Module | Example | Notes |
|--------|---------|-------|
| `math` | `math.sqrt(2)`, `math.PI` | `Math` is a deprecated alias (same object) |
| `str` | `str.split("a,b", ",")` | String builtins |
| `io` | `io.readFile(path)`, `io.print(x)` | Files, paths, env, git helpers, print/input |

Flat globals (`abs`, `split`, `readFile`, …) remain for **one release** and emit IDE warning `malda-style`.

## Implementation

- `StdLibNamespaces.cs` — method sets and deprecation messages
- `StdLibModuleInstance` + `MathInstance`, `StrInstance`, `IoInstance`
- `BuiltInFunctions.RegisterBuiltIns` registers `math`, `str`, `io`
- `CSharpTranspiler` maps `math.*` / `str.*` / `io.*` to built-ins
- `StdLibNamespaceDiagnostics` — IDE warnings on flat calls and `Math.*`

## Verify

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~StdLibNamespace"
```
