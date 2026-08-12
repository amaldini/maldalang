# Phase 3 — Modules and boundaries (summary)

**Status:** Complete (2026-06-04)  
**Design:** [phase-3-modules-design.md](phase-3-modules-design.md)

## Delivered

| Task | Outcome |
|------|---------|
| 3.1 | Design doc, spec §14, grammar `ImportStmt` / `ExportableDecl` |
| 3.2 | `import` / `export`, isolated file modules, `ModuleLoader` |
| 3.3 | `ModuleSymbolResolver`, transpiler inline, `getSymbols`, IDE/LSP completion |
| 3.4 | Workspace SDK resolver (`MALDA_PACKAGES_DIR`, repo `packages/`) |

## Workspace packages (3.4 closure)

`import my-sdk-pack;` resolves to `packages/my-sdk-pack/*.malda` when:

- `MALDA_PACKAGES_DIR` points at the repo `packages/` folder, or
- `MALDA_SDK_ROOT` contains `packages/`, or
- the process cwd is inside a tree that has a `packages/` directory (walk-up discovery).

Installed packages in `~/.malda` take precedence; workspace is the fallback.

## Deferred

- `module { }` blocks (historical; `export type` / `export schema` shipped post-Final — see [`docs/selective-imports.md`](../selective-imports.md))
- Per-module C# namespaces in transpiler
- Embedded-package symbol load in IDE (`embedded:` paths)

**Shipped later (P0 maturity P1):** selective `import { … } from` — see [`docs/selective-imports.md`](../selective-imports.md).

**Next:** [Phase 4 — Gradual types](malda-language-purity-roadmap.md#phase-4--gradual-types--correctness-10-12-weeks) (4.1 started: `TypeHintDiagnostics`).
