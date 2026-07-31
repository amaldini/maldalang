# MALDA architecture (OSS core)

Short map of how the open-source core fits together. For agent workflow rules, see [`AGENTS.md`](../AGENTS.md).

## Pipeline

```text
.malda source
    │
    ▼
 Lexer (MaldaLang/Lexer.cs)
    │
    ▼
 Parser → AST (MaldaLang/Parser/)
    │
    ├──────────────────┬──────────────────────┐
    ▼                  ▼                      ▼
 Interpreter      C# transpiler           JS / PWA transpiler
 (run now)        (MaldaLang.Compiler)    (JsTranspiler)
    │                  │                      │
    ▼                  ▼                      ▼
 RuntimeValue     .exe / DLL + runtime    browser / PWA bundle
 + BuiltIns
```

Language intelligence (completions, diagnostics, hover) shares `MaldaLang/IDE/LanguageService.cs` across Desktop IDE, Web IDE, and the LSP project.

## Projects

| Project | Responsibility |
|---------|----------------|
| `MaldaLang` | CLI (`malda`), lexer/parser/interpreter, builtins, shared `LanguageService` |
| `MaldaLang.Compiler` | C# / JS / PWA compile and publish orchestration |
| `MaldaLang.UIHost` | Server-driven UI host support used by runtime / Desktop |
| `MaldaLang.IDE` | Blazor **Web IDE** (playground) |
| `MaldaLang.DesktopIDE` | WPF **Desktop IDE** (reference) |
| `MaldaLang.LanguageServer` | LSP server process |
| `MaldaLang.TestLib` / `MaldaLang.Tests` | Shared test helpers and automated tests |
| `vscode-malda` | VS Code extension (client) |
| `Examples/`, `Templates/` | Samples and `malda new` scaffolds |
| `ReferenceManual/` | HTML language reference |
| `conformance/` | Spec / tier0 conformance assets |

## Execution modes

- **Interpret:** `malda program.malda` — AST walked by `Interpreter`.
- **Transpile to C#:** `malda compile … --mode transpile` — emits C# that calls into MALDA runtime helpers / builtins.
- **JS / PWA:** `--mode js` / `--mode pwa` — subset of language + browser runtime (`mlRuntime`).

Optional vertical packs are **out of tree**. The compiler may still contain **string-only** emit hooks under `MaldaLang.Compiler/OptionalPack/` so external DLLs can plug in without being ProjectReferences of core.

## Built-ins

Built-ins are the largest “surface area” for language work:

- Implementation / dispatch: `MaldaLang/BuiltIns/BuiltInFunctions.cs`
- Registry metadata: `MaldaLang/BuiltIns/BuiltInRegistry.cs`
- Interpreter recognition: `Interpreter.IsBuiltIn`
- Transpile recognition + codegen: `CSharpTranspiler.IsBuiltInFunction` / `TranspileBuiltInFunction`

Missing one registration site is a common bug (works interpreted XOR transpiled).

## Web stack (language feature, not Web IDE)

MALDA programs can host HTTP/UI via builtins and decorators (`@GET`, `@PAGE`, …). Examples live under `Examples/Web/` and `Templates/`. This is separate from the **Web IDE** Blazor app in `MaldaLang.IDE`.

## Docs layout

| Path | Trust level |
|------|-------------|
| `ReferenceManual/` | User-facing language reference |
| `docs/spec/` | Spec / matrix notes |
| `docs/start-here.md`, `docs/architecture.md` | Onboarding |
| `docs/javascript-backend.md`, `docs/profiling.md`, … | Topic guides |
| `docs/planning/` | Historical roadmap — verify against code before trusting |

## Design preferences (OSS)

- Prefer clear examples with `function` keyword.
- Prompt params are untyped names only.
- Keep Desktop vs Web IDE documentation honest about parity.
- Prefer filtered tests and small smoke programs over whole-suite runs.
