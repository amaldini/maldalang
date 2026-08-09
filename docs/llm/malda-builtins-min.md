# MALDA builtins (minimum set for codegen)

*Applies to: MALDA 0.1.33*

**If a name is not in [`malda-builtins.tsv`](malda-builtins.tsv), it does not exist â€” do not
invent it.** The TSV is generated from the engine and guarded by a test, so absence from it
is authoritative, not merely a documentation gap.

A curated introduction, not the catalog. For one specific name, look it up in the generated
table, which lists every built-in with its preferred spelling, its arguments, and any gotcha:

```bash
awk -F'\t' '$1 == "randomInt"' docs/llm/malda-builtins.tsv
# randomInt  math.randomInt  2 arguments: (min, max)  min and max are BOTH included ...
```

The third column is the arity the built-in enforces, in its own words: call it wrongly and
the runtime error says exactly that. An empty third column means the built-in is variadic â€”
`all(...tasks)` and the `ui*` component builders, which all take `(props?, ...children)`.

Full prose catalog: `ReferenceManual/11-built-in-functions.html` and `MaldaLang/BuiltIns/`.

## What exists at the top level

Knowing *what to look for* is most of the problem. These names are globals; everything else
you call is either a built-in function or a class you construct with `new`.

| Global | Role |
|--------|------|
| `math` | Arithmetic, rounding, trig, random. Also `math.PI`, `math.E`, `math.TAU`, `math.INF`, `math.NaN` |
| `str` | String operations: case, trim, split/join, regex, hashing, base64 |
| `io` | Console, files, paths, environment, git |
| `AnsiConsole` | Rich terminal output: `markupLine`, `markup`, `table`, `panel`, `tree`, `status`, `prompt`, `progress` |
| `ui` | Server-driven UI composition (web/full-stack apps) |
| `result` / `option` | `Result` and `Option` constructors and combinators |

Built-in classes you instantiate with `new`: `LLMClient`, `OpenRouterClient`,
`LlamaCppClient`, `LlamaEmbedder`, `Agent`, `CodingAgent`, `MALDACodingAgent`, `DevAgent`,
`GitAgent`, `HumanAgent`, `Tool`, `Conversation`, `VectorDB`, `GraphMemory`, `SqliteClient`,
`PostgresClient`, `SqlServerClient`, `HttpServer`, `RestServer`, `RestClient`, `LLMServer`,
`MCPClient`, `MCPServer`, `ACPClient`, `ACPServer`, `ACPAgentTool`, `HTMLCache`,
`SerialConnection`, `ArduinoConnection`.

## I/O and basics

| Call | Role |
|------|------|
| `io.print(x)` | Console output, appends a newline. There is **no** `println` |
| `io.input(prompt?)` | Read one line from stdin |
| `sleep(ms)` | Real-time delay (prefer over busy loops) |
| `string(x)` / `int(x)` / `float(x)` / `bool(x)` | Coerce. `toIntOr(x, fallback)` / `toIntOrNull(x)` do not throw |
| `str.length(s)`, array `.length`, indexing `a[i]` | Size and access |

## Strings / JSON

| Call | Role |
|------|------|
| `parseJSON(text)` | Parse a JSON string â†’ object/array |
| `toJSON(value)` | Serialize to a JSON string |
| `str.upper/lower/trim/split/join/replace` | Common string ops |
| `str.repeat(s, count)` | Repeat a string; `count` must be an integer or a **whole-valued** float |
| `+` / `$"â€¦"` | Concatenation, or `$`-prefixed interpolation (`$"n={n}"`). Plain `"n={n}"` prints the braces literally. Prompt bodies interpolate without `$` |

`parseJson` (lowercase `s`) is a **different** built-in â€” a schema-validating parser for LLM
output. Do not reach for it to read JSON.

## Arrays / maps

Arrays use **member-style** methods (`items.append(x)`), not free functions. That is a
different convention from `str.length(s)` / `math.sqrt(x)`. There is no `arr` namespace â€”
`arr.append(history, x)` and `arr.length(history)` do not exist. In
[`malda-builtins.tsv`](malda-builtins.tsv) the preferred-call column writes these as
`<array>.append` / `<array>.pop` / `<array>.shift` to mark a method on the receiver.

| Pattern | Role |
|---------|------|
| `[1, 2, 3]` | Array literal |
| `dict { "a": 1 }` | Dict literal |
| `{ "a": 1 }` | Object literal (common in APIs) |
| index `items[i]`, member `obj.field` | Access |
| `items.length` | Element count â€” a **property**, not a call (`items.length()` errors). Also `str.length(items)` |
| `items.append(x)` | Append one item (method on the array) |
| `items.pop()` / `items.shift()` | Remove last / first element |

## Math and randomness

| Call | Role |
|------|------|
| `math.abs/round/floor/ceil/sqrt/pow/min/max/sum/average` | Arithmetic |
| `math.floor` / `round` / `ceil` / `sqrt` | Always return a **float**; whole-valued results coerce at integer sinks (`repeat`, indexes, â€¦) |
| `math.randomInt(min, max)` | Random integer, **both endpoints included** |
| `math.random()` / `math.randomFloat(min, max)` | Random floats |
| `math.seed(n)` | Pin the sequence so a run is reproducible and testable |

## AnsiConsole

