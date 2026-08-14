# Interpret-mode debug

Source-level debugging of `.malda` in the **interpreter**. Transpile failures stay on [`debugging-transpile.md`](debugging-transpile.md) (`#line` / `build_errors.txt`). JS/PWA stays in browser DevTools.

## CLI

```bash
malda debug-adapter
```

Speaks [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/) on stdin/stdout. The process must not print CLI banners or any other non-DAP bytes on stdout.

Language intelligence stays on `malda-lsp` (a **separate** stdio process). Do not mix DAP into the language server.

Launch is interpret-only. Typical request arguments:

| Field | Meaning |
|-------|---------|
| `program` | Path to a `.malda` file (required) |
| `stopOnEntry` | Pause on the first stoppable statement |
| `cwd` | Working directory for the debuggee |
| `args` | Stored; not a language-level argv in v1 |
| `env` | Optional process environment variables |

Lines are **1-based**. Breakpoints that land on a non-stoppable line (for example a `function` declaration) map to the next stoppable statement in that file, or come back `verified: false`.

## VS Code / Cursor

F5 and `contributes.debuggers` are **not** in this workstream (D3). Until then, point a DAP client at `malda debug-adapter`.

## What will not stop

- Block `{` lines
- `function` / `class` / `schema` / `type` / `prompt` / `workflow` / `api` / `actor` declarations
- `import` / `include` themselves (stop inside the loaded file)
- Transpiled `.exe`s and JS/PWA bundles
- Spawned actors (v1 debugs the spawner script as a single thread)
