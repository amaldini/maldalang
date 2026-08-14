# Interpret-mode source-level debug (implementation plan)

**Status:** Plan (not implemented)  
**Created:** 2026-08-14  
**Audience:** maintainers implementing DAP + a shared interpreter debug core  
**Spec line:** Final 1.0 stays. This is **tooling**, not a language change. No new keyword, no builtin, no spec MINOR unless a later phase adds a `debugger` statement (out of v1).

This is the plan for **source-level debugging of interpreted `.malda`**. It is not a substitute for [`docs/debugging-transpile.md`](debugging-transpile.md) (C# `#line` / `build_errors.txt`) or JS source maps.

**Not in scope for v1:** debugging transpiled `.exe`s, JS/PWA DevTools, Web IDE Desktop parity, actor multi-thread DAP, time-travel, expression-level stepping, a `debugger` keyword.

---

## Guiding principles

1. **Reuse the interpreter hook.** `IDebuggerHook` already fires in `Interpreter.ExecuteAsync`. Do not add a second walk or an eval-based debugger.
2. **One debug core, three clients.** Stepping, breakpoints, pause/resume, and variable inspect live in `MaldaLang`. Desktop IDE, Web IDE, and DAP all call that core. Do not grow a third copy of `DebuggerHook`.
3. **DAP is a process, not LSP.** `malda-lsp` owns language intelligence on stdio. Debug Adapter Protocol also needs exclusive stdio. Ship `malda debug-adapter` (CLI subcommand) rather than mixing DAP into the language server.
4. **Lines are 1-based everywhere in the core.** Tokens, AST `Node.Line`, DAP `source.line`, and stack frames use 1-based lines. Convert to 0-based only at Monaco / AvalonEdit / WPF UI edges.
5. **Statement granularity.** Stop before executing a statement. Do not instrument every `EvaluateAsync` in v1. `await prompt …` is one statement; do not pretend to step into the model.
6. **Interpreter only.** Transpile remains “compile then use the C# debugger / `#line`”. JS remains browser source maps. Document that split honestly.

---

## What is true today

| Piece | Location | Reality |
|-------|----------|---------|
| Hook interface | [`MaldaLang/Interpreter/IDebuggerHook.cs`](../MaldaLang/Interpreter/IDebuggerHook.cs) | `OnStatement` / `OnPause` / enter-exit / breakpoint / `DebugMode` |
| Pause site | [`Interpreter.ExecuteAsync`](../MaldaLang/Interpreter/Interpreter.cs) | Every statement; **converts `stmt.Line - 1`** for `OnStatement`, then `OnPause(stmt.Line)` (1-based). Busy-wait `Task.Delay(50)` up to **5 minutes** |
| Call stack | `Interpreter._callStack` / `GetCallStack()` | Pushed on function enter; **stores declaration line**, not current statement |
| Variables | `GetVariables()` | **`_globals` only**, stringified `ToString()`; locals in `_environment` are invisible |
| Source file | `stmt.SourceFile` / `_currentFile` | Set around `ExecuteAsync`; imports/includes can switch file |
| Desktop hook | [`MaldaLang.DesktopIDE/Services/DebuggerHook.cs`](../MaldaLang.DesktopIDE/Services/DebuggerHook.cs) | Full UI debug (F5 continue, step over/into/out). Breakpoints **0-based** |
| Web hook | [`MaldaLang.IDE/Services/DebuggerHook.cs`](../MaldaLang.IDE/Services/DebuggerHook.cs) | Near-duplicate of Desktop. Playground only |
| Condition eval | both `CheckBreakpointCondition` | Interpreter passes `() => true`; conditions are **stubs** |
| CLI | `malda program.malda` | No debug subcommand; `--profile` exists, `--debug` does not |
| LSP / VS Code | `MaldaLang.LanguageServer`, `vscode-malda` | Diagnostics/complete/hover only; **no `contributes.debuggers`** |
| Tests | — | **Zero** tests for `IDebuggerHook` / pause / step |
| Actors | [`ActorRuntime.SpawnActor`](../MaldaLang/Interpreter/ActorRuntime.cs) | Child interpreter **shares** the parent hook (concurrent pause is undefined) |
| Transpile debug | [`docs/debugging-transpile.md`](debugging-transpile.md) | `#line` + `build_errors.txt` — keep; do not conflate with this plan |

Desktop and Web already prove the hook can pause and step. The gap is: the core is racy and line-confused, inspect is globals-only, there is no DAP, and VS Code (the cross-platform editor) cannot debug interpret mode at all.

---

## Target architecture

```text
  VS Code / Cursor          Desktop IDE              Web IDE (playground)
        │                        │                         │
        │ DAP stdio              │ in-process              │ in-process
        ▼                        ▼                         ▼
  malda debug-adapter      DebuggerService           DebuggerService
        │                        │                         │
        └────────────┬───────────┴─────────────────────────┘
                     ▼
           MaldaLang.Interpreter.Debug
             DebugSession  (breakpoints, step mode, pause gate)
             DebugValueFormatter (RuntimeValue → inspect tree)
                     │
                     ▼
              Interpreter + IDebuggerHook
              ExecuteAsync  →  pause gate  →  statement
```

**CLI entry:** `malda debug-adapter` speaks DAP on stdin/stdout, redirects program `print` / stdout to DAP `output` events, and never prints banners. Mirrors how `malda-lsp` must own stdio.

**Do not** add a `MaldaLang.DebugAdapter` project unless the DAP surface outgrows the CLI (same reason `malda-lsp` is separate: protocol isolation). v1 can live under `MaldaLang/DebugAdapter/` referenced only by the CLI host so tests can construct `DebugSession` without spinning JSON-RPC.

---

## Line-number contract (must land in D0)

| Surface | Convention |
|---------|------------|
| Lexer / AST `Node.Line` | 1-based |
| `IDebuggerHook.OnStatement` / `OnPause` | **1-based** (change from today’s `stmt.Line - 1`) |
| DAP `SourceBreakpoint.line`, `StackFrame.line` | 1-based ([DAP spec](https://microsoft.github.io/debug-adapter-protocol/specification)) |
| Desktop / Web editor glyph | 0-based at the control; convert at the `DebuggerService` boundary |

File identity: compare breakpoints with **normalized full paths** (`Path.GetFullPath`), not `file ?? "main.malda"`. Inline eval / Web playground may keep a synthetic name (`memory:main.malda`) documented as such.

---

## Pause gate (must land in D0)

Replace the poll loop in `ExecuteAsync`:

```csharp
// today: Task.Delay(50) until DebugMode != Paused or 5 minutes
```

with a gate on `DebugSession`:

- `WaitIfPausedAsync(CancellationToken)` using `TaskCompletionSource` (or `SemaphoreSlim`).
- Continue / step / disconnect **releases** the gate; do not busy-wait.
- Disconnect / stop **cancels** the interpret `CancellationToken` so `await prompt` and HTTP servers can unwind.
- Remove the 5-minute silent resume (it looks like a continue and loses the pause).

Fast path: if `_debuggerHook` is null, `ExecuteAsync` stays as cheap as today (one null check). When a hook is attached but mode is `Continue` and the line is not a breakpoint, return without allocating.

---

## What v1 stops on

**Stoppable (before execute):** `VarDecl`, destructuring decl/assign, `Assignment`, `ExpressionStatement`, `Return`, `Throw`, `If` (condition line), `While` / `For` / `ForIn` (header line each iteration), `Print`, `Send`, `Try` (entry), `Using`, `Defer` (when it runs), workflow `step` / `await signal` / `approval`.

**Do not stop:** `BlockStatement` (the `{` line), declaration first-pass (`function` / `class` / `schema` / `type` / `prompt` / `workflow` / `api` / `actor` collect), `Include`/`Import` themselves (stop inside the loaded file’s statements instead).

**Step over / into / out:** keep the existing depth counter (`OnFunctionEnter` / `OnFunctionExit`). Fix StepInto so it does **not** also pause on function-declaration line *and* the first body statement (today both `OnFunctionEnter` and `OnStatement` can pause). One stop per user gesture.

**`await` / prompts:** the expression statement is one stop. After continue, the thread stays paused from DAP’s point of view only if the next statement is reached; while the model runs, emit an OutputEvent (`stdout` or a `telemetry`-style category) `await prompt …` so the UI does not look frozen. Do not add a fake stack frame inside the LLM client in v1.

---

## Inspect model (D1)

Replace `GetVariables()`-as-globals-strings with:

| DAP scope | Source |
|-----------|--------|
| Locals | current `_environment` own bindings (not enclosing) |
| Closure / outer | enclosing environments until globals |
| Globals | `_globals`, **omit** stdlib namespaces (`math`, `str`, `io`, builtin functions) unless a setting `malda.debug.showBuiltins` is on |
| This | `_currentObject` when inside a method |

`RuntimeValue` preview:

- Primitives: canonical `ToString()` (same as language).
- `array` / `dict` / `object` / `variant`: summary + lazy children (DAP `variablesReference`).
- `task` / `prompt` / capability token: type tag + short summary; do not expand host handles into fake fields.
- Cap tokens: show `kind` + path; do not allow watches to forge a token.

**Watches / `evaluate`:** parse a small expression with the existing parser + `EvaluateAsync` in the selected frame’s environment. Side-effecting watches are allowed but documented (a watch that calls a function runs it). v1 may reject assignments in watches.

**Call stack:** each `InterpreterCallStackFrame` stores **current statement line/file**, updated in `OnStatement` before pause. Function name / class stay as today. Top-level script is frame `"<script>"`.

---

## DAP surface (D2) — implement these requests

Minimum for VS Code F5:

| Request / event | v1 behavior |
|-----------------|-------------|
| `initialize` | `supportsConfigurationDoneRequest`, `supportsConditionalBreakpoints` (if D1.5 landed), `supportsEvaluateForHovers`, `supportsSetVariable` = false |
| `launch` | `{ program, args?, cwd?, stopOnEntry?, env? }` — interpret only |
| `setBreakpoints` | per-file; unverified if line has no stoppable statement (map to next stoppable line in that file, DAP `breakpoint.verified`) |
| `configurationDone` | start interpret if not `stopOnEntry` |
| `threads` | single thread `id=1` name `"main"` |
| `stackTrace` / `scopes` / `variables` | from `DebugSession` snapshot at pause |
| `continue` / `next` / `stepIn` / `stepOut` | map to `DebugMode` |
| `pause` | set `Paused`; takes effect on next statement (cooperative; cannot interrupt a builtin mid-call except via cancel on disconnect) |
| `evaluate` | watch / REPL in current frame |
| `disconnect` / `terminate` | cancel interpret, complete `exited` |
| `output` | program stdout/stderr |
| `stopped` | `breakpoint` / `step` / `entry` / `exception` |
| `exited` / `terminated` | process end |

**Attach** is out of v1 (no debug server sitting on a running `malda`).

**Exceptions:** on `RuntimeException` / `MALDAException`, stop with `reason: "exception"` and the message; stack from `_callStack`. Optional `setExceptionBreakpoints` in v1.1.

---

## VS Code client (D3)

[`vscode-malda/package.json`](../vscode-malda/package.json) gains:

- `contributes.debuggers` type `malda`
- `configurationAttributes.launch` (`program`, `args`, `cwd`, `stopOnEntry`)
- `initialConfigurations` / `configurationSnippets`
- `languages: ["malda"]` so breakpoints work in `.malda` editors

[`vscode-malda/src/extension.ts`](../vscode-malda/src/extension.ts): register a `DebugAdapterExecutable` that runs `malda debug-adapter` (path from new setting `malda.cli.path`, default `malda`). Keep LSP on `malda-lsp`. Two processes, two settings.

Do **not** claim Desktop UIHost / MCP UI / virtual `@malda-section` tabs in the extension README. Debug is interpret-mode only.

---

## Workstreams

### D0 — Shared debug core (blocking)

**Concrete work**

- Add `MaldaLang/Interpreter/Debug/DebugSession.cs` implementing `IDebuggerHook` (breakpoints, depth, pause gate, 1-based lines, path normalize).
- Change `ExecuteAsync` to await the gate; pass `CancellationToken` through interpret (add overload; existing `InterpretAsync` can use `CancellationToken.None`).
- Skip non-stoppable statement kinds.
- Update `_callStack` current line on each stoppable statement.
- `InternalsVisibleTo` already includes tests.
- Desktop + Web: wrap `DebugSession` instead of duplicating step logic. Keep their `DebuggerService` as UI state. Convert 0-based editor lines at that boundary.
- Fix `OnStatement` to use `stmt.Line` (1-based). **This is a breaking change for Desktop/Web breakpoint storage** — migrate `DebuggerService` in the same PR.

**Primary paths:** `IDebuggerHook.cs`, `Interpreter.cs` (`ExecuteAsync`, `GetCallStack`, function enter/exit), new `Interpreter/Debug/`, both IDE `DebuggerHook.cs` / `DebuggerService.cs`.

**Done when**

- Filtered tests: breakpoint hit, continue, step over (does not enter callee), step into, step out, `stopOnEntry`, cancel during pause, file-qualified breakpoint, no stop on `BlockStatement`.
- No 50 ms poll; no 5-minute auto-continue.
- Desktop and Web still pause/continue after the line-base migration (manual smoke on Windows for Desktop; Web playground on Linux CI is enough for the shared core).

**Risk:** Desktop breakpoint persistence (if any) stored 0-based — convert on load.

### D1 — Inspect

**Concrete work**

- `GetFrameScopes(frameId)` / `GetVariables(containerId)` with lazy `variablesReference`.
- Formatter for `RuntimeValue` (cap, task, prompt, variant, object fields).
- Locals vs globals vs this.
- Optional: `EvaluateAsync` for watches (parse snippet; document side effects).

**Done when**

- Tests cover nested dict/array children, locals inside a function, globals excluding `math`/`str`/`io` by default.
- Watch `1 + 2` and `x` in a paused function.

### D1.5 — Conditional breakpoints (optional, same PR as D1 if small)

Evaluate `breakpoint.Condition` with the watch evaluator in the current environment; break when truthy. On eval error, **break** (today’s stub already breaks on throw) and emit output `breakpoint condition error`.

### D2 — `malda debug-adapter`

**Concrete work**

- CLI: `malda debug-adapter` in [`MaldaLang/Program.cs`](../MaldaLang/Program.cs) (hidden from casual `malda` help or listed under “toolchain”).
- DAP JSON-RPC on stdio. Prefer a small hand-rolled dispatcher for the table above **or** a maintained DAP library if one is already acceptable under dual MIT/Apache (do not add a package that forces a third licence). Hand-rolled is fewer moving parts for ~15 requests.
- Redirect `Console.Out` / interpreter output callback → DAP `output`.
- Launch: lex/parse/interpret with `DebugSession` + `currentFile = program`.

**Done when**

- A headless test drives the adapter over pipes: launch a tiny `.malda`, hit a breakpoint, `stackTrace` shows the `.malda` line, `continue`, `exited`.
- `malda debug-adapter` does not print “MALDA” banners on stdout.

### D3 — VS Code / Cursor

**Concrete work**

- `contributes.debuggers` + `DebugAdapterExecutable`.
- README: how to F5 a `.malda` file; `malda.cli.path`.
- Launch snippet: `"type": "malda", "request": "launch", "program": "${file}"`.

**Done when**

- Documented steps work against a locally built `malda`. No claim of marketplace publish in this workstream.

### D4 — IDE clients on the shared core

**Concrete work**

- Delete duplicated step/breakpoint condition code from both `DebuggerHook.cs` files; they become thin UI adapters.
- Desktop: keep F5 / glyph UX; point at `DebugSession`.
- Web: keep playground debug panel; same core. **Not** DAP-in-browser.

**Done when**

- One implementation of step-over depth. Filtered tests do not need WPF.

### D5 — MALDA-specific stops (after D0–D3)

Only after VS Code can debug `hello_world.malda`:

| Item | v1.x note |
|------|-----------|
| `import` / `include` | Breakpoints in imported files already work if `stmt.SourceFile` is a real path — add a test |
| Workflow `step` | Show `step Name` as a stack frame while the body runs |
| `await prompt` | OutputEvent while waiting; still one statement |
| Exception pause | `stopped` / `exception` with message + `.malda` line |
| Actors | v1: **ignore child interpreters** (do not share the hook) or pause only the spawner thread. Sharing the hook across actor tasks is unsafe. Document “debug the actor script as a single-threaded program; spawned actors are not stepped” |

---

## Explicitly out of scope (do not start in v1)

| Item | Reason |
|------|--------|
| Debug transpiled `exe` | Use C# debugger + `#line`; see [`debugging-transpile.md`](debugging-transpile.md) |
| JS / PWA DAP | Browser already has source maps |
| Mix DAP into `malda-lsp` | Two protocols, one stdio |
| Web IDE = Desktop debug UX | Playground; honest parity |
| `debugger;` statement | Grammar + spec MINOR; a breakpoint is enough |
| Expression-level / instruction step | Every `EvaluateAsync` is too noisy and slow |
| Reverse / time-travel | No trace format for this |
| Attach to running `malda` | No debug server |
| Temporal-style workflow replay debug | Durability is single-writer SQLite; not history comparison |
| Stepping inside builtins / .NET interop | Host frames omitted |
| Public DAP library as a product | Adapter is for editors, not a second API |

---

## Tests (filtered only)

Never the full suite. Suggested filter:

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~InterpretDebug"
```

| Class (suggested) | Covers |
|-------------------|--------|
| `InterpretDebugSessionTests` | D0 pause gate, step modes, 1-based lines, file paths, skip block |
| `InterpretDebugInspectTests` | D1 scopes, locals, lazy children, hide stdlib |
| `InterpretDebugAdapterTests` | D2 DAP launch / breakpoint / stackTrace / continue over anonymous pipes |
| `InterpretDebugImportTests` | D5 imported file breakpoint |

Use a **programmable** hook or `DebugSession` directly in-process. Do not require VS Code. Tiny programs as strings (no new Examples unless useful as a manual recipe).

---

## Docs to update when implementing (not in the plan PR)

| Doc | When |
|-----|------|
| [`docs/debugging-transpile.md`](debugging-transpile.md) | Add a short “interpret debug is DAP; this page is transpile” pointer |
| New `docs/debugging-interpret.md` | User guide: VS Code F5, CLI `debug-adapter`, line numbers, what will not stop |
| [`vscode-malda/README.md`](../vscode-malda/README.md) | Debug configuration |
| [`MaldaLang.LanguageServer/README.md`](../MaldaLang.LanguageServer/README.md) | Explicit: LSP ≠ debugger |
| [`docs/start-here.md`](start-here.md) | Link interpret debug vs transpile debug |
| [`docs/architecture.md`](architecture.md) | Debug core + `malda debug-adapter` row |
| [`AGENTS.md`](../AGENTS.md) | Architecture map row; “do not mix DAP into LSP” |
| Reference Manual toolchain chapter | If there is a CLI/tools chapter; otherwise skip (not a language construct) |
| [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) | **No entry** unless a `debugger` keyword is added |

---

## Suggested PR split

Keep PRs focused (AGENTS.md). Do not land D0–D3 in one patch.

| PR | Contents |
|----|----------|
| **PR1** | D0 core + tests + IDE line-base migration (Desktop/Web still work) |
| **PR2** | D1 inspect + optional D1.5 conditions |
| **PR3** | D2 `malda debug-adapter` + pipe tests |
| **PR4** | D3 `vscode-malda` debugger contribution + README |
| **PR5** | D5 import/workflow/exception as needed |

PR1 is the only one that must change `ExecuteAsync`. Everything else builds on `DebugSession`.

---

## Success bar (v1)

A contributor on Linux or macOS, with VS Code + `vscode-malda` + a built `malda` on `PATH`, can:

1. Open `Examples/Basics/hello_world.malda`
2. Set a breakpoint on the `print` line
3. F5 (type `malda`)
4. See the yellow line on that statement, locals/globals in the sidebar, continue, and a clean exit

Desktop Windows debug keeps working via the same `DebugSession`. Web IDE remains a playground that uses the same core.

---

## Related documents

| Doc | Role |
|-----|------|
| [`docs/debugging-transpile.md`](debugging-transpile.md) | Transpile `#line` / `build_errors.txt` (different product) |
| [`docs/architecture.md`](architecture.md) | Engine map |
| [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) | D1 there was **transpile** debug docs (landed); this plan is interpret DAP |
| [`docs/profiling.md`](profiling.md) | `--profile` is not a debugger |
| [`vscode-malda/README.md`](../vscode-malda/README.md) | LSP client today |

---

## Revision history

| Date | Change |
|------|--------|
| 2026-08-14 | Initial plan: shared `DebugSession`, DAP via `malda debug-adapter`, VS Code client; D0–D5 split |
