# MALDA engineering improvements roadmap

**Status:** I1, I2, I3, I4 and I6 landed 2026-10-15; I5 three named files split (follow-up list open); I7, I8 and I10 open; I9 dropped (root files are intentional)  
**Created:** 2026-10-15  
**Audience:** maintainers choosing engine / tooling work after P0, L1–L6 and DT0–DT8

This plan covers work that the other roadmaps do not track: tooling, backend parity checks,
and the size of the engine's largest files. Language constructs stay in
[`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md), trust work in
[`docs/roadmap-trust.md`](roadmap-trust.md).

**Not in scope here:** closing L1c (stays gated in the constructs plan), Web IDE parity with
Desktop, actor supervision trees, new top-level builtins, JavaScript parity for host-only
features (agents, HTTP, workflows, prompts).

---

## Guiding principles

1. **Move code before changing it.** File splits are pure moves, one file per PR, with the
   fuzz and pair tests green.
2. **Tools enforce what docs only describe.** A gotcha that a tool can detect becomes a
   diagnostic; the doc then links to it.
3. **The capability matrix is executable.** Every `no` row for JavaScript has a compile-time
   diagnostic and a fixture.
4. **No new type system.** Type inference produces editor warnings only; runtime behavior
   and compile success do not change.

---

## Themes and priority

| Rank | Workstream | Status | Why | Risk | Size |
|------|------------|--------|-----|------|------|
| 1 | **I1** Split `Program.cs` into commands | Landed | 5,217 lines; ~25 subcommands in one switch | Low | M |
| 2 | **I2** Differential fuzz in the nightly workflow | Landed | Backend divergence was only checked on 3 seeds | Low | S |
| 3 | **I3** `malda fmt` | Landed | No CLI / CI formatter; only the IDE indenter exists | Medium | M |
| 4 | **I4** JavaScript capability pre-check | Landed | Unsupported features failed late, one `NotSupportedException` at a time | Medium | M |
| 5 | **I5** Split `Interpreter.cs` / `Parser.cs` / `LanguageService.cs` | Partly landed | The three named files are split; other engine files still exceed ~1,500 lines (see I5) | Low | L |
| 6 | **I6** `malda lint` (gotchas as diagnostics) | Landed | Common footguns live only in `docs/llm/` | Medium | M |
| 7 | **I7** Pure stdlib complete on JavaScript | Open | `math` / `str` / collections are `partial` on JS | Medium | M–L |
| 8 | **I8** Gradual type inference in the editor | Open | Warnings without the deferred full static type system | High | L |
| 9 | **I9** Repository root cleanup | Dropped | See I9 below | — | — |
| 10 | **I10** Git-index package registry | Open | Bridge toward the deferred public registry | Medium | L |

---

## Sequencing

```text
Phase A (low risk, no behavior change)
  I2 nightly fuzz
  I1 split Program.cs
       └─ I5 split Interpreter / Parser / LanguageService (one PR per file)

Phase B (user-facing tools)
  I3 malda fmt ──► I6 malda lint (shared trivia-preserving infrastructure + rules)
  I4 JavaScript capability pre-check

Phase C (language and ecosystem)
  I7 JavaScript stdlib (driven by I2 fuzz and I4 diagnostics)
  I8 gradual inference (after I5: LanguageService already split)
  I10 Git-index registry
```

---

## I1 — CLI commands as separate files

- Same pattern as the `BuiltInFunctions` split: `Program` is `partial`, and each area is a
  pure move into `MaldaLang/Program.<Area>.cs` (`Check`, `Compile`, `Run`, `Repl`, `Workflow`,
  `Assistant` (cron / gateway / Telegram), `Memory`, `Onboard`, `Status`, `Agent`, `Help`,
  `Packages`, `New`). No command interface was introduced: it would change behavior surface for
  no gain over the partials.
- `Program.cs` keeps console setup, `Main` (global parsing + dispatch), stdin helpers and
  `GetVersion` (643 lines, from 5,217). The largest partial is `Program.Compile.cs` (~1,100).
- Verified: no line lost or duplicated, CLI smoke (`run`, `check --json`, `--help`,
  `workflow --help`), `LicenseHeaderGuardTests`, `ShipContractGuardTests`, `TranspileSmokeTests`.

## I2 — Fuzz in the nightly workflow

- `.github/workflows/tier0-nightly.yml` job `fuzz` (Linux, Node 20) runs
  `BackendDifferentialFuzzTests` over 100 seeds. The window starts at
  `1000 + (days since epoch mod 500) * 100`, so it moves every night and wraps after 500 days.
- A failing seed uploads `/tmp/malda_fuzz/` as the `malda-fuzz-repro` artifact.
- PR CI keeps the three fixed seeds.

## I3 — `malda fmt`

- The lexer keeps comments (trivia); printing is idempotent.
- Modes: `malda fmt file|dir`, `--check` for CI, `--stdin` for LSP and IDE.
- Wire it to `textDocument/formatting` in `malda-lsp`; replace `MaldaIndentFormatter` in the
  Desktop IDE.
- **Done when:** formatting twice is a no-op on all `Examples/`, interpreter stdout is identical
  before and after formatting, and the `ReferenceManual` guards stay green.
- **Status (2026-10-15): landed.** The `Lexer` is unchanged. `SourceTriviaScanner`
  (`MaldaLang/Formatting/SourceTrivia.cs`) is a separate lossless scan that mirrors the lexer's
  token boundaries (operators, numbers, every string and interpolation form) and keeps whitespace,
  newlines, `//` and `/* */` comments as trivia; `SplitLines` groups it into logical lines (a
  multi-line string or block comment belongs to the line where it starts). `MaldaFormatter`
  works one logical line at a time and never joins or splits lines: indentation from bracket depth,
  `match` case bodies, wrapped operator lines and `if (` / `while (` conditions; spacing around
  operators, commas and braces (package names such as `malda-demo-math` in `import` lines are
  left alone); trailing whitespace; at most one blank line; one final newline. String tokens are
  copied verbatim, `// @malda-section` markers stay in column 0, CRLF and a BOM are kept. Safety
  net: the output is lexed again and must give the same token types and lexemes, otherwise the
  input comes back unchanged with an error; sources the lexer rejects are never formatted.
  `malda fmt` (`Program.Fmt.cs`, `FmtCommandRunner`) has `--check`, `--stdin`, `--indent-size`,
  `--use-tabs`. `malda-lsp` full and range formatting and the Desktop IDE *Format Document* use
  the same formatter; `MaldaIndentFormatter` remains only as the rename-edit applier.
  `MaldaFormatterTests` checks the scan is lossless and formatting idempotent on every `Examples/`
  file, interpreter stdout before/after on 16 deterministic examples, comments, strings and
  prompts, CLI modes and the LSP handler. `Examples/` is not reformatted in the repo
  (75 of 282 files would change). I6 should build on `SourceTriviaScanner` for comment-based
  suppressions.

