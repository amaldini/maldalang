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
 (run now)        (MaldaLang.Compiler)    (JsTranspiler + GlslTranspiler)
    │                  │                      │
    ▼                  ▼                      ▼
 RuntimeValue     .exe / DLL + runtime    browser / PWA bundle
 + BuiltIns
```

Interpreter `async` tasks use a per-task [`InterpreterActivation`](../MaldaLang/Interpreter/InterpreterActivation.cs) (`AsyncLocal`). Overlapping user functions that `sleep` do not share `_environment`, `this`, or execution stacks. Spawned actors still get a new `Interpreter`.

Language intelligence (completions, diagnostics, hover) shares `MaldaLang/IDE/LanguageService.cs` across Desktop IDE, Web IDE, and the LSP project.

Interpret-mode debug core: [`MaldaLang/Interpreter/Debug/DebugSession.cs`](../MaldaLang/Interpreter/Debug/DebugSession.cs) (breakpoints, step mode, pause gate, 1-based lines, DAP-shaped scopes / lazy children / watches / conditional breakpoints; include/import file BPs, workflow `step` frames, uncaught exception pause). Spawned actors do not share the hook. Desktop and Web wrap that session via `IHasDebugSession`. DAP stdio is `malda debug-adapter` ([`MaldaLang/DebugAdapter/`](../MaldaLang/DebugAdapter/)); LSP stays `malda-lsp`. User notes: [`docs/debugging-interpret.md`](debugging-interpret.md). Transpile failures stay on [`docs/debugging-transpile.md`](debugging-transpile.md). Desktop IDE F5 on `dom.*` / `game.*` / `three.*` programs uses WebView2's Chromium debugger plus JS source maps instead of the interpreter hook. Full-stack files (`@client()` plus `@server()` or a route) start both: interpret on the host partition and WebView2 on the client.

## Projects

| Project | Responsibility |
|---------|----------------|
| `MaldaLang` | CLI (`malda`), lexer/parser/interpreter, builtins, shared `LanguageService`, `malda check` (diagnose without execute; `--json` for agents), `malda debug-adapter` (DAP stdio) |
| `MaldaLang.Compiler` | C# / JS / PWA compile and publish orchestration; JS-mode `@shader()` → GLSL via `GlslTranspiler` |
| `MaldaLang.UIHost` | Server-driven UI host support used by runtime / Desktop — see [`docs/ui-framework.md`](ui-framework.md) |
| `MaldaLang.IDE` | Blazor **Web IDE** (playground) |
| `MaldaLang.DesktopIDE` | WPF **Desktop IDE** (reference) |
| `MaldaLang.LanguageServer` | LSP server process (`malda-lsp`; not DAP) |
| `MaldaLang.TestLib` / `MaldaLang.Tests` | Shared test helpers and automated tests |
| `vscode-malda` | VS Code / Cursor extension: LSP client + interpret-mode debugger type `malda` (`malda debug-adapter`) |
| `Examples/`, `Templates/` | Samples and `malda new` scaffolds (`webapi`, `fullstack`, `game`, `agent`) |
| `ReferenceManual/` | HTML language reference (English canonical; Italian in `ReferenceManual/it/`) |
| `conformance/` | Spec / tier0 conformance assets |

## Execution modes

- **Interpret:** `malda program.malda` — AST walked by `Interpreter`.
- **Transpile to C#:** `malda compile … --mode transpile` — emits C# that calls into MALDA runtime helpers / builtins.
- **JS / PWA:** `--mode js` / `--mode pwa` — subset of language + browser runtime (`mlRuntime`). `@shader()` functions compile to GLSL strings via `glsl.compile` (not a fourth backend). `malda play` is the JS preview inner loop (`malda new game`).

Optional vertical packs are **out of tree**. The compiler may still contain **string-only** emit hooks under `MaldaLang.Compiler/OptionalPack/` so external DLLs can plug in without being ProjectReferences of core.

## Built-ins

Built-ins are the largest “surface area” for language work:

- Implementation / dispatch: `MaldaLang/BuiltIns/BuiltInFunctions.cs`
- Registry metadata: `MaldaLang/BuiltIns/BuiltInRegistry.cs`
- Interpreter recognition: `Interpreter.IsBuiltIn`
- Transpile recognition + codegen: `CSharpTranspiler.IsBuiltInFunction` / `TranspileBuiltInFunction`

Missing one registration site is a common bug (works interpreted XOR transpiled). CI `InterpretTranspilePairTests` asserts the same stdout and exit on interpret and C# transpile for a curated offline set ([`docs/roadmap-trust.md`](roadmap-trust.md) DT7).

## Web stack (language feature, not Web IDE)

MALDA programs can host HTTP/UI via builtins and decorators (`@GET`, `@PAGE`, …). Examples live under `Examples/Web/` and `Templates/`. Browser `game.*` / `three.*` samples are under `Examples/Games/`. This is separate from the **Web IDE** Blazor app in `MaldaLang.IDE`.

Server-driven `ui.*` trees, patch protocol, and `MaldaLang.UIHost` wiring are documented in [`docs/ui-framework.md`](ui-framework.md). Language API for components and controls: [`ReferenceManual/24-web-ui.html`](../ReferenceManual/24-web-ui.html).

## Docs layout

| Path | Trust level |
|------|-------------|
| `ReferenceManual/` | User-facing language reference (English canonical; Italian in [`ReferenceManual/it/`](../ReferenceManual/it/)) |
| `docs/spec/` | Spec / matrix notes |
| `docs/spec/backend-capability-matrix.md` | Interpreter vs C# vs JS product capabilities |
| `docs/start-here.md`, `docs/architecture.md` | Onboarding |
| [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) | P0 maturity roadmap (workstreams complete 2026-08-12; next = post-Final / deferred) |
| [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) | Post-Final language constructs (schema/sum types, Mode C, budget, workflow determinism, grounded values, capability tokens) |
| [`docs/roadmap-trust.md`](roadmap-trust.md) | Post-Final trust plan (strict compile, transpile smoke, interpret/transpile pairs, loud gotchas; toolchain 1.0.0 landed) |
| [`docs/roadmap-interpret-debug.md`](roadmap-interpret-debug.md) | Interpret-mode source-level debug — D0–D3 and D5 landed (`malda debug-adapter`, VS Code type `malda`, MALDA-specific stops) |
| [`docs/roadmap-games.md`](roadmap-games.md) | Browser games kit (`game.*` / `three.*` JS-only; G0–G16 landed) |
| [`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md) | 2D kit vs Love2D / Pico-8 / Phaser / Godot (evaluation, not a roadmap) |
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
- Nested `schema` fields: [`SchemaRegistry`](../MaldaLang/BuiltIns/SchemaRegistry.cs) expands sibling schemas inline and may name a sum type; unknown names / cycles fail on resolve. `validate("Name", …)` resolves both schema and sum-type names and leaves tagged dicts as dicts. `asVariant("Name", …)` coerces a tagged dict (or an existing variant) into a variant for `match`. `evalPrompt(instance, fixture)` runs the same coerce path as `await prompt … -> Type` on a fixture with no LLM. Sum-type constructors may optionally type payloads (`Buy(sku: string, qty: int)`); name-only stays permissive.
- P0 maturity plan (landed): [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md). Post-Final gaps: [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md). Next language constructs: [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md). Trust / publish: [`docs/roadmap-trust.md`](roadmap-trust.md). Browser games kit: [`docs/roadmap-games.md`](roadmap-games.md).
- Workflow HA / multi-worker (single writer + read-only ops, SQLite limits): [`docs/workflows-ha.md`](workflows-ha.md).
- Workflow determinism: fixed WF1001/WF1002 deny-list in [`BuiltInRegistry.GetWorkflowBehavior`](../MaldaLang/BuiltIns/BuiltInRegistry.cs); IDE same-file call-graph in [`WorkflowDeterminismDiagnostics`](../MaldaLang/IDE/WorkflowDeterminismDiagnostics.cs) (imported/unknown callees are WF1005 Info; not Temporal-style history detection).
- Resource bounds: [`@within(ms)`](../MaldaLang/Interpreter/WithinBoundsContext.cs) for wall-clock; [`@budget(tokens, tools, cost?)`](../MaldaLang/Interpreter/ResourceBoundsContext.cs) for prompt/agent-turn abort (env `MALDA_AGENT_CONTEXT_BUDGET_TOKENS` remains context-trim only).
- Grounded values: [`grounded.wrap`](../MaldaLang/BuiltIns/GroundedStdLib.cs) wraps a payload with `{ source, id?, span? }` citations; GraphMemory [`ask`](../MaldaLang/BuiltIns/GraphMemory.cs) / `query(..., { grounded: true })` is the opt-in ASK path. Not a `match`-visible kind.
- Capability tokens: [`cap.fileRead`](../MaldaLang/BuiltIns/CapStdLib.cs) mints an unforgeable FileRead handle; `cap.read` / `io.readFile` consume it. Object literals cannot forge a token. `@effects("io")` stays a name allow-list. First-contact scaffold: `malda new agent` (`Templates/agent/`).
