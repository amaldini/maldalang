# AGENTS.md — working on MALDA (OSS)

This file is the entry map for humans and coding agents. Read it before large edits.

## What this repo is

Open-source **MALDA core**: language runtime, compiler/transpiler, Desktop + Web IDEs, LSP, examples, templates, reference manual.

**Not in this repo:** private product apps and vertical domain packs kept outside the OSS
core. Packaging for the open-source CLI is in-repo
(`build_malda_distribution.bat` / `scripts/build-oss-dist.ps1`).

## Two LLM entrypoints

| Goal | Load first |
|------|------------|
| Edit the **engine** (C#, compiler, IDE apps) | This file + `docs/architecture.md` |
| **Write / review `.malda` programs** | `docs/llm/` (syntax, gotchas, grammar, few-shots, built-in table) |

Do not mix them: repo rules are not a substitute for the language pack.

## Build and run (smoke)

```bash
dotnet build MaldaLang.sln
dotnet run --project MaldaLang -- check Examples/Basics/hello_world.malda --json
dotnet run --project MaldaLang -- Examples/Basics/hello_world.malda
```

Stable CLI output (preferred over `dotnet run -e` on Windows):

```bash
dotnet build MaldaLang.Compiler -o artifacts/malda-cli
artifacts/malda-cli/malda.exe Examples/Basics/hello_world.malda
```

Build `MaldaLang.Compiler`, not `MaldaLang`: the CLI has no reference to the compiler (it loads
`MaldaLang.Compiler.dll` from beside `malda.exe`), so `dotnet build MaldaLang -o …` leaves a
stale compiler DLL there and `malda compile` silently uses the old transpiler. Building the
compiler project also builds and copies the CLI.

Produce a self-contained executable (default mode is Interpreter — that is expected, not a
fallback). Use `--mode transpile` for typed publish:

```bash
artifacts/malda-cli/malda.exe compile prog.malda -o dist/prog.exe
```

The named `-o` exe is the shippable artifact. `MaldaLang.Executable.exe` / `.pdb` beside it
are publish scaffolding and can be ignored or deleted when shipping only the named output.
On transpile failure the CLI prints full paths to `build_errors.txt` and `GeneratedProgram.cs`
next to `-o`; a successful compile removes a stale `build_errors.txt` from that folder.
Force English `dotnet` diagnostics via `DOTNET_CLI_UI_LANGUAGE=en` (the compiler sets this).

## Tests — do not run the full suite

The full suite is too slow. Use filtered tests for the area you touch:

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~RelevantTests"
```

Backend parity fuzzing: `BackendDifferentialFuzzTests` generates seeded programs and requires the
interpreter, C# transpile and JS transpile to print the same output (3 seeds by default, ~20 s).
After touching an emitter or the JS runtime, explore more seeds with
`MALDA_FUZZ_SEEDS=100-160 dotnet test MaldaLang.Tests --filter "FullyQualifiedName~BackendDifferentialFuzzTests"`;
a failure leaves the program and the three outputs under `%TEMP%/malda_fuzz/seed<N>/`.

UI protocol fuzzing: `UiPatchFuzzTests` diffs seeded `UiNode` tree pairs with `UiDiffEngine` and
requires the patches, applied by `MaldaUiCore` in `malda-ui-client.js` (Node) and by the C#
`UiPatchApplier`, to rebuild the new tree; `MaldaUiDom` applying them to a fake DOM must equal a
fresh render. After touching the diff engine or the client's patch
code, run it with `MALDA_UI_FUZZ_SEEDS=1-200`; failures land in `%TEMP%/malda_ui_fuzz/seed<N>/`.

CI smoke filter (see `.github/workflows/ci.yml`): `BuiltInRegistryTests`, `CompilerPackDecouplingGuardTests`, `OptionalPackTranspileEmitTests`, `LicenseHeaderGuardTests`, `ReleaseVersionGuardTests`, `ReferenceManual`, `BackendCapabilityMatrixGuardTests`, `ShipContractGuardTests`, `TranspileSmokeTests`, `InterpretTranspilePairTests`, `InterpretJsPairTests`, `WorkflowTranspilerParityTests`, `HttpTraceParityTests`, `JobTraceParityTests`, `Tier0CsharpShipSmokeTests`, `BackendDifferentialFuzzTests`, `UiPatchFuzzTests`, `BuiltInSurfaceGuardTests`, `ExamplesParseGuardTests`, `ExamplesStyleGuardTests` (Windows job).

## IDE roles (not interchangeable)

| Tool | Role |
|------|------|
| `MaldaLang.DesktopIDE` | **Reference** full Windows IDE |
| `MaldaLang.IDE` | Browser **learning playground** (Monaco) — not Desktop parity |
| `vscode-malda` + `MaldaLang.LanguageServer` | Cross-platform editor integration (`malda-lsp`). Interpret debug is `malda debug-adapter`, not the language server |

Web IDE improvements (Monaco UX, examples browser, diagnostics presentation) are good first contributions. Do not assume Desktop-only features exist on Web (virtual `@malda-section` tabs, MCP UI, local model browser, UIHost preview).

## Architecture map (where to edit)

| Concern | Primary paths |
|---------|----------------|
| Lexer / tokens | `MaldaLang/Lexer.cs`, `MaldaLang/TokenType.cs` |
| Parser / AST | `MaldaLang/Parser/` |
| Interpreter | `MaldaLang/Interpreter/Interpreter.cs` (+ partials) |
| Interpret debug core | `MaldaLang/Interpreter/Debug/DebugSession.cs` — pause gate / 1-based lines; IDE hooks wrap this |
| DAP (interpret) | `MaldaLang/DebugAdapter/` — `malda debug-adapter` on stdio. Do not mix DAP into `malda-lsp` |
| Built-in functions | `MaldaLang/BuiltIns/BuiltInFunctions.cs` (dispatch) + `BuiltInFunctions.<Area>.cs` partials (`Math`, `Strings`, `Files`, `Ui`, `Workflows`, …), `BuiltInRegistry.cs` |
| C# transpile | `MaldaLang.Compiler/CSharpTranspiler.cs` (+ `CSharpTranspiler.<Area>.cs` partials: `Statements`, `Expressions`, `Calls`, `BuiltIns`, `Prompts`, `RuntimeHelpers.<Area>`, …), `Compiler.cs` (+ `Compiler.<Area>.cs`: `ProjectFiles`, `DotnetBuild`, `JavaScript`, `Transpiled`, `UiHost`) |
| JS / PWA transpile | `MaldaLang.Compiler/JsTranspiler.cs` (+ `JsTranspiler.<Area>.cs` partials: `Expressions`, `Calls`, `Match`, `Scopes`, `Actors`, `Shaders`, `SourceMap`), `GlslTranspiler.cs` (`@shader()` → GLSL) |
| Server-driven UI (`ui.*` / UIHost) | `MaldaLang/Runtime/UI/`, `MaldaLang.UIHost/` — [`docs/ui-framework.md`](docs/ui-framework.md) |
| Optional pack emit (string-only) | `MaldaLang.Compiler/OptionalPack/` |
| CLI | `MaldaLang/Program.cs` (entry + dispatch) + `Program.<Area>.cs` partials (`Compile`, `Run`, `Repl`, `Workflow`, `Assistant`, `Help`, …) |
| Language intelligence (IDE/LSP shared) | `MaldaLang/IDE/LanguageService.cs` (+ `LanguageService.<Area>.cs` partials: `Completions`, `Hover`, `SignatureHelp`, `Decorators`, `AutoFix`, …) |
| LSP host | `MaldaLang.LanguageServer/` (`malda-lsp`; not the debugger) |
| Web IDE | `MaldaLang.IDE/` |
| Desktop IDE | `MaldaLang.DesktopIDE/` |
| Examples | `Examples/` |
| Language reference (edit HTML chapters) | `ReferenceManual/*.html` (not generated PDF HTML). Italian translation: `ReferenceManual/it/` (English remains canonical) |
| Spec / onboarding docs | `docs/spec/`, `docs/start-here.md`, `docs/architecture.md` |
| Licensing | `LICENSE-MIT`, `LICENSE-APACHE`, `LICENSE-RUNTIME-EXCEPTION`, `TRADEMARK.md`, `THIRD-PARTY-NOTICES.md` |

Longer overview: [`docs/architecture.md`](docs/architecture.md).

## Hard rules for agents

1. **Never run the full test suite** unless the user explicitly asks.
2. **Do not hand-edit generated artifacts** such as `GeneratedProgram.cs` or generated `.js` from `.malda` — change the MALDA source and regenerate.
3. Use the keyword **`function`**. `fn` and `def` are syntax errors.
4. **Prompt declarations** use name-only parameters (no `name: string` style). `-> ReturnType` on prompts is informational only.
5. When adding a **built-in**, register it in interpreter + transpiler surfaces (see `docs/architecture.md` § Built-ins).
6. Keep PRs focused. Prefer filtered tests that match the change.
7. `docs/planning/` is historical / roadmap notes — not the source of truth for current behavior. Prefer code, `docs/spec/`, and `ReferenceManual/`.
8. **Licensing is dual `MIT OR Apache-2.0`.** New C# files carry `// SPDX-License-Identifier: MIT OR Apache-2.0` under the copyright line. Never write "All rights reserved", never add a file named `NOTICE`, and never offer only one of the two licences — `LicenseHeaderGuardTests` fails on all three. If you change what the transpilers emit into user programs, update the "Runtime Material" list in `LICENSE-RUNTIME-EXCEPTION`.
9. **Release version is one number.** When cutting a release, set the same `<Version>` in `MaldaLang/MaldaLang.csproj` and `MaldaLang.DesktopIDE/MaldaLang.DesktopIDE.csproj`, add `docs/releases/vX.Y.Z.md`, then tag `vX.Y.Z`. `ReleaseVersionGuardTests` and the Release workflow fail on drift; `build-oss-dist.ps1` reads the CLI csproj for zip names.

## Built-in surface (soft freeze)

The stdlib is large (~300+ names). Prefer extending existing namespaces (`math` / `str` /
`io` / web helpers) over adding new top-level globals. Flat aliases are deprecated — do not
add new ones. Every new built-in must complete the checklist below and land with filtered
tests; drive-by “nice to have” builtins are out of scope for focused PRs.

## New built-in checklist (summary)

1. Implement in the matching `MaldaLang/BuiltIns/BuiltInFunctions.<Area>.cs` partial (or related BuiltIns type)
2. Check the argument count with `BuiltInArity.Require("name", args, min, max, "a, b?")` — one
   phrasing for every built-in, and the generator in step 6 reads these call sites
3. Add one arm to `DispatchBuiltIn` in `BuiltInFunctions.cs`. Add an arm to `CallBuiltInAsync`
   only when the implementation awaits — every other name falls through to `DispatchBuiltIn`
4. Register the name in `BuiltInRegistry.GetDescriptor`, then run
   `scripts/generate-core-builtin-inventory.ps1`. The registry is what interpreter `IsBuiltIn`
   and transpiler `IsBuiltInFunction` read, and C# transpile emits a dispatch call for it by
   default — add a `TranspileBuiltInFunction` case only for a special emit.
   `BuiltInSurfaceGuardTests` fails when the registry and the dispatch table drift
5. Name it somewhere in `ReferenceManual/*.html` (the coverage guard fails otherwise)
6. Regenerate the agent lookup table: `pwsh scripts/sync-llm-builtins-tsv.ps1`
7. Rebuild and smoke-test interpreted + transpiled paths
8. Update `docs/llm/` gotchas/syntax when the built-in has agent-facing footguns; bump the
   pack `Applies to` version when cutting the release that ships it

## Debugging transpile failures

Full guide: [`docs/debugging-transpile.md`](docs/debugging-transpile.md).

1. Check `build_errors.txt` if present (gitignored locally)
2. Prefer `.malda(line)` from the CLI / Roslyn `#line` mapping; otherwise inspect `GeneratedProgram.cs`
3. Fix the transpiler method — do not patch generated output as the fix

## Chapter numbering

After changing `ReferenceManual/chapters.json` order, run:

```bash
pwsh scripts/sync-reference-manual-chapter-numbers.ps1
```

That script must build ←/→ via Unicode codepoints (`[char]0x2190` / `0x2192`) so Windows PowerShell 5 does not mojibake footers.

## Reference Manual content guards

See [`ReferenceManual/README-content-guards.md`](ReferenceManual/README-content-guards.md). Tests keep the manual aligned with the code: reserved words vs `Lexer.Keywords`, built-in coverage vs `BuiltInRegistry`, internal links, unique section numbers, the `navigation.js` fallback vs `chapters.json`, and execution of every snippet marked `data-run="true"`.

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~ReferenceManual"
```

Adding a built-in without naming it anywhere in `ReferenceManual/*.html` fails the coverage guard.

## Reference Manual presentation

See [`ReferenceManual/README-print.md`](ReferenceManual/README-print.md). Short version:

- After adding a chapter, run `pwsh scripts/sync-reference-manual-assets.ps1` so it links the shared CSS/JS. `print.css` must load **after** `styles.css`.
- Code highlighting is `ReferenceManual/malda-highlight.js` and `vscode-malda/syntaxes/malda.tmLanguage.json`; keyword lists mirror `MaldaLang/Lexer.cs`. Update them together.
- Paper edition: `pwsh scripts/build-reference-manual-book.ps1` (add `-Locale it` for the Italian HTML tree) → `artifacts/reference-manual/`, then print to PDF from Chrome/Edge.

## Useful links

- [`README.md`](README.md) — product overview
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — contributor workflow
- [`docs/start-here.md`](docs/start-here.md) — learning paths
- [`docs/roadmap-p0-maturity.md`](docs/roadmap-p0-maturity.md) — P0 maturity roadmap (complete; next = post-Final / deferred)
- [`docs/roadmap-language-constructs.md`](docs/roadmap-language-constructs.md) — next language constructs (schema/sum types, Mode C, budget, WF determinism)
- [`docs/roadmap-trust.md`](docs/roadmap-trust.md) — trust plan (strict compile, smoke, gotchas; toolchain 1.0.0 landed)
- [`docs/roadmap-games.md`](docs/roadmap-games.md) — browser games kit (`game.*` / `three.*`, JS-only; G0–G22 landed)
- [`docs/roadmap-neural.md`](docs/roadmap-neural.md) — neural-nets kit (`math.matmul`, `nn.*` activations and dense forward/backward, spatial host layers, AI_Theory, `OnnxModel`; N0–N4 and N6–N15 landed, N5 partial)
- [`docs/roadmap-improvements.md`](docs/roadmap-improvements.md) — engineering improvements (CLI split, nightly fuzz, `malda fmt` / `lint`, JS capability pre-check, file splits; I1–I2 landed)
- [`docs/roadmap-ui-runtime.md`](docs/roadmap-ui-runtime.md) — web UI runtime (protocol 1.1, one host, event bridge, DOM patching, secure-by-default host; U0–U7 landed)
- [`docs/roadmap-js-platform.md`](docs/roadmap-js-platform.md) — JavaScript platform for large browser apps (JS interop, ESM output, typed emit/buffers, workers, graphics, client-side `ui.*`, storage/files; J0–J5 and J7 landed, J6 and J8 open)
- [`llms.txt`](llms.txt) — compact doc index for LLM tools
