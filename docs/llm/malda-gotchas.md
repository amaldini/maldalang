# MALDA gotchas: the mistakes the interpreter will not catch

*Applies to: MALDA 1.0.14*

`malda-syntax.md` lists the JS-isms an agent might guess — `const`, `console.log`, `def`.
Those are cheap: the parser rejects them immediately and you fix them on the next run.

This file is for the expensive ones. Every entry below **runs without error** and produces
the wrong output, so there is no feedback loop to self-correct from. Read it before you
claim a program works.

## Silent failures

| You write | What actually happens | Write this instead |
|-----------|----------------------|--------------------|
| `print("n is {n}")` | Prints the literal `n is {n}`. A plain string does **not** interpolate. IDE/LSP **malda-interp** Warning (prompt bodies still use `{name}` templates). | `io.print($"n is {n}")` or `io.print("n is " + string(n))` |
| `var raw = io.input("? ");` in a loop | At end of input this returns `""` — **not** `null`, and it does not exit. Every later call returns `""` too, so a loop that only advances on valid input never terminates. | Treat empty as end/quit: `if (str.trim(raw) == "") { break; }` |
| `AnsiConsole.markup("line one")` | No trailing newline, so consecutive calls smear onto one line. | `AnsiConsole.markupLine("line one")` |
| `parseJson(text)` | A schema-validating parser for LLM output, not a JSON reader. One-arg calls now error and name `parseJSON`; two-arg `parseJSON` names `parseJson`. | `parseJSON(text)` — capital JSON |
| `validate("Name", value)` expecting a throw | Mismatch returns `{ ok: false, error }` — it does **not** throw. Unknown schema name does throw. | Check `checked.ok`; use `parseJson(text, "Name")` when you want throw-on-mismatch from JSON text |
| Tool / `think()` JSON used without `validate` | Undecorated helpers and `think()` payloads are still unchecked. `@MCPTool` / `@Tool` **with a third-argument schema** are host-validated before the body (`callTool` / `tools/call` / `execute` / agent invoke). Omitting the schema still advertises all-string properties and does **not** check args. | Attach `"AddArgs"` (or a JSON schema string). `server.callTool("add", args)` returns `{ ok, data\|error }`. Other `think()` JSON: `schema` + `validate(...)` before I/O — `Examples/Agents/agent_governance_golden.malda` |
| `@effects("io")` plus `io.readFile(path)` on a tool that takes `path: string` | The decorator only **names** the effect. The model can still invent `/etc/passwd`. The call succeeds. | Host-mint `cap.fileRead(root)`, take a relative path, `cap.confine` then `cap.read`. How-to: `ReferenceManual/12-input-output.html` §12.6. Scaffold: `malda new agent` |
| `randomInt(1, 100)` | Returns `100` too. Both endpoints are included, unlike half-open ranges elsewhere. | Fine as written — just do not subtract 1 |
| `println(x)` | `Undefined variable 'println'`. It does not exist. | `print(x)` |
| `var n: int = "abc";`, `n = "a"`, `var p: Person = 1`, `f(s)` when `f(x: int)` and `s: string`, or `var n: int = make()` when `make() -> string` | Runs at runtime under `malda run`. IDE/LSP default reports mismatches as **Errors**. `malda compile --mode transpile` (and `publish`) refuses emit on those Errors; escape `--lenient-types`. CLI `run --strict-types` also enables match/`@pure`/bounds/const. Nothing enforces hints at interpret-time unless you pass `--strict-types`. | Fix the value, validate explicitly, or use `toIntOrNull` |
| `schema A { b: NotAType; }` or `type T = Ctor(x: NotAType)` | IDE reports `malda-schema` on the declaration; resolving/validating also throws — unknown field / payload types are not silently treated as `string`. | Use a JSON primitive (`string`, `int`, …), a declared schema name (`B` / `B[]`), or a declared sum type |
| `str.repeat("-", n / 2)` when `n` is odd | `Error: repeat() expects (string, integer)`. `/` always yields a float; a fractional float is **not** coerced at integer sinks. Whole-valued floats from `math.floor` / `round` / `ceil` (and exact `n / 2`) are accepted. | `str.repeat("-", int(n / 2))` or `math.floor(n / 2)` |
| `str.trim(io.getEnv("MISSING"))` | `getEnv` returns **`null`** when the variable is unset (unlike `io.input`, which returns `""` at EOF). `trim` then errors. | `io.getEnvOr("MISSING")` (or `io.getEnvOr("MISSING", "")`), or `str.trimText(io.getEnv("MISSING"))` |
| `csrfField(secret)` under `enableCsrf(secret)` | CSRF requires **cookie value == form `_csrf`**. `csrfField(secret)` generates a *new* token; if the CSRF cookie was already set to a different token, mutating requests 403. | On the GET that renders the form, reuse `req.cookie("csrf_token")` when valid, or generate once and set both the cookie and the field to that same token |
| `req.session.getFlash("err")` twice | Flash values are **one-shot**. The first `getFlash` / `getFlashes` consumes them; a second read in the same request returns empty. | Read flash once when rendering the page, or keep a local variable |
| `res.header("X-Foo", "1")` after `res.json(...)` | `res.json` / `send` / `html` / `fragment` **send and close** the HTTP response immediately. Later header/cookie calls throw `response already sent`. | Set cookies and headers *before* `res.json` / `fragment`. Keep slow work *after* the send. |
| `ui.dispatchEvent(...)` then `ui.render(...)` without `pullEvent` / state update | The event sits in the session queue. The next tree is built from unchanged state, so the UI looks “stuck”. IDE **UI1001**. | `pullEvent` → update `ui.setState` / locals → rebuild tree → `ui.render` — see `Examples/Web/ui_event_loop.malda` |
| `ui.dispatchEvent(event, 1)` treating the second arg as a sequence | The second argument is **sessionId** (string) only. An integer there is ignored; the event goes to `"default"` and is not sequenced. | `ui.dispatchEvent(event, sessionId)` or `ui.dispatchEvent(event, sessionId, sequence)` |
| Mixing `@PAGE` HTML strings with `ui.*` trees as if they were the same model | Both run, but they are different UIs: `@PAGE` returns HTML; `ui.mount` / `ui.render` expect node trees from `ui.button(props, …)`, not `"<button>…"`. | Pick one model per surface; see `ReferenceManual/23-web-ui-hub.html` |
| `ui.state(id, key, null)` or `ui.state(id, key, {})` on critical keys | Get-or-create **persists** the default on miss. After TTL/LRU eviction the next call re-poisons the store. IDE **UI1003**. | Peek with `ui.getState`; use `[]` / `0` / `""` when initializing; `ui.pinState` for process-lifetime data — `Examples/Web/ui_state_lifecycle.malda` |
| `import "lib.malda"` when you only need one helper | Merges the **entire** export surface into the host scope (name clutter / collisions). | `import { justThis } from "lib.malda"` — `Examples/Modules/selective_import.malda` |
| Module with `export function` plus bare `type` / `schema` (no `export type` / `export schema`) | Once any `export` exists, unmarked types/schemas drop off the export surface (IDE + selective import). | `export type …` / `export schema …` — `Examples/Modules/export_type_schema.malda` |
| `enqueueJob` expecting durable workflow semantics | Jobs are a **lightweight SQLite queue** (`./.malda/jobs.db`), not `workflow` / `step` / compensate. | Use `workflow { }` for durable steps; use jobs for fire-and-forget workers |
| `step` inside `while` / `for` expecting N executions | Steps are memoized by **name**. After the first success, replay returns the journaled result and the body of that step does not run again. | Put the loop **inside** the step callee, or use distinct step names per iteration |
| `now()` / `sleep()` / `writeFile()` / `copyFile()` outside a workflow `step` “should be fine if my helper is pure-ish” | Outside `step`, deny-listed built-ins raise `WF1001`/`WF1002` at **runtime even when nested in a helper**. The IDE walks same-file `function` callees (bounded depth) and reports the same codes; imported/unknown callees are **WF1005 Info**, not a hard error. There is still no Temporal-style history detector — other effects are assumed safe. | Put clock, sleep, I/O, HTTP, and any other effect inside a `step` (`Examples/Workflows/determinism_helpers.malda`) |
| `memory.query(...)` hits used as if `match` / `validate` could see citations | Query returns an **array of nodes**. Provenance stays on the side until you wrap it. | `grounded.wrap(answer, citations)` or opt-in `memory.ask(...)` / `query(..., { grounded: true })` — `{ value, citations, sourced }`. Not a `Grounded` keyword. Example: `Examples/Memory/grounded_ask.malda` |
| `@MCPTool("add", "…")` without a schema (or a JSON blob as the third arg) | Every parameter is advertised as <code>"string"</code>. The host does **not** check args. Numbers and optional fields are lost unless you pass a schema. | Declare `schema AddArgs { a: int; b: int; }` and write `@MCPTool("add", "…", "AddArgs")` (or the identifier `AddArgs`). The host then rejects bad `callTool` / `tools/call` / `execute` args before the body. JSON object strings remain valid for enums/defaults. Example: `Examples/MCP/mcp_schema_tool.malda` |
| `MALDA_AGENT_CONTEXT_BUDGET_TOKENS=4000` expecting a hard abort | Env var only **trims** agent context for undeclared agents. It does not throw. | `@budget(tokens: 4000, tools: 8)` on the prompt or function — abort with a dedicated message when the bound trips |
| `prompt p() -> MySchema { tools: [...]; … }` expecting structured `response_format` | For v1 this used to omit format and appendix. Now MALDA **sends both with tools**. Some backends reject `tools` + `json_schema`; the host retries once without format (keeps tools) and remembers that backend. `tools:` is **not** two LLM calls. | Mode A: omit tools (`Examples/Prompts/schema_prompt_structured.malda`). Mode B: one call, structured-if-supported (`Examples/Prompts/prompt_tools_mode.malda`). Mode C: `gather:` + `-> Type` (`Examples/Prompts/prompt_tools_then_structured.malda`) |
| Treating `-> Type` as compile-time typing | Hints are not static types. On **`await`**, the runtime validates JSON against the resolved schema (and may send OpenAI `response_format`). Without `await`, you only build a `PromptInstance`. | Prefer `schema Name { … }` + `await prompt(...) -> Name`; see `Examples/Prompts/schema_prompt_structured.malda`. Offline: `evalPrompt(instance, fixture)` |
| `evalPrompt(p, fixture)` expecting an LLM call | No model, no `@budget` consumption, no gather round. Bad fixtures return `{ ok: false, error }` instead of throwing (unlike `await` after repair). Attachment bytes are not read. | That is the harness. Use `await` / `runPrompt` when you want the model |
| `await prompt` with `attachments:` on the default local GGUF client | In-process llama.cpp is text-only. MALDA throws before loading the model; it does not strip images/PDFs. | Pass `OpenRouterClient` / `LLMClient` (vision/file-capable model). Example: `Examples/Prompts/multimodal_attachments.malda` |
| `@budget(tokens: N)` expecting image/PDF tokens | `@budget` still uses backend usage (or chars/4 on text). Attachment bytes are not a second counter. | Size limits are 8 attachments × 10 MB; pick a vision model that reports usage |
| Sum-type prompt returning a plain object | `prompt p() -> Intent` with `type Intent = …` coerces JSON into a **variant**. Object field access like `intent.tag` is wrong. | Use `match intent { case Buy(sku, qty): … }`. Wire JSON must be `{ "tag": "Buy", …payload }` |
| Same name for `schema Foo` and `type Foo` | Registration throws — a name cannot be both. | Pick one spelling / rename one of them |
| `validate("Intent", taggedDict)` expecting a variant for `match` | Success returns `{ ok, data }` with **`data` still the dict**. Coercion to `Search(q)` / `Buy(…)` happens on `await prompt … -> Intent`, or on `asVariant("Intent", data)`. | `asVariant("Intent", checked.data)` then `match`; or `await` a typed prompt. Example: `docs/llm/few-shot/25_as_variant.malda` |
| `case x: { if (x > 10) … }` expecting later cases to run | An identifier/`_` pattern always matches **unless** the identifier is a declared variant constructor (`case Ok:` matches the `Ok` variant, not everything). An `if` **inside** the body does not fall through. | `case x if x > 10:` then a later case. Failed guards try the next arm. Guarded `Ok` / `_` is **not** exhaustive under `--strict-types` |
| `runProgram` vs `executePlan` / `@Tool` | `runProgram` only calls api methods (no LLM). `executePlan` drives an agent per task step. `@Tool` is a multi-round tool loop. | Use `api` + `program(Api)` + `runProgram` for closed deterministic plans |
| `await solve(expr)` in a `workflow` body | Replay re-runs the body. Model calls are **not** WF1001/WF1002, so the LLM can fire again. | Produce the program JSON before `startWorkflow` (or from a helper used as the `step` call). Execute with `step result = runProgram(prog);`. Example: `docs/llm/few-shot/29_runprogram_in_step.malda` |
| `prompt … -> program(Calc)` JSON with `"2"` / `{type,value}` / nested `{call,args}` in `args` | The host now coerces numeric strings, unwraps `{type,value}`, and flattens nested/`@func` calls before `add`/`mul` run. Leftover objects fail validation (they used to be passed through, so `a + b` concatenated or received an object). | Canonical wire: JSON numbers and `"$t0"` — see `Examples/Prompts/api_program_calc.malda` |
| `api` methods `_add`/`_mul` but the model emits `add`/`mul` or `+`/`*` | Validation used to fail (or the repair loop kept asking for `_add`). The host maps unique aliases, `Api.` prefixes, and operators, and treats a bare `t0` as `"$t0"`. | Keep the underscore names on the `function`s; the program JSON may say `add`. Structured-output schema uses `api` not `@api` so providers do not drop `response_format` |
| `api` / `runProgram` with `--mode js` | JS transpile rejects `api` declarations (host-only, same as prompts). | Interpreter or `malda compile --mode transpile` |
| `new VectorDB(...)` with `--mode js` | JS has no VectorDB runtime (`malda-js-runtime.js`). Emit is a bare `new VectorDB(...)` that fails at run time. | Interpreter or `malda compile --mode transpile` |
| `api` method without a top-level `function` of the same name | `runProgram` fails at the call step. | Declare `function add(a, b) { … }` matching the signature |
| `result.map(r, (x) => result.ok(x + 1))` (or `option.map` returning `Some`/`None`) | `map` transforms a **payload**. Interpreter currently returns a nested variant as-is; C#/JS wrap it as `Ok(Ok(...))` / `Some(Some(...))`. Sequencing fallible steps this way is wrong. | `result.andThen(r, (x) => result.ok(x + 1))` — `fn` must return `Ok`/`Err` (or `Some`/`None` on `option.andThen`) |
| `pdf.extractText(scanned.pdf)` expecting OCR | Extracts the **digital text layer** only (PdfPig). Image-only / scanned PDFs often return empty or near-empty text with no error. | OCR first, or convert to `.md` / `.txt` before BUILD |
| `doc.extractText("old.doc")` | Only **`.docx`** (Office Open XML) is supported. Legacy binary `.doc` throws. | Save as `.docx`, or convert before BUILD |