## I4 — JavaScript capability pre-check

- One AST pass before JavaScript emit, reading the same features as
  [`backend-capability-matrix.md`](spec/backend-capability-matrix.md) (prompts, file I/O,
  workflows, VectorDB, HttpServer, …). It reports **every** violation with line and column
  instead of stopping at the first exception.
- `NotSupportedException` in `JsTranspiler.*.cs` stays as a safety net.
- **Done when:** `BackendCapabilityMatrixGuardTests` checks that each JavaScript `no` row has a
  diagnostic and a fixture.
- **Status (2026-10-15): landed.** `MaldaLang.Compiler/JsCapabilityChecker.cs` runs on the
  JavaScript partition after target partitioning and before `JsTranspiler`. Codes `JS0001`–`JS0013`
  (table in [`backend-capability-matrix.md`](spec/backend-capability-matrix.md)); `--mode js` /
  `--mode pwa` fail with every violation as `Line L:C - JS0xxx: …` and write no JavaScript.
  Names the program declares (a user `function readFile`, `class VectorDB`) are not reported;
  `@property` blocks that do not target `js` are skipped. `JsCapabilityCheckerTests` holds one
  fixture per code; `EveryJavaScriptNoRow_HasPreCheckDiagnosticAndFixture` maps each matrix `no`
  row to a rule (the `actors` property tag is exempt: JavaScript runs actors process-local). A
  sweep of `Examples/` found no false positives: every newly rejected example uses agents, LLM
  clients, files, servers, `ui.*`, jobs or databases. `malda check` has no backend option, so the
  pre-check runs on compile only.

## I5 — Split the largest files

- Same pattern as the `BuiltInFunctions` and transpiler splits: partials by area
  (`Interpreter.Expressions`, `.Statements`, `.Classes`, …; `Parser.Declarations`,
  `.Expressions`, `.Prompts`, …).
