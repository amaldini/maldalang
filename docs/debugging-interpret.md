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

The `vscode-malda` extension contributes debugger type `malda`. LSP stays on `malda-lsp` (`maldaLanguageServer.path`). DAP is a **second** process: the extension runs `malda debug-adapter` using `malda.cli.path` (default `"malda"`). To run without debugging and see `io.print` in the Terminal, use **MALDA: Run File** (editor play button).

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
- Transpiled `.exe`s and JS/PWA bundles (except Desktop IDE F5 for `dom.*` / `game.*` / `three.*`, which uses WebView2 + source maps — see below)
- Spawned actors: v1 does **not** share the debugger hook with child actor interpreters (concurrent pause is unsafe). Debug the actor script as a single-threaded program; spawned actors are not stepped.
- Caught MALDA `try` / `catch` exceptions. v1 pauses only on **uncaught** interpret exceptions (`RuntimeException` / `MALDAException`) with DAP `stopped` reason `exception`. `setExceptionBreakpoints` is v1.1. Control-flow (`break` / `continue` / `return`) and cancel are not exception stops.
- `await prompt …` is one statement (no fake LLM stack frame). While the model runs, the adapter emits a DAP OutputEvent (`console`, not program stdout) `await prompt …` so the UI does not look frozen.

## Desktop IDE JavaScript debug

`dom.*` / `game.*` / `three.*` programs (for example `Examples/Games/maldanoid.malda`) throw in the interpreter. Desktop IDE **F5** detects those APIs, transpiles to JavaScript with a VLQ source map, loads the result in Web Preview, and uses WebView2's Chromium debugger so glyph breakpoints, continue, and step hit `.malda` lines.

Full-stack files (`@client()` / `@javascript()` plus `@server()` / `@csharp()` or a route decorator) start **both** sessions: the interpreter debugs the host partition (client-only functions are skipped) and Web Preview debugs the JavaScript partition. Continue / step follow whichever side last paused; Pause stops both. Output is labeled `[server]` and `[client]`. One current-line highlight and inspect panel is shown at a time. `@shared()` bodies can stop in either runtime.

Ctrl+F5 on browser-only files opens Web Preview without attaching the debugger. Ctrl+F5 on full-stack files still offers the Server / Client preview / Full stack run dialog. Watch expressions and breakpoint conditions on the client are JavaScript. VS Code F5 (`malda debug-adapter`) stays interpret-only. Compiled `.js` + `.map` still work in browser DevTools.