| Call | Role |
|------|------|
| `AnsiConsole.markupLine(text)` | Markup + trailing newline |
| `AnsiConsole.markup(text)` | Markup, **no** trailing newline |
| `AnsiConsole.panel(body, title?, border?)` | Bordered box; body and title parse markup |
| `AnsiConsole.table(rows, title?, columns?)` | Table; **cell values and headers parse markup too** |
| `AnsiConsole.tree(label, items?)` | Tree; items are strings or `{ "label", "children"? }` |
| `AnsiConsole.status(message, action?)` | Spinner around optional 0-arg callback |
| `AnsiConsole.progress(callback)` | `ctx.addTask` / `increment` / `isFinished` |
| `AnsiConsole.prompt(config)` | Interactive `{ type, message, defaultValue? }` â€” not pipe-friendly |

`io.getEnv(name)` returns **`null`** when unset (not `""`). See the TSV `returns` column.

Markup tags (Spectre.Console): styles `bold`, `dim`, `italic`, `underline`, `strikethrough`;
colours `black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`, `grey` /
`gray`, plus `bright*` forms (`brightred`, â€¦); close with `[/]`. Combine as
`[bold blue]â€¦[/]`. Double a bracket to print it literally: `[[` / `]]`.

`panel` `borderStyle` values that mean something: `"rounded"`, `"double"`, `"heavy"`.
Anything else silently becomes square. Piped / redirected output also collapses Unicode
borders to square â€” see [`malda-gotchas.md`](malda-gotchas.md).

## Files / OS (when needed)

`io.readFile`, `io.writeFile`, `io.pathExists`, `io.pathJoin`, `io.listDirectory`, `io.glob`,
`io.grep`, `io.getEnv`, and the `io.git*` family. Grep the TSV for exact arguments rather
than guessing.

## HTTP / web

| Pattern | Role |
|---------|------|
| `@GET("/path")` / `@POST(...)` | REST handlers on functions |
| `@PAGE("/path")` | HTML/page handler (route-first HTML strings) |
| `RedirectTo(url)` | Redirect response helper (see web examples) |
| `http.enableSession(secret)` / `disableSession` | Signed session cookie; `req.session` get/set/flash |
| `http.enableCsrf(secret)` + `csrfField(...)` | Cookie + form `_csrf` must match (see gotchas) |
| `http.mount(restServer)` | Serve API + UI on one `HttpServer` port (`new RestServer()` defers port) |
| `bindForm` / `formErrors` / `pageLayout` | CSRF-aware forms and simple `@PAGE` chrome |
| `enqueueJob` / `claimJob` / `completeJob` / `failJob` / `getJob` / `listJobs` | Lightweight job queue in `./.malda/jobs.db` (not durable workflows) |

### Server-driven UI (`ui.*`)

Prefer this when building component trees (not raw HTML pages). There is **no JSX** and no
HTML-string children â€” compose nodes with `ui.*` helpers.

| Pattern | Role |
|---------|------|
| `ui.control(props, children?, key?)` | V2 signature for controls (`ui.button`, `ui.column`, `ui.text`, â€¦). `props` is an object/dict |
| `ui.mount(root, sessionId?)` / `ui.render(root, sessionId?)` | Mount or diff a tree; returns a patch envelope |
| `ui.mountEnvelope(root, sessionId?, options?)` | Mount + snapshot + resync helper in one call |
| `ui.dispatchEvent(event, sessionId?)` / `ui.pullEvent(sessionId?)` | Client â†’ server event queue |
| `ui.state` / `ui.setState` (or `componentState*`) | Server-side component state |
| `component Name(...) { â€¦ }` + `@ACTION` / `@LIVE` | Full-stack component model (see Reference Manual) |

Use `@PAGE` + `pageLayout` for route-first HTML; use `ui.*` (+ optional `component`) for
tree/patch UI. Grep `malda-builtins.tsv` for a control name (`uiButton`, `uiDataGrid`, â€¦);
call it as `ui.button(...)`, `ui.dataGrid(...)`.

API reference: `ReferenceManual/16-web-ui.html` (start at `16-web-ui-hub.html`).
Runnable shapes: `docs/llm/few-shot/19_ui_tree.malda`, `Examples/Web/ui_*.malda`,
`Templates/fullstack/`.

See also `Examples/Web/auth_cookie_login.malda`, `Examples/Web/form_validate_flash.malda`
(CSRF + `bindForm` + `validate` + flash), and `docs/tutorials/fullstack-sessions-auth.md`.

## AI

| Pattern | Role |
|---------|------|
| `prompt name(args) { user: "..."; }` | Prompt declaration |
| `prompt name(args) -> SchemaName { â€¦ }` + `await` | Typed prompt: JSON Schema â†’ `response_format` (no tools); validate + repair |
| `new OpenRouterClient(...)` / `LLMClient` | LLM clients (see `Examples/Prompts`, `Examples/AI_LLM`) |
| `new Agent(...)` / `CodingAgent` | Agents + tools |

Structured await example: `Examples/Prompts/schema_prompt_structured.malda`
(and few-shot `docs/llm/few-shot/18_schema_prompt.malda`).

## Actors

| Pattern | Role |
|---------|------|
| `actor Name { message ...; on ... {} }` | Actor type |
| `spawn Name()` | Start actor |
| `send target.msg(args)` | Message send |
| `reply(value)` | Reply from handler |
| `sleep(ms)` | Allow async processing in demos |
