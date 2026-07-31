# MALDA Language Support (VS Code / Cursor)

This extension adds language support for **MALDA** (`.malda` files) in VS Code and Cursor by starting the MALDA Language Server. You get:

- Diagnostics (parser errors, decorator validation)
- Completion (keywords, built-ins, symbols)
- Hover documentation
- Go to definition / Find references / Rename
- Code actions (quick fixes)
- Signature help
- Formatting
- Workspace symbol search

## Prerequisites

- **MALDA Language Server** executable (`malda-lsp` or `malda-lsp.exe`). Build it from the MALDA repo or obtain it from your distribution.
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

- **`maldaLanguageServer.path`** (default: `"malda-lsp"`)  
  Path to the MALDA language server executable. Use a full path if it's not on your system PATH (e.g. `C:\path\to\malda-lsp.exe` on Windows).

- **`maldaLanguageServer.trace`** (default: `"off"`)  
  Set to `"messages"` or `"verbose"` to trace LSP communication in the output panel.

## After installing

1. Open a `.malda` file (or set the language mode to **MALDA**).
2. The extension will start the language server automatically. If the executable path is wrong, check **View → Output → MALDA** for errors and set `maldaLanguageServer.path` to the correct path.
