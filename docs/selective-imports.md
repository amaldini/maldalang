# Selective imports (`import { … } from`)

**Status:** Shipped (P1 maturity roadmap) + `export type` / `export schema`  
**Parent:** [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md)  
**Spec:** [`docs/spec/malda-language-1.0.md`](spec/malda-language-1.0.md) §14

## Syntax

```malda
import { clamp, VERSION } from "math_utils.malda";
import { helper } from my-sdk-pack;
import { Result, Contact } from "types_lib.malda";
```

- Top-level only.
- After `import {` … `}` the next token must be the contextual word `from` (not a reserved keyword).
- Source is a string path (file module) or a package name (same resolver as `import pkg`).
- Not combinable with `import alias = …` on the same statement.
- Rename (`as`) is deferred.

## Export surface

| Declaration | Open module (no `export`) | Module with any `export` |
|-------------|---------------------------|---------------------------|
| `function` / `var` / `class` | All top-level | Only `export …` |
| `type` | Surfaced (constructors merge) | Requires `export type` (constructors included) |
| `schema` | Surfaced for IDE + `validate` after load | Requires `export schema` |

`export type T` adds **T** and all of T’s **constructors** to the export name set. Selecting `T` in a selective import merges those constructors into the host. Selecting a constructor name also expands to the type declaration for transpile/IDE.

## Semantics

1. Load the module as today (`ModuleLoader`, isolated env, circular detect, cache).
2. Compute the export surface (`export` markers, or all top-level if none).
3. Expand selective names for sum types (type ↔ constructors).
4. Merge **only** the named value bindings into the importer; schema/type names may be selected without a runtime binding.
5. If a requested name is missing from that surface → runtime error.

Full `import "…"`, `import pkg`, and `import alias = …` are unchanged.

## Tooling

| Surface | Behavior |
|---------|----------|
| Interpreter | Selective merge + missing-name error; constructors from `export type` |
| `ModuleSymbolResolver` | IDE symbols / type hints see exported (or open-module) types/schemas; selective expands type↔ctors |
| C# transpile expand | Inlines selected exported declarations (including `type` / `schema`) |

## Non-goals

- `import * as ns from "…"` (use `import ns = pkg` / file alias)
- `module { }` blocks
- Import rename (`as`)

## Examples

- [`Examples/Modules/selective_import.malda`](../Examples/Modules/selective_import.malda)
- [`Examples/Modules/export_type_schema.malda`](../Examples/Modules/export_type_schema.malda)
