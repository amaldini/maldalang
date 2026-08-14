# Debugging transpile failures

When `malda compile --mode transpile` (or `compile` targeting a native `.exe`) fails after MALDA has already emitted C#, the CLI writes diagnostics next to `-o` and prints their full paths.

## Artifacts next to `-o`

| File | Role |
|------|------|
| `build_errors.txt` | Full `dotnet` / Roslyn output (gitignored locally) |
| `GeneratedProgram.cs` | C# the transpiler produced for that compile |

A successful compile removes a stale `build_errors.txt` from that folder.

## How errors map back to `.malda`

The C# emitter inserts `#line N "path.malda"` so many Roslyn diagnostics already point at the **MALDA source line**. Prefer that location when the CLI message shows `file.malda(line,...)`.

Some AST nodes are unmapped (`#line default`). In that case Roslyn points at `GeneratedProgram.cs` / `Program.cs` — open the generated file at the reported line, then find the corresponding MALDA construct and fix the **transpiler**, not the generated file by hand.

## Suggested steps

1. Read the CLI summary (first error + path footer).
2. If the diagnostic names a `.malda` file and line, open that source line.
3. Otherwise open `build_errors.txt` and `GeneratedProgram.cs` next to `-o`.
4. Fix emit logic in `MaldaLang.Compiler` (or the MALDA program), then recompile. Do not hand-edit `GeneratedProgram.cs` as the permanent fix.

## JavaScript / browser backend

`malda compile --mode javascript` already emits VLQ `.map` files and a `//# sourceMappingURL=` comment. Browser DevTools can map stack frames to `.malda`. That path is separate from the C# `#line` story above.

Interpret-mode source-level debug (pause / step while running `.malda` in the interpreter) is a separate product: [`docs/debugging-interpret.md`](debugging-interpret.md) (`malda debug-adapter`). The implementation plan is [`docs/roadmap-interpret-debug.md`](roadmap-interpret-debug.md).

## Related

- Agent checklist: [`AGENTS.md`](../AGENTS.md) — Debugging transpile failures
- LLM pack notes: [`docs/llm/README.md`](llm/README.md), [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md)
- JS backend: [`docs/javascript-backend.md`](javascript-backend.md)
