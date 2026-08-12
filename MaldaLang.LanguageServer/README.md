# MALDA Language Server (LSP)

Language Server Protocol implementation for MALDA. Provides diagnostics, completion, hover, and document symbols (outline) for `.malda` files.

## Build

From the solution root:

```bash
dotnet restore MaldaLang.LanguageServer/MaldaLang.LanguageServer.csproj
dotnet build MaldaLang.LanguageServer/MaldaLang.LanguageServer.csproj
```

Output: `MaldaLang.LanguageServer/bin/Debug/net8.0/malda-lsp.exe` (or `Release`).

## Run

The server communicates via **stdio** (stdin/stdout) using JSON-RPC. Start it as a process; the client (editor) connects to it:

```bash
dotnet run --project MaldaLang.LanguageServer
```

Or run the built executable; it will read LSP messages from stdin and write responses to stdout.

## Settings

| Setting | Default | Meaning |
|---------|---------|---------|
| `malda.types.strict` | `true` | Type-hint mismatches and unknown hints are **Errors**. Set `false` for lenient mode (Warning / Information). Does **not** enable CLI `--strict-types` extras (match exhaustiveness, `@pure`, bounds, const). |

The VS Code extension sends this via `initializationOptions.typeStrict` and `workspace/didChangeConfiguration`.

Desktop IDE: **View → Type Errors as Errors** (persisted under AppData `MaldaLang/type-analysis-settings.json`).

## Capabilities

- **textDocument/didOpen, didChange, didClose**: Document sync (full document).
- **textDocument/publishDiagnostics**: Parser errors, type analysis, and decorator validation for the current file (full file path so imports resolve), plus a slower workspace pass that republishes diagnostics for sibling `.malda` files on save and after idle edits. Type mismatches default to **Error** (`malda.types.strict`); this is not Desktop UIHost parity.
- **textDocument/completion**: Keywords (including `for`, `foreach`, `while`, `if`, etc. with snippet-style insert text), built-ins, decorators, symbols (classes, functions, variables), member completion; imports resolve when the document URI is a `file://` path.
- **textDocument/hover**: Documentation for symbols (including `schema` / sum `type`), decorators, and keywords (e.g. `foreach`, `for`, `while`, `if`, `schema`). Imported schema/type names resolve when the document path is known.
- **textDocument/documentSymbol**: Outline with classes, functions, actors, prompts, workflows, and schemas (schema fields as children).
- **textDocument/documentHighlight**: Highlights other occurrences of the identifier under the cursor in the current document.
- **textDocument/definition**: Go to definition for classes, functions, actors, prompts, schemas, workflows, and variables. Top-level declarations resolve across workspace files when a unique match is available; local symbols stay single-file.
- **textDocument/references**: Find references to the symbol at position. Top-level symbols aggregate references across workspace files; local symbols stay single-file.
- **textDocument/rename**: Rename symbol with validation. Top-level symbols rename across workspace files; local symbols stay single-file.
- **textDocument/codeAction**: Quick fixes for parser errors (e.g. insert missing brace/semicolon).
- **textDocument/signatureHelp**: Function signature and active parameter at call site.
- **textDocument/formatting**, **textDocument/rangeFormatting**: Indentation based on brace nesting (spaces or tabs).
- **workspace/symbol**: Go to symbol in workspace across discovered `.malda` files under the active workspace root (includes schemas), not just open documents.

Web IDE remains a learning playground and does not claim Desktop or LSP feature parity.

## Testing

Use VS Code with the "LSP Inspector" extension, or any LSP client that can launch a server via stdio. Configure the client to run `malda-lsp` (or `dotnet run --project MaldaLang.LanguageServer`) and use language id `malda` for `.malda` files.