## Half-truths

**`@shader()` is a JavaScript compile-time GLSL kernel, not a host function.** `malda compile --mode js` inlines those functions through `glsl.compile({ ... })` and does not emit JS. Calling `hitSphere(...)` from the MALDA host, or running the file in the interpreter / C# transpile, will not execute GPU code. Keep uniforms and the `three.*` loop in host MALDA; keep rays in `@shader()` functions. Raw triple-quoted GLSL strings still work.

**Top-level `function` in JS mode cannot see top-level `var`.** The JS transpiler emits `function`s beside `main()` and puts script `var`s inside `main()`, so a helper that reads `gameOver` throws `ReferenceError`. Keep the canvas locals and helpers in one block, as in `Examples/Games/game_bounce.malda` / `malda new game`. Nested `function`s close over the block.

**`malda play` serves `.malda-play/`, not the source folder.** The command compiles `--mode js` into a sibling preview directory, copies `malda-js-runtime.js`, and (if present) copies `assets/`. Edits to `app.malda` are not live-reloaded — stop and run `malda play` again. `game.loadImage("assets/foo.png")` works in preview because `assets/` is copied; a file sitting next to the `.malda` but outside `assets/` is not. PWA output is still `malda compile --mode pwa`. Fullstack score apps (`malda new game --fullstack`) are refused: compile `--mode fullstack` and run the server with `MALDA_WEB_DIRECTORY`. Example: `malda new game my-game` then `malda play app.malda`.

