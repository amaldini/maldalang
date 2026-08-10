# MALDA gotchas: the mistakes the interpreter will not catch

*Applies to: MALDA 0.1.47*

`malda-syntax.md` lists the JS-isms an agent might guess — `const`, `console.log`, `def`.
Those are cheap: the parser rejects them immediately and you fix them on the next run.

This file is for the expensive ones. Every entry below **runs without error** and produces
the wrong output, so there is no feedback loop to self-correct from. Read it before you
claim a program works.

## Silent failures

| You write | What actually happens | Write this instead |
|-----------|----------------------|--------------------|
| `print("n is {n}")` | Prints the literal `n is {n}`. A plain string does **not** interpolate. | `io.print($"n is {n}")` or `io.print("n is " + string(n))` |
| `var raw = io.input("? ");` in a loop | At end of input this returns `""` — **not** `null`, and it does not exit. Every later call returns `""` too, so a loop that only advances on valid input never terminates. | Treat empty as end/quit: `if (str.trim(raw) == "") { break; }` |
| `AnsiConsole.markup("line one")` | No trailing newline, so consecutive calls smear onto one line. | `AnsiConsole.markupLine("line one")` |
| `parseJson(text)` | A schema-validating parser for LLM output, not a JSON reader. Different arguments, different job. | `parseJSON(text)` — capital JSON |
| `validate("Name", value)` expecting a throw | Mismatch returns `{ ok: false, error }` — it does **not** throw. Unknown schema name does throw. | Check `checked.ok`; use `parseJson(text, "Name")` when you want throw-on-mismatch from JSON text |
| `randomInt(1, 100)` | Returns `100` too. Both endpoints are included, unlike half-open ranges elsewhere. | Fine as written — just do not subtract 1 |
| `println(x)` | `Undefined variable 'println'`. It does not exist. | `print(x)` |
| `var n: int = "abc";`, `n = "a"`, `var p: Person = 1`, or `f(s)` when `f(x: int)` and `s: string` | Runs at runtime. The language server emits a **Warning** for mismatches on literals, `new ClassName()`, and identifiers with known hints (Tier 0 names, declared class/schema names, host classes; variables, fields, assignment, call arguments, `return` vs `-> T`). With `--strict-types` those mismatches are **Errors**; unknown hint names are also errors. Operators / call results are not inferred. Nothing enforces hints at runtime. | Fix the value, validate explicitly, or use `toIntOrNull` |
| `str.repeat("-", n / 2)` when `n` is odd | `Error: repeat() expects (string, integer)`. `/` always yields a float; a fractional float is **not** coerced at integer sinks. Whole-valued floats from `math.floor` / `round` / `ceil` (and exact `n / 2`) are accepted. | `str.repeat("-", int(n / 2))` or `math.floor(n / 2)` |
| `str.trim(io.getEnv("MISSING"))` | `getEnv` returns **`null`** when the variable is unset (unlike `io.input`, which returns `""` at EOF). `trim` then errors. | `io.getEnvOr("MISSING")` (or `io.getEnvOr("MISSING", "")`), or `str.trimText(io.getEnv("MISSING"))` |
| `csrfField(secret)` under `enableCsrf(secret)` | CSRF requires **cookie value == form `_csrf`**. `csrfField(secret)` generates a *new* token; if the CSRF cookie was already set to a different token, mutating requests 403. | On the GET that renders the form, reuse `req.cookie("csrf_token")` when valid, or generate once and set both the cookie and the field to that same token |
| `req.session.getFlash("err")` twice | Flash values are **one-shot**. The first `getFlash` / `getFlashes` consumes them; a second read in the same request returns empty. | Read flash once when rendering the page, or keep a local variable |
| `new RestServer(8080)` then also `HttpServer` on 8080 | Two listeners fight for the port. For fullstack, use `new RestServer()` (deferred port) and `http.mount(api)`. | One port owner: `HttpServer` + `mount` |
| `ui.dispatchEvent(...)` then `ui.render(...)` without `pullEvent` / state update | The event sits in the session queue. The next tree is built from unchanged state, so the UI looks “stuck”. | `pullEvent` → update `ui.setState` / locals → rebuild tree → `ui.render` |
| Mixing `@PAGE` HTML strings with `ui.*` trees as if they were the same model | Both run, but they are different UIs: `@PAGE` returns HTML; `ui.mount` / `ui.render` expect node trees from `ui.button(props, …)`, not `"<button>…"`. | Pick one model per surface; see `ReferenceManual/16-web-ui-hub.html` |
| `enqueueJob` expecting durable workflow semantics | Jobs are a **lightweight SQLite queue** (`./.malda/jobs.db`), not `workflow` / `step` / compensate. | Use `workflow { }` for durable steps; use jobs for fire-and-forget workers |
| `prompt p() -> MySchema { tools: [...]; … }` expecting structured `response_format` | For v1, **tools and `response_format` are mutually exclusive**. If the prompt body lists tools, the schema is **not** sent to the LLM. | Omit tools for typed structured output, or validate the free-form reply yourself |
| Treating `-> Type` as compile-time typing | Hints are not static types. On **`await`**, the runtime validates JSON against the resolved schema (and may send OpenAI `response_format`). Without `await`, you only build a `PromptInstance`. | Prefer `schema Name { … }` + `await prompt(...) -> Name`; see `Examples/Prompts/schema_prompt_structured.malda` |

