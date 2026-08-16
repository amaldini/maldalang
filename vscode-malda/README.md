# MALDA Language Support (VS Code / Cursor)

This extension adds language support for **MALDA** (`.malda` files) in VS Code and Cursor by starting the MALDA Language Server. You get:

- Syntax coloring (TextMate grammar aligned with `Lexer.cs`)
- Diagnostics (parser errors, decorator validation)
- Completion (keywords, built-ins, symbols)
- Hover documentation
- Go to definition / Find references / Rename
- Code actions (quick fixes)
- Signature help
- Formatting
- Workspace symbol search
- **Interpret-mode debug** (F5): breakpoints, step, continue, and inspect via `malda debug-adapter`

This is not a marketplace publish of the extension. It is the in-repo client: Install from Location (or the install script / a locally packed VSIX).

The extension is **not** the Desktop IDE. It does not provide UIHost preview, MCP UI, or virtual `@malda-section` tabs.

There is **no compile step**. `package.json` `"main"` is `./src/extension.js` (plain JavaScript). `src/extension.ts` is kept in sync as a typed reference; you do not need to emit `out/`.

## Prerequisites

- **MALDA Language Server** executable (`malda-lsp` or `malda-lsp.exe`). Build it from the MALDA repo or obtain it from your distribution. Setting: `maldaLanguageServer.path`.
- **MALDA CLI** that understands `debug-adapter` (`malda` or `malda.exe`) if you want F5. Setting: `malda.cli.path` (default `"malda"`). This is a **second** process, not the language server.
- **Node.js** (only for `npm install` of the extension dependencies; the extension code is plain JavaScript, no TypeScript required).

## Installation in Cursor (or VS Code)

### Option A: Install using the MALDA script (recommended if you have MALDA)

1. From the repo root, set the path to the extension folder and run the install script:
   - **PowerShell:** `$env:MALDA_EXTENSION_SOURCE = (Resolve-Path "vscode-malda").Path` then `malda run install-malda-extension.malda`
   - **cmd:** `set MALDA_EXTENSION_SOURCE=%CD%\vscode-malda` then `malda run install-malda-extension.malda`
2. The script copies the extension into `%USERPROFILE%\.vscode\extensions\malda-language` and `%USERPROFILE%\.cursor\extensions\malda-language`.
3. **Reload** Cursor or VS Code when prompted.

### Option B: Install from the folder (development / local)

1. **Prepare the extension** (install Node dependencies only; no compile step): `cd vscode-malda` then `npm install`.

2. In Cursor: Command Palette → **Developer: Install Extension from Location...** → select the `vscode-malda` directory.

3. **Reload** the window when prompted.

### Option C: Package as VSIX and install

1. Install the [VS Code Extension Manager](https://marketplace.visualstudio.com/items?itemName=ms-vscode.vscode-pack) (e.g. `npm install -g @vscode/vsce`).
2. In `vscode-malda`: `npm install` then `npx vsce package`.
3. In Cursor: **Extensions: Install from VSIX...** and select the generated `.vsix` file.

## Configuration

Two processes, two settings:

- **`maldaLanguageServer.path`** (default: `"malda-lsp"`)  
  Path to the MALDA language server executable. Use a full path if it's not on your system PATH (e.g. `C:\path\to\malda-lsp.exe` on Windows), or `${workspaceFolder}/artifacts/malda-lsp/malda-lsp.exe` in this repo. LSP only — this is **not** the debugger.

- **`malda.cli.path`** (default: `"malda"`)  
  Path to the MALDA CLI used for interpret-mode debug. The extension runs `malda debug-adapter` (stdio DAP). Use a full path if `malda` is not on PATH.

- **`maldaLanguageServer.trace`** (default: `"off"`)  
  Set to `"messages"` or `"verbose"` to trace LSP communication in the output panel.

`dotnet run --project MaldaLang` is **not** the debug adapter. The editor must spawn a `malda` executable that implements the `debug-adapter` subcommand. Build one, then point the setting at it:

```bash
dotnet build MaldaLang -o artifacts/malda-cli
```

Then set `malda.cli.path` to:

- Linux / macOS: `artifacts/malda-cli/malda` (absolute path recommended)
- Windows: `artifacts/malda-cli/malda.exe`

## After installing

1. Open a `.malda` file (or set the language mode to **MALDA**).
2. The extension will start the language server automatically. If the executable path is wrong, check **View → Output → MALDA** for errors and set `maldaLanguageServer.path` to the correct path.

## Interpret-mode debug (F5)

Debug is **interpret-mode only**. Transpiled `.exe`s use the C# debugger and `#line` mapping ([`docs/debugging-transpile.md`](../docs/debugging-transpile.md)). JS/PWA stays in browser DevTools. User notes: [`docs/debugging-interpret.md`](../docs/debugging-interpret.md).

1. Put `malda` on PATH, **or** set `malda.cli.path` to a built CLI as above.
2. Open a `.malda` file (for example `Examples/Basics/hello_world.malda`).
3. Click the gutter to set a glyph breakpoint (language `malda`).
4. Press **F5** (or Run → Start Debugging). If prompted, pick **MALDA** / **Debug MALDA file**.

Launch configuration (`launch.json` snippet):

```json
{
  "type": "malda",
  "request": "launch",
  "name": "Debug MALDA file",
  "program": "${file}"
}
```

Optional launch fields: `args`, `cwd`, `stopOnEntry`, `env`.

The debugger factory starts `malda debug-adapter` using `malda.cli.path`. Language intelligence stays on `malda-lsp`. Do not mix DAP into the language server.