**Two `function boot()` declarations, one `@client()` and one `@server()`, are how a shared `boot();` call hits the right backend.** Unmarked top-level calls go to both partitions. Duplicate names are filtered by target before emit. Do not run that file with the interpreter. Template: `Templates/game-fullstack/app.malda`.

**`game.startFixed` does not pass wall-clock `dtMs`.** `update` always receives `tickMs` (default `1000 / 60`). A hitch runs at most five catch-up ticks and drops the rest — you no longer copy `if (dt > 50) dt = 50`. `game.start` and `startFixed` cannot run together. `game.save` / `load` / `removeSave` are origin `localStorage` under `malda.game.`, not files; missing or corrupt data is `null`. Example: `Examples/Games/game_fixed_save_smoke.malda`.

**`game.audioStopSample` does not stop the music track or pattern.** Sample one-shots share the Web Audio node cap with tones, but `audioStopSample(url?)` never calls `audioStopTrack` / `audioStopPattern`. Omit the URL to stop every sample. Decode is cached per URL; a missing file is a silent no-op. Call `audioInit` after a click or key or the browser will mute everything. Example: `Examples/Games/game_audio_sample_smoke.malda`.

**`game.overlapRect` counts touching edges, not only interiors.** Inclusive AABB: a box at `x=0,w=10` overlaps one at `x=10`. Zero or negative width/height/radius is `false`. Helpers are pure (no canvas) and ignore `setCamera`. Example: `Examples/Games/game_collision_smoke.malda`.