## Half-truths

**The interpreter and `malda compile --mode transpile` are different backends.** A program that
runs under the interpreter is not automatically a program that compiles. Prefer smoke-testing
both when you need a shippable `.exe`. Escape sequences inside `$"..."` (for example `\n`,
`\r`, `\t`) are valid in both; if transpile fails with a C# string error (`CS1039`), inspect
`GeneratedProgram.cs` next to `-o` — and `build_errors.txt` in that same folder. Unknown
escapes (e.g. `\x`, or a single `\d` meant for regex) are a **lexer error** — write `\\d` in
a string when you need a literal backslash for a regex. Product-level support (agents, HTTP
servers, workflows vs browser DOM) is summarized in
[`docs/spec/backend-capability-matrix.md`](../spec/backend-capability-matrix.md).

**Colour and Unicode borders disappear when you pipe output.** Spectre.Console strips ANSI
escapes when stdout is not a terminal, and MALDA also turns off Unicode capabilities for
redirected / non-terminal output. So `malda prog.malda > out.txt` or a piped stdin test
produces plain text — and `"rounded"` / `"heavy"` panel borders fall back to square corners
(square box-drawing) even though those styles work in an interactive terminal. Under a
non-UTF-8 console codepage (common default in Windows Git Bash) box-drawing can also arrive
as mojibake — same non-bug class, different symptom. Do not debug this; it is correct
behaviour for a non-TTY / mismatched encoding.

**`AnsiConsole.panel` border styles are three values, not Spectre's full set.** Only
`"rounded"`, `"double"` and `"heavy"` are recognised. Anything else — including `"square"`,
`"ascii"`, `"none"`, or a typo — silently becomes square. There is no error.

**`AnsiConsole.panel` renders unparseable markup literally.** The body and title are parsed
as markup, but content that is not valid markup — arbitrary JSON, code, a stack trace with
brackets — falls back to literal text instead of raising. So a malformed tag shows up as
text rather than as an error. The same markup parsing applies inside `AnsiConsole.table`
cells and headers.

**`math.floor` / `round` / `ceil` still return floats.** Integer sinks (counts, indexes,
seeds, `str.repeat`, …) coerce whole-valued floats automatically, so
`str.repeat("-", math.floor(n / 2))` works. The value is still tagged float: `print` shows
`1` not `1.0`, and a fractional float such as `2.7` is still rejected. Use `int(...)` when
you want an integer value, not only an integer-accepting call.

**Flat built-in names are deprecated aliases.** `sqrt(16)` and `Math.sqrt(16)` both run, but
the language server reports both as deprecated. Prefer `math.sqrt(16)`. See the namespace
rule in [`malda-syntax.md`](malda-syntax.md).

**Typed prompts send `response_format` only to OpenAI-compatible chat APIs.** Llama.cpp
clients accept the parameter and ignore it; validation + repair retries still run after the
reply. If a backend rejects `response_format`, the host retries once without it.

## Before you say it works

Look up any built-in you are unsure about in [`malda-builtins.tsv`](malda-builtins.tsv) —
one lookup, five columns (`name`, preferred call, arguments, notes, returns). Notes hold
footguns; **returns** is where null-vs-empty and typed results live. The preferred-call
column uses `<array>.append` for member-style methods — that is a receiver method, not a
free-function namespace:

```bash
awk -F'\t' '$1 == "randomInt"' docs/llm/malda-builtins.tsv
```

(`grep -P` is not reliable in Windows Git Bash; prefer `awk` as above, or
`grep -E '^randomInt	'` with a literal tab.)

Then run the program. [`README.md`](README.md) describes how to smoke-test programs that
read input or use randomness, which are exactly the ones that look untestable.
