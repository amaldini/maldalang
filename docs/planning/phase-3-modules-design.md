# Phase 3 — Modules and boundaries (design)

**Status:** Approved for implementation (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 3  
**Spec target:** [malda-language-1.0.md](../spec/malda-language-1.0.md) § Modules (interim)

---

## Problem

| Mechanism | Scope | Pollution |
|-----------|--------|-----------|
| `include "path"` | Parse-time splice into one AST | All top-level symbols become globals in the host file |
| `using Package` | Runtime isolated env, merge into host | Same as package load; `using Alias = P` gives a namespace object |
| `loadNativeModule("…")` | Native DLL hook | Returns a host object; not a Malda module boundary |

Scaling to SDK packs and user libraries needs **isolated evaluation** plus an explicit **export surface**, without breaking existing scripts.

---

## Goals (Phase 3)

1. **`import`** — canonical way to load a module (file or installed package).
2. **`export`** — mark which top-level bindings are visible to importers.
3. **Compatibility** — `include` and `using` remain supported; documented migration path.
4. **DoD** — ≥3 production-style examples use `import` for pack SDKs; sum-type export rule recorded below.

---

## Syntax (MVP — implemented in 3.2)

### Import

```malda
// File module (isolated env, merge exports)
import "../../packages/my-sdk-pack/entry.malda";

// Installed package (same resolver as `using`)
import my-sdk-pack;
import sdk = my-sdk-pack;

// Legacy (unchanged)
using helpers;
include "legacy.malda";
```

**Rules**

- Top-level only (like `include` / `using`).
- File paths: string literal, resolved relative to the **importer’s** `SourceFile` directory (same rules as `include`).
- Package imports: identifier + optional `.submodule` + optional `Alias =` (parser parity with `using`).

**Deferred (3.3+)**

- `import { helper, Api } from "…";` selective imports
- `import * as sdk from "…";` (use `import sdk = pkg` today)
- `module Name { … }` block syntax

### Export

```malda
export function helper(x) { … }
export var api = new Api();
export class Api { … }
```

- Only on **top-level** `function`, `var`, `class`.
- If a file contains **at least one** `export` declaration, only exported names are merged into the importer.
- If a file has **no** `export`, all top-level bindings are exported (backward compatible with current package modules and SDK `.malda` files).

### Sum types (design decision)

| Rule | Detail |
|------|--------|
| Constructors | Scoped to the module file where the `type` is declared |
| Cross-module use | Importer must `import` the module and use exported type tags via normal `match`; re-export with `export type` in a later phase |
| Phase 3 MVP | No `export type` yet; sum types in pack files stay internal unless accessed through exported functions |

Recorded in spec § Modules (interim).

---

## Runtime architecture

```
Importer (host Environment)
    │
    ▼
import path / import pkg
    │
    ▼
ModuleLoader
    ├─ parse module file
    ├─ ModuleExports.CollectExplicitExports(AST)
    ├─ Interpreter in isolated Environment
    └─ merge: exported symbols only (or all if no export keyword)
```

- Circular imports: detected via load stack (same as circular `include`).
- Cache: keyed by resolved absolute path or `package[.sub]`.
- `using` and `import` share merge logic (`Interpreter.ImportExecutor`).

---

## Compatibility matrix

| Feature | Phase 3 behavior |
|---------|------------------|
| `include` | Unchanged (parse-time inline) |
| `using` | Unchanged; alias of `import` for packages |
| `import` | Preferred for new code |
| SDK `packages/*.malda` | Works via **file** `import` without install; optional `import pack-name` when package is installed |
| Transpiler / IDE | 3.3: `DependencyAnalyzer` + completion for `import` |

---

## Migration (3.4)

1. Replace `include "../../packages/…/entry.malda"` → `import "../../packages/…/entry.malda"`.
2. When packages are published to `.malda` storage, switch to `import pack-name;`.
3. Add `export` to new library files; tighten existing SDK files in a follow-up (non-breaking while zero `export` lines exist).

---

## Testing

- `ImportExecutionTests` — package + file import, export filtering, alias
- Examples under `Examples/Modules/`
- Spec/grammar drift guards when `Lexer.cs` / `Parser.cs` change

---

## Follow-ups (post–3.3)

- `module { }` blocks and `export type`
- Transpiler per-module C# namespaces (3.3 inlines file exports into `GeneratedCode`)
- Package `import malda-*` in transpiler (file-path import supported)
- Workspace resolver: `MALDA_SDK_ROOT` / repo `packages/` for `import malda-*` without install

## Shipped in 3.3

- `MaldaLang/IDE/ModuleSymbolResolver.cs` — shared import graph for tooling
- `CSharpTranspiler` — `ExpandFileImportsForTranspile` before codegen
- `getSymbols` — `imports` array + `fromModule` on imported symbols
- `LanguageService` / LSP — package and `.malda` path completion on `import` / `using`
