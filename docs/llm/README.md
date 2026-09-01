# LLM pack: writing MALDA programs

*Applies to: MALDA 1.0.13*

Use this folder when an LLM should **author or review `.malda` source**, not when editing the C# compiler/runtime.

| File | Use |
|------|-----|
| [`malda-syntax.md`](malda-syntax.md) | Idioms, do/don't, preferred style |
| [`malda-gotchas.md`](malda-gotchas.md) | Mistakes that run without error and produce wrong output |
| [`malda-grammar.md`](malda-grammar.md) | Plain-text BNF (parser-aligned) |
| [`malda-builtins-min.md`](malda-builtins-min.md) | High-frequency builtins, and what top-level objects exist |
| [`malda-builtins.tsv`](malda-builtins.tsv) | Every built-in, one per line, grep-able. Generated from the engine (notes + returns hand-written) |
| [`few-shot/`](few-shot/) | Tiny runnable snippets |

## Suggested load order (token budget)

1. `malda-syntax.md` (always — includes array mutation and `$"..."` interpolation)
2. `malda-gotchas.md` (always — it is short, and it covers the failures nothing else reports)
3. 2–4 files from `few-shot/` matching the task
4. `malda-grammar.md` if generating unfamiliar constructs
5. `malda-builtins-min.md` for stdlib shape (I/O, math, arrays, AnsiConsole); grep `malda-builtins.tsv` for one specific name
6. Deeper: `Examples/`, `ReferenceManual/`, `docs/spec/malda-language-1.0.md`

## Ground truth

- Parser (repo only): `MaldaLang/Parser/Parser.cs` + `MaldaLang/Lexer.cs`
- Human reference: `ReferenceManual/`
- Agent map: root `AGENTS.md` (repo engine map, or the distribution copy shipped in the zip)

## Smoke-check generated code

Diagnose **without executing** first (parse + IDE diagnostics, including schema names
and interpolation warnings). `--json` is the generate → diagnose → patch loop:

```bash
malda check path/to/program.malda --json
# Windows zip root: .\malda.bat check path\to\program.malda --json
```

From a distribution unzip (or with `malda` / `malda.bat` on your `PATH`), then run:

```bash
malda path/to/program.malda
# Windows zip root: .\malda.bat path\to\program.malda
```

From a full source checkout:

```bash
dotnet run --project MaldaLang -- check path/to/program.malda --json
dotnet run --project MaldaLang -- path/to/program.malda
```

### Produce an executable

```bash
malda compile prog.malda -o dist/prog.exe
# default Mode: Interpreter is expected (self-contained), not a failed transpile
# --mode transpile for typed publish
malda compile prog.malda -o dist/prog.exe --mode transpile
```

The named `-o` path is the shippable artifact. The output directory may also contain
`MaldaLang.Executable.exe` (byte-identical scaffolding from the publish step) and
`MaldaLang.Executable.pdb` — safe to ignore or delete when shipping only the named exe.
On failure, `build_errors.txt` and `GeneratedProgram.cs` land next to `-o` (the CLI prints
their full paths). A successful compile removes a stale `build_errors.txt` from that folder.
See [`docs/debugging-transpile.md`](../debugging-transpile.md) for `#line` mapping and what to open first.

**Interpreter ≠ transpile.** `malda prog.malda` and `malda compile --mode transpile` are
different backends. Smoke both when the deliverable is an `.exe`.

### Programs that read input or use randomness

An agent cannot sit at a prompt, so interactive and random programs look unverifiable. They
are not. Two facts turn "I wrote it and it looks right" into "I ran it and here is the
output":

**Seed the generator.** `math.seed(n)` pins `random`, `randomInt`, `randomFloat` and `randn`
for the whole run, so every branch becomes reachable on purpose:

```malda
math.seed(7);
var secret = math.randomInt(1, 100);   // always 39
```

A program you ship should usually stay unseeded. For a test run, prepend a seed into a
temporary copy and leave the source alone:

```bash
{ echo "math.seed(7);"; cat guess_number.malda; } > /tmp/seeded.malda
printf '50\n25\n39\n' | malda /tmp/seeded.malda
```

**Pipe a scripted transcript into `input()`.** Each line of stdin answers one `input()` call.
At end of input, `io.input` returns `""` forever (not `null`) — every input loop needs an
empty-string / quit path (`break`), or a short transcript will hang:

```bash
printf '50\n25\n39\n' | malda guess_number.malda
# Windows zip root: printf '50\n25\n39\n' | ./malda.bat guess_number.malda
```

Do **not** use `"50","25","39" | .\malda.bat …` under Windows PowerShell 5.1 — it pipes a
UTF-8 BOM on the first line, so the program sees `﻿50` and `toIntOrNull` rejects it. Prefer
the `printf` form above (Git Bash), or write the transcript to a file and redirect:

```powershell
Set-Content -Path guess.txt -Value @("50","25","39") -Encoding ascii
Get-Content guess.txt | .\malda.bat guess_number.malda
```

Put the two together — **seed, pipe a transcript, assert on the output** — and you can prove
the win path, each wrong-guess branch, and the input-validation guard, rather than asserting
they work.

One caveat while you do this: when stdout is not a terminal, Spectre.Console strips colour
and MALDA disables Unicode capabilities, so piped output has no escape codes and panel
borders such as `"rounded"` fall back to square corners. Under a non-UTF-8 console codepage
(common in Windows Git Bash) box-drawing can also arrive as mojibake. That is expected, not
a bug to chase — check those visuals in an interactive UTF-8 terminal.