**`game.overlapRect` does not stop a fast mover.** A box that ends the tick past a thin wall reports no overlap — it tunneled. `game.sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh)` returns `{ hit, t, nx, ny, x, y }` at first contact (`t` is 1 and `x`/`y` are the end pose on a miss). `game.sweepRects(..., obstacles)` is the same against an array of `{ x, y, w, h }` (earliest `t`; ties keep the first). Canvas Y+ is down, so a floor is `ny = -1`. Walking along a touching floor while sweeping X is not a hit. Positive-area overlap at the start is `t` 0 plus an MTV normal — not a physics engine. Example: `Examples/Games/game_collision_smoke.malda`.

**`game.tileAt` is cell coordinates, not pixels.** `col` is X and `row` is Y (row 0 at the top). Values are floored. Out of range is `out` (default `0`), not a throw — a cave that should be steel outside must pass `{ "out": WALL }`. Nested rows (`cells[row][col]`) or a flat array **plus** `columns`; a flat array without `columns` is an empty map. `drawTiles` skips id `0` (atlas index is `id - 1` unless you set `firstId`). `sweepTiles` is CCD against solid cells, not Tiled. Example: `Examples/Games/game_tiles_smoke.malda`.

**`game.wasKeyPressed` is true for one update, never in `render`.** Edges are snapshotted at the start of `update` and cleared before `render`. Holding a key does not retrigger. Same names as `isKeyDown`. `wasMousePressed` / `wasMouseReleased` use the same clock (default button `0`). `wasGamepadButtonPressed` / `wasGamepadButtonReleased` match that clock. `getTouches()` is canvas pixels; the first active touch still aliases mouse button 0 (so a new touch also edges `wasMousePressed(0)`). Missing pads return disconnected / axis `0`. `getGamepadAxis(index, axis, deadzone?)` keeps the two-arg form raw; optional deadzone returns `0` when `|v| ≤ deadzone` (no radial rescale). Example: `Examples/Games/game_input_smoke.malda`.

