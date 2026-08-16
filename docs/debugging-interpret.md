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

## VS Code / Cursor (F5)

The `vscode-malda` extension contributes debugger type `malda`. LSP stays on `malda-lsp` (`maldaLanguageServer.path`). DAP is a **second** process: the extension runs `malda debug-adapter` using `malda.cli.path` (default `"malda"`).

`dotnet run --project MaldaLang` is **not** the adapter. You need a `malda` executable that understands `debug-adapter`:

```bash
dotnet build MaldaLang -o artifacts/malda-cli
```

Then put that directory on PATH, or set `malda.cli.path` to `artifacts/malda-cli/malda` (Linux / macOS) or `artifacts/malda-cli/malda.exe` (Windows). There is no compile step for the extension; `package.json` `"main"` is `./src/extension.js`.

1. Install `vscode-malda` (Install from Location or the install script — see [`vscode-malda/README.md`](../vscode-malda/README.md)). This is not a marketplace publish.
2. Open a `.malda` file (for example `Examples/Basics/hello_world.malda`).
3. Set a glyph breakpoint on a stoppable statement (for example a `print` line).
4. Press **F5** (or Run → Start Debugging). Choose **Debug MALDA file** if prompted.

Launch snippet:

```json
{
  "type": "malda",
  "request": "launch",
  "name": "Debug MALDA file",
  "program": "${file}"
}
```

Interpret-mode only. This extension is not Desktop IDE parity (no UIHost preview, MCP UI, or virtual `@malda-section` tabs).

## What will not stop

- Block `{` lines
- `function` / `class` / `schema` / `type` / `prompt` / `workflow` / `api` / `actor` declarations
- `import` / `include` themselves — breakpoints **inside** the loaded file do stop (included statements keep `SourceFile` of the included path; imported function bodies keep the module path and pause on the **host** interpreter when those bodies run). Module load uses a child interpreter with no hook so import is not debugged concurrently.
- Transpiled `.exe`s and JS/PWA bundles
- Spawned actors: v1 does **not** share the debugger hook with child actor interpreters (concurrent pause is unsafe). Debug the actor script as a single-threaded program; spawned actors are not stepped.
- Caught MALDA `try` / `catch` exceptions. v1 pauses only on **uncaught** interpret exceptions (`RuntimeException` / `MALDAException`) with DAP `stopped` reason `exception`. `setExceptionBreakpoints` is v1.1. Control-flow (`break` / `continue` / `return`) and cancel are not exception stops.
- `await prompt …` is one statement (no fake LLM stack frame). While the model runs, the adapter emits a DAP OutputEvent (`console`, not program stdout) `await prompt …` so the UI does not look frozen.
