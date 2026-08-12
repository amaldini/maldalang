# Selective imports (`import { … } from`)

**Status:** Shipped (P1 maturity roadmap)  
**Parent:** [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md)  
**Spec:** [`docs/spec/malda-language-1.0.md`](spec/malda-language-1.0.md) §14

## Syntax

```malda
import { clamp, VERSION } from "math_utils.malda";
import { helper } from my-sdk-pack;
```

- Top-level only.
- After `import {` … `}` the next token must be the contextual word `from` (not a reserved keyword).
- Source is a string path (file module) or a package name (same resolver as `import pkg`).
- Not combinable with `import alias = …` on the same statement.
- Rename (`as`) is deferred.

## Semantics

1. Load the module as today (`ModuleLoader`, isolated env, circular detect, cache).
2. Compute the export surface (`export` markers, or all top-level if none).
3. Merge **only** the named bindings into the importer.
4. If a requested name is missing from that surface → runtime error.

Full `import "…"`, `import pkg`, and `import alias = …` are unchanged.

## Tooling

| Surface | Behavior |
|---------|----------|
| Interpreter | Selective merge + missing-name error |
| `ModuleSymbolResolver` | IDE symbols / type hints see only selected names |
| C# transpile expand | Inlines selected exported declarations only (keep selected callees self-contained or also listed) |

## Non-goals (this release)

- `import * as ns from "…"` (use `import ns = pkg` / file alias)
- `export type`
- `module { }` blocks
- Import rename (`as`)

## Example

[`Examples/Modules/selective_import.malda`](../Examples/Modules/selective_import.malda)