**`game.loadImage` is ready later, not now.** The call returns a handle immediately. `drawImage` / `drawImageRect` / `drawImageEx` / `drawTiles` silently no-op until `imageIsReady` is true (missing files stay unready and do not throw). `imageWidth` / `imageHeight` are `0` until then — do not use them as atlas frame size before ready. Start the loop anyway; check readiness in `render`. Camera (`setCamera`) offsets `fillRect` / images / text / lines / `strokeCircle`, not `setPixel` / `blitPixels`. `setCameraZoom` (default `1`) also scales those world sizes; non-positive zoom resets to `1`. `measureText` ignores pan and zoom (font pixels at zoom 1 — measure HUD after `pushCamera` + `setCameraZoom(1)`). `getMouseX` stays in canvas pixels; `getMouseWorldX` / `screenToWorld` add the camera and divide by zoom. HUD: `pushCamera` + `setCamera(0, 0)` + `setCameraZoom(1)` + draw + `popCamera`. `drawImageEx` flip/rotate around `(ox, oy)` (default dest top-left): `flipX` without `ox` draws to the left of `x` — set `ox` to `w / 2` for in-place facing. `drawImageEx` `tint` is multiply (white is identity); white hit-flash needs `tintFill: true`. `setPixelated(true)` is opt-in crisp pixels; `createCanvas` turns it back off. Example: `Examples/Games/game_sprite_smoke.malda`.