- One PR per file, moves only.
- **Done when:** no engine file exceeds ~1,500 lines and fuzz plus `InterpretTranspilePairTests`
  are green.
- **Status (2026-10-15):** `Interpreter.cs`, `Parser.cs`, `LanguageService.cs`,
  `BuiltIns/Conversation.cs`, `BuiltIns/GraphMemory.cs`, `BuiltIns/HttpServer.cs`,
  `BuiltIns/WebRuntime.cs`, `BuiltIns/RestServer.cs` and `BuiltIns/BuiltInTools.cs` are split
  (the tool switch in `ExecuteToolOperation` now chains per-area `Execute<Area>Tool` methods).
  The Desktop IDE `MainWindow.xaml.cs`, `MainWindow.Editor.cs` and `MainWindow.Run.cs` are split
  into more `MainWindow.<Area>.cs` partials. No engine or Desktop IDE file is still over ~1,500 lines.

## I6 — `malda lint`

- First rules from the gotchas: interpolation on a plain string, unchecked `parseJson`,
  ambiguous Mode B prompts, non-deterministic calls in workflows beyond the deny-list.
- Stable codes (`ML0xxx`), per-line suppression, `--json` output like `check`.
- **Done when:** every gotcha in `docs/llm/` links to a rule or is marked docs-only.
- **Status (2026-10-15): landed.** `MaldaLang/Linting/` (`MaldaLinter` + `.Rules` / `.Workflows`
  partials, `LintRules`, `LintSuppressions`, `LintCommandRunner`; CLI in `Program.Lint.cs`). Rules
  run on the parser AST through `AstNodeWalker` (moved from `MaldaLang.Compiler` into
  `MaldaLang/Parser/AST/` so both share it) and, for constructs the parser drops, on
  `SourceTriviaScanner` tokens. Codes: `ML0000` the file does not parse (error); `ML0001`
  `{a.b}` / `{a[i]}` / `{f(x)}` in a plain string (plain `{name}` stays check's `malda-interp`);
  `ML0002` `parseJSON` of model output never validated; `ML0003` Mode B prompt (info); `ML0004`
  model / network / file / repository / job effects in a workflow body outside a step, through
  same-file helpers, beyond the WF1001/WF1002 deny-list; `ML0005` assignment in an
  expression-bodied lambda; `ML0006` `step` in a loop; `ML0007` `case NAME:` on a `var`;
  `ML0008` always-true condition (`0`, `""`, `[]`, `.length`); `ML0009` `result.map` /
  `option.map` returning `Ok` / `Some`; `ML0010` `str.*` on `io.getEnv(…)`; `ML0011`
  `str.repeat(s, n / 2)`; `ML0012` numeric `ui.dispatchEvent` session; `ML0013` `case` after an
  unguarded catch-all. Suppression: `// malda-lint-disable-next-line ML0xxx`, trailing
  `// malda-lint-disable-line ML0xxx`, `// malda-lint-disable-file ML0xxx` (no code = all rules).
  `malda lint <file|dir>... [--json] [--stdin] [--list-rules]`: `--json` is the `check --json`
  shape plus `fileCount` / `suppressedCount`; exit 1 when any finding is a warning or an error,
  info never fails. `malda-lsp` publishes the findings (source `malda-lint`, code `ML0xxx`;
  `ML0000` is skipped because the parser errors are already published). Every row and
  paragraph of `docs/llm/malda-gotchas.md` now carries a Lint marker (`ML0xxx`, `check: …`,
  `compile: JS0xxx` or `docs-only`), enforced by `MaldaLinterTests`. Sweep of `Examples/`: no
  warning or error besides `ML0000` on the 6 examples the parser already rejects; one justified
  info (`ML0003` on the Mode B demo `prompt_tools_mode.malda`).

## I7 — JavaScript stdlib

- Take the `math` / `str` / collection names from `BuiltInRegistry` and implement in JavaScript
  everything that does not need the host.
- Extend the fuzz generator to call them.
- **Done when:** the stdlib row in the capability matrix goes from `partial` to `yes`, with an
  explicit exception list.

## I8 — Gradual inference

- Warnings only, in `LanguageService` and `check --json`: literal types, function signatures,
  `schema` / `type` fields.
- No runtime change and no compile error; the full static type system stays deferred
  ([`roadmap-trust.md`](roadmap-trust.md) § Explicitly deferred).
- **Done when:** false positives on `Examples/` are zero or explicitly suppressed.

## I9 — Repository root cleanup (dropped)

The root files that looked like leftovers are intentional:

- `program.html` / `host.html` — Desktop IDE web preview hosts; `MainWindow` resolves them from
  the repo root and `scripts/build-oss-dist.ps1` copies them to the zip root.
- `install-malda-extension.malda` — documented in `vscode-malda/README.md`.
- `malda-cheat-sheet.html` — listed in `llms.txt` and shipped in the dist zip.

## I10 — Git-index registry

- An index repository with `packages/<name>.json` (URL, versions, hashes); `malda search` and
  `malda install` read it.
- **Out of scope:** package hosting and accounts.

---

## Related documents

| Doc | Role |
|-----|------|
| [`docs/architecture.md`](architecture.md) | Engine map |
| [`docs/roadmap-trust.md`](roadmap-trust.md) | Trust plan (DT0–DT8) |
| [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) | Language constructs (L1–L6) |
| [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md) | Backend support per feature (I4, I7) |
| [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) | Source of lint rules (I6) |

---

## Revision history

| Date | Change |
|------|--------|
| 2026-10-15 | Initial plan (I1–I10); I9 dropped after checking root file references; I2 landed (nightly fuzz job) |
| 2026-10-15 | I1 landed: `Program.cs` split into `Program.<Area>.cs` partials |
| 2026-10-15 | I5 (Interpreter): `Interpreter.cs` split into `Interpreter.<Area>.cs` partials; exception types moved to `RuntimeException.cs` |
| 2026-10-15 | I5 (Parser): `Parser.cs` split into `Parser.<Area>.cs` partials; `ParseException` moved to `ParseException.cs`; spec drift guard watches `Parser*.cs` |
| 2026-10-15 | I5 (LanguageService): `LanguageService.cs` split into `LanguageService.<Area>.cs` partials; `DecoratorInfo` moved to `DecoratorInfo.cs`; remaining large files listed under I5 |
| 2026-10-15 | I5 (Conversation): `Conversation.cs` split into `Conversation.<Area>.cs` partials; the built-in tool switch is split into `ExecuteFileTool` / `ExecuteWorkspaceTool` / `ExecuteGitTool` / `ExecuteMaldaTool` / `ExecutePlanTool` |
| 2026-10-15 | I5 (GraphMemory): `GraphMemory.cs` split into `GraphMemory.<Area>.cs` partials |
| 2026-10-15 | I5 (HttpServer): `HttpServer.cs` split into `HttpServer.<Area>.cs` partials; `CachedFile` moved to `CachedFile.cs` |
| 2026-10-15 | I5 (WebRuntime): one file per request/response/auth context type plus `WebMiddleware.cs` and `WebRuntimeException.cs`; `WebRuntimeHelpers` split into `WebRuntime.<Area>.cs` partials |
| 2026-10-15 | I5 (RestServer): `RestServer.cs` split into `RestServer.<Area>.cs` partials |
| 2026-10-15 | I5 (BuiltInTools): `BuiltInTools.cs` split into `BuiltInTools.<Area>.cs` partials (Workspace, Git, Malda, Plan; file tools stay in the main file) |
| 2026-10-15 | I4 landed: `JsCapabilityChecker` pre-check (`JS0001`–`JS0013`) before JavaScript emit; matrix guard requires a diagnostic and fixture per JavaScript `no` row |
| 2026-10-15 | I3 landed: `malda fmt` (`--check`, `--stdin`) on the lossless `SourceTriviaScanner`; LSP and Desktop IDE formatting use the same `MaldaFormatter` |
| 2026-10-15 | I6 landed: `malda lint` (`ML0000`–`ML0013`, suppression comments, `--json`), findings in `malda-lsp`; every gotcha in `docs/llm/malda-gotchas.md` is linked to a rule or marked docs-only |
| 2026-10-15 | I5 (Desktop IDE): `MainWindow.xaml.cs` split into `MainWindow.<Area>.cs` partials (Theme, ToolCalls, Prompts, Examples, Panels, Menus; web preview moved to `WebUi`) and `NativeMethods.cs`; `MainWindow.Editor.cs` split into Completion, SyntaxPanel, Diagnostics and Navigation; `MainWindow.Run.cs` web preview moved to `MainWindow.Preview.cs` |
