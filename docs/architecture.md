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
| `MaldaLang.UIHost` | Server-driven UI host support used by runtime / Desktop — see [`docs/ui-framework.md`](ui-framework.md) |
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

Server-driven `ui.*` trees, patch protocol, and `MaldaLang.UIHost` wiring are documented in [`docs/ui-framework.md`](ui-framework.md). Language API for components and controls: [`ReferenceManual/23-web-ui.html`](../ReferenceManual/23-web-ui.html).

## Docs layout

| Path | Trust level |
|------|-------------|
| `ReferenceManual/` | User-facing language reference |
| `docs/spec/` | Spec / matrix notes |
| `docs/spec/backend-capability-matrix.md` | Interpreter vs C# vs JS product capabilities |
| `docs/start-here.md`, `docs/architecture.md` | Onboarding |
| [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) | P0 maturity roadmap (workstreams complete 2026-08-12; next = post-Final / deferred) |
| [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) | Post-Final language constructs (schema/sum types, Mode C, budget, workflow determinism, grounded values, capability tokens) |
| [`docs/workflows-ha.md`](workflows-ha.md) | Durable workflows: single-writer + read-only ops model (W2) |
| `docs/javascript-backend.md`, `docs/ui-framework.md`, `docs/profiling.md`, `docs/benchmarks.md`, … | Topic guides |
| `docs/planning/` | Historical roadmap — verify against code / ReferenceManual / AGENTS.md ([README](planning/README.md)) |

## Design preferences (OSS)

- Prefer clear examples with `function` keyword.
- Prompt params are untyped names only.
- Keep Desktop vs Web IDE documentation honest about parity.
- Prefer filtered tests and small smoke programs over whole-suite runs.

## P0 types / schema (post–Phase 6)

- Call-site type hints: [`MaldaLang/IDE/TypeCompatibilityDiagnostics.cs`](../MaldaLang/IDE/TypeCompatibilityDiagnostics.cs) infers declared callee return types (imports via [`ModuleSymbolResolver`](../MaldaLang/IDE/ModuleSymbolResolver.cs)).
- Nested `schema` fields: [`SchemaRegistry`](../MaldaLang/BuiltIns/SchemaRegistry.cs) expands sibling schemas inline and may name a sum type; unknown names / cycles fail on resolve. `validate("Name", …)` resolves both schema and sum-type names. Sum-type constructors may optionally type payloads (`Buy(sku: string, qty: int)`); name-only stays permissive.
- P0 maturity plan (landed): [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md). Post-Final gaps: [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md). Next language constructs: [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md).
- Workflow HA / multi-worker (single writer + read-only ops, SQLite limits): [`docs/workflows-ha.md`](workflows-ha.md).
- Workflow determinism: fixed WF1001/WF1002 deny-list in [`BuiltInRegistry.GetWorkflowBehavior`](../MaldaLang/BuiltIns/BuiltInRegistry.cs); IDE same-file call-graph in [`WorkflowDeterminismDiagnostics`](../MaldaLang/IDE/WorkflowDeterminismDiagnostics.cs) (imported/unknown callees are WF1005 Info; not Temporal-style history detection).
- Resource bounds: [`@within(ms)`](../MaldaLang/Interpreter/WithinBoundsContext.cs) for wall-clock; [`@budget(tokens, tools, cost?)`](../MaldaLang/Interpreter/ResourceBoundsContext.cs) for prompt/agent-turn abort (env `MALDA_AGENT_CONTEXT_BUDGET_TOKENS` remains context-trim only).
- Grounded values: [`grounded.wrap`](../MaldaLang/BuiltIns/GroundedStdLib.cs) wraps a payload with `{ source, id?, span? }` citations; GraphMemory [`ask`](../MaldaLang/BuiltIns/GraphMemory.cs) / `query(..., { grounded: true })` is the opt-in ASK path. Not a `match`-visible kind.
- Capability tokens: [`cap.fileRead`](../MaldaLang/BuiltIns/CapStdLib.cs) mints an unforgeable FileRead handle; `cap.read` / `io.readFile` consume it. Object literals cannot forge a token. `@effects("io")` stays a name allow-list.