**`game.setBlend` sticks until you reset it.** `"add"` / `"multiply"` / `"screen"` apply to every later `fillRect` / image / text draw (not `setPixel` / `blitPixels`). `clear()` still wipes the frame in `"alpha"` and does **not** restore the mode — call `setBlend("alpha")` after sparks. Unknown names become `"alpha"`. `createCanvas` resets blend. Example: `Examples/Games/game_sprite_smoke.malda`.

**`three.createTexture` / `three.loadGLTF` are ready later, not now.** The calls return a handle/group immediately. `createStandardMaterial({ "map": handle })` leaves `map` unset until the texture is ready (missing files stay unready and do not throw). `loadGLTF` is a group you can `add` right away; children appear when `modelIsReady` is true. Failures stay unready. Orbit controls are not in the wrapper. Example: `Examples/Games/three_textured.malda`.

**`three.mandelbrotOrbit` is JavaScript BigInt, not the interpreter.** Pass **decimal strings** for the center (`"-0.75"`, not a MALDA float). Host numbers are IEEE double and will round a deep C before the orbit is built. The helper packs a 1-row float texture; `createShaderMaterial` / `setUniform` unwrap that handle for `sampler2D`. Interpreter and C# transpile do not run `three.*`. Example: `Examples/Games/three_shader_mandelbrot.malda`.

**The interpreter and `malda compile --mode transpile` are different backends.** A program that
runs under the interpreter is not automatically a program that compiles. Prefer the
interpret/transpile pair suite (`InterpretTranspilePairTests`) when you need a shippable
`.exe`. Escape sequences inside `$"..."` (for example `\n`,
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

**Typed prompts send `response_format` to OpenAI-compatible chat APIs** when `-> Type` is set
and the body is not a Mode C `gather:` instance. In-process GGUF (LLamaSharp) converts the
same schema to a **GBNF grammar** and constrains sampling (Mode A / Mode C extract). Tool
rounds (Mode B) stay unconstrained so the model can emit tool calls. MALDA also appends a
compact **schema appendix** to the system message. Validation + repair retries still run
after the reply. If an OpenAI-compatible backend rejects `response_format` (including tools
+ `json_schema`), the host retries once without it (keeps tools) and remembers that backend.
Supported modes: **A** typed structured (no tools); **B** tools listed, structured-if-supported
(one call); **C** `gather:` then a typed extract without tools — see
`Examples/Prompts/prompt_tools_mode.malda` and `prompt_tools_then_structured.malda`.

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
