# Backend capability matrix (product surface)

**Status:** Active  
**Code source of truth for property-test tags:** [`MaldaLang.Tests/BackendCapabilityMatrix.cs`](../../MaldaLang.Tests/BackendCapabilityMatrix.cs)  
**Tier 0 construct suite:** [`tier0-backend-matrix.md`](tier0-backend-matrix.md)

MALDA programs can run as:

| Backend | How |
|---------|-----|
| **Interpreter** | `malda prog.malda` |
| **C# transpile** | `malda compile prog.malda --mode transpile -o out.exe` |
| **JavaScript** | `malda compile prog.malda --mode js` (browser / Node + runtime) |

Interpreter and C# share the host runtime surface. JavaScript is a **real subset** (DOM / game helpers, not agents or HTTP servers).

## Property-test capability tags

These strings are what `@property` / `runProperty` use via `GetRequiredCapabilities()`. A guard test fails if a tag below is missing from this file.

| Capability tag | Interpreter | C# transpile | JavaScript |
|----------------|:-----------:|:------------:|:----------:|
| `core` | yes | yes | yes |
| `file-io` | yes | yes | no |
| `actors` | yes | yes | no |
| `workflows` | yes | yes | no |
| `dotnet-interop` | yes | yes | no |
| `host-interop` | yes | yes | no |
| `web-dom` | no* | no* | yes |
| `game-canvas` | no* | no* | yes |

\* `web-dom` / `game-canvas` are JS-target capabilities. Host backends may still expose related APIs in other forms; property tests that require these tags only run on JS.

## Product features (what to expect)

| Feature | Interpreter | C# transpile | JavaScript |
|---------|:-----------:|:------------:|:----------:|
| Core language (vars, functions, classes, match, async/await) | yes | yes | yes (Tier 0 subset) |
| Standard library (`math` / `str` / collections / JSON / encodings / conversions, `io.print` / `io.input`) | yes | yes | yes‡ |
| Fixed-size numeric buffers and binary data (`buf.*`) | yes | yes | yes (typed arrays) |
| Host-only stdlib helpers (`markdownToHtml` / `extractHTML` / `hashPassword` / JWT / `race` / `firstOk` / `stream` / `AnsiConsole`) | yes | yes | no |
| File I/O | yes | yes | no |
| Actors (`spawn` / `send` / `on`) | yes | yes | yes (process-local) |
| Worker actors (`@worker() actor`, copied messages, `buf.transfer`) | yes (thread) | yes (thread) | yes (Web Worker / `worker_threads`) |
| Durable workflows (`workflow` / `step`) | yes | yes | no |
| Workflow call-graph determinism (WF1001/WF1002 via in-file helpers) | yes | yes | n/a |
| Agents / prompts / MCP / ACP | yes | yes | no |
| Run journal / cassettes / `malda eval` / `suite` | yes | yes | n/a |
| `within` scopes / declared `context` / `policy` | yes | yes | n/a |
| `stream` / `for await` | yes | yes | n/a |
| Agent teams (`agents.define` / `agents.team` / role-aware `executePlan` / `step.out` / team `decomposeTask`) | yes | yes | n/a |
| `schema` / `validate()` / `asVariant()` / `evalPrompt()` | yes | yes | yes (validate + asVariant; payload types n/a). evalPrompt n/a (prompts host-only) |
| Typed prompt `response_format` (schema → OpenAI structured output) | yes* | yes* | no |
| Gather-then-extract prompts (`gather:` + `-> Type`) | yes | yes | n/a |
| Prompt attachments (`attachments:` image/pdf) | yes | yes | n/a |
| Closed `api` / `program(Api)` / `runProgram` | yes | yes | n/a |
| `@budget` resource bounds (tokens / tools / cost) | yes | yes | n/a |
| Grounded values (`grounded.wrap` / GraphMemory `ask`) | yes | yes | wrap only (GraphMemory n/a) |
| VectorDB (`new VectorDB` / `add` / `searchSimilar` / `asRetriever`) | yes | yes | no |
| ONNX inspect (`new OnnxModel` / `inputs` / `outputs` / `run`) | yes | yes | no |
| Neural helpers (`nn.dense` / activations / `denseBackward` / `Dense` / `Sequential` / `Parallel` / `Identity` / `Conv` / `Conv2D` / `MaxPool2D` / `AvgPool2D` / `Flatten` / `BatchNorm2D` / `Dropout2D` / `GlobalAvgPool2D` / `GlobalMaxPool2D` / `Activation` / `Embedding` / `Rnn` / `LayerNorm` / `Attention`) | yes | yes | yes |
| Capability tokens (`cap.fileRead` / `cap.httpGet` / `cap.mcpCall` / `cap.shell`) | yes | yes | mint / `is` / `confine` (consume host-only) |
| HttpServer / RestServer / sessions | yes | yes | no |
| UIHost / `ui.*` server-driven UI | yes† | yes† | no |
| Jobs (`enqueueJob` / `claimJob` / `completeJob` / `failJob`) | yes | yes | no |
| Browser `dom.*` / `three.*` / game canvas | n/a | n/a | yes |
| `@shader()` / `glsl.compile` → GLSL (JS compile-time) | n/a | n/a | yes |
| JavaScript interop (`import js` modules / globals, dotted `new`) | no§ | no§ | yes |
| .NET interop | yes | yes | no |

\* OpenAI-compatible chat APIs receive `response_format` (and the host appends a `MALDA_OUTPUT_SCHEMA` appendix) when `await prompt(…) -> Type` has **no `gather:`**. Mode B (`tools:` listed) sends format + appendix with tools; if the backend rejects tools+`json_schema`, the host retries once without format (keeps tools) and remembers that backend. Mode C: `gather:` + `-> Type` runs a tool round, then a fresh typed prompt without tools. In-process llama.cpp / LLamaSharp converts the same schema to a GBNF grammar and constrains Mode A / extract sampling (not Mode B tool rounds). Closed `api` / `program(Api)` / `runProgram` is the same host surface (interpreter + C#); JS transpile rejects `api` declarations. Prompt `attachments:` (image/pdf) become OpenAI content parts on HTTP clients; in-process GGUF throws if attachments are present. `@MCPTool` / `@Tool` third argument may be a registered schema or sum-type name (or a JSON schema object string); an attached schema is host-validated on `callTool` / `tools/call` / `execute` / agent invoke; JS: n/a (MCP is host-only).

† Host embed on interpret/transpile via `MaldaLang.UIHost` when the program uses `ui.mount` / related APIs — see [`docs/ui-framework.md`](../ui-framework.md). Not available on the JS backend.

§ `import js` stops the interpreter at the statement and C# transpile at compile time, with a hint to `--mode js`. In a full-stack source it goes to the JavaScript half only. See [Reference Manual 28.11](../../ReferenceManual/28-browser-javascript-backend.html#js-interop).

‡ JavaScript runs every pure `math.*` / `str.*` built-in and the flat collection, JSON, encoding and
conversion helpers (`sort` / `reverse` / `includes` / `range` / `toCsv`, `toJSON` / `parseJSON`,
`urlEncode` / `urlDecode`, `toIntOr` / `toIntOrNull` / `formatNumber`, `isString` / `isArray` / `isObject`,
`assert` / `expect`) with interpreter semantics; the differential fuzz and `InterpretJsPairTests` check them
against the interpreter and C# transpile. Exceptions:

- **One number type.** `typeOf` cannot tell a whole float from an int, `1 == 1.0` is true, and a float
  `-0` prints `0` (see [`docs/llm/malda-gotchas.md`](../llm/malda-gotchas.md)).
- **Transcendental math** (`math.exp` / `log*` / trigonometry / `pow` with a fractional result /
  `sigmoid` / `tanh` / `softmax` / `logSumExp`) uses the host's libm; the last bit can differ from .NET.
- **Random numbers.** `math.seed` / `random` / `randomInt` / `randn` … exist, but the sequence differs from
  the interpreter's for the same seed.
- **Regex flavor.** `str.regex*` and regex `str.split` use JavaScript `RegExp`: .NET-only syntax (`(?i)`,
  atomic groups, `\d` matching non-ASCII digits, named groups numbered after unnamed ones) differs.
  .NET replacement syntax (`$0`, `${name}`, `$+`, `$_`) is honored.
- **Culture.** `sort` on strings uses `localeCompare` (the interpreter uses the current .NET culture);
  `str.upper` / `str.lower` map one character at a time without culture rules (`"İ"` stays `"İ"`).
- **Dict key order.** Integer-like keys (`"2"`) come first in a JavaScript object, so printing,
  `toJSON` and `toCsv` columns can reorder them.
- **`toJSON` of a variant** writes `{"tag":…,"payload":[…]}`; the interpreter writes `"<Variant>"`.
- **Error paths.** Wrong argument types or arity often coerce on JavaScript where the interpreter stops
  with an error; `str.includes(text, part)` also accepts a string (the interpreter wants an array).
- **Dates and environment** (`now` / `formatDate` / `getEnv` …) follow the browser or Node clock, time zone
  and environment.
- **Not on JavaScript (`JS0014` and the rows above):** `markdownToHtml`, `extractHTML`, `hashPassword` /
  `verifyPassword`, `createJwt` / `verifyJwt`, `race` / `firstOk` / `stream`, `AnsiConsole.*`; web-server
  helpers (`renderTemplate`, `component*`, CSRF / cookies / forms: `JS0007`); AI-pipeline helpers
  (`embed*`, `splitDocuments`, `composePipe`, `webFetch` / `webSearch` …: `JS0004`); host info
  (`exit`, `getMaldaHome`, `getCommandLineArgs` …: `JS0011`).

Jobs are a lightweight SQLite queue (`./.malda/jobs.db`, or `MALDA_JOBS_CONNECTION`), not durable `workflow` / `step` semantics.

## JavaScript pre-check diagnostics

`malda compile --mode js` / `--mode pwa` run one AST pass (`MaldaLang.Compiler/JsCapabilityChecker.cs`)
before emit. It reports **every** use of a feature this matrix marks `no` for JavaScript, as
`Line L:C - JS0xxx: …`, and writes no output. Names the program declares itself (a user
`function readFile`, `class VectorDB`) are not reported. `NotSupportedException` in the
JavaScript transpiler stays as a safety net. `BackendCapabilityMatrixGuardTests` fails when a
JavaScript `no` row has no rule or no fixture (`MaldaLang.Tests/JsCapabilityCheckerTests.cs`).

| Code | Feature | Detected on | Matrix rows |
|------|---------|-------------|-------------|
| `JS0001` | File I/O | `readFile` / `writeFile` / `hasFile` / `glob` / `git*` / `path*` …, `io.*` file members, `pdf.*` / `doc.*` | File I/O, `file-io` |
| `JS0002` | Durable workflows | `workflow` declarations, `startWorkflow` / `getWorkflow*` / `listWorkflows` … | Durable workflows, `workflows` |
| `JS0003` | Prompts | `prompt` declarations (typed or not), `runPrompt`, `evalPrompt` | Agents / prompts, Typed prompt `response_format` |
| `JS0004` | Agents / LLM clients / MCP / ACP | `agents.*`, `new Agent` / `LLMClient` / `OpenRouterClient` / `Conversation` / `MCPServer` / `ACPServer` / `GraphMemory` …, `create*Tool`, `executePlan`, `graph` literals, skills, `embed*` / retrieval-pipeline helpers, `webFetch` / `webSearch` | Agents / prompts / MCP / ACP |
| `JS0005` | VectorDB | `new VectorDB` | VectorDB |
| `JS0006` | ONNX | `new OnnxModel` | ONNX inspect |
| `JS0007` | HTTP servers | `new HttpServer` / `RestServer` / `HTMLCache`, `renderTemplate` / `component*` / `redirect` / CSRF / secure cookies / `bindForm` / `pageLayout` | HttpServer / RestServer / sessions |
| `JS0008` | UIHost | `ui.*`, `ui*` builtins, `generateUI` | UIHost / `ui.*` |
| `JS0009` | Jobs | `enqueueJob` / `claimJob` / `completeJob` / `failJob` / `getJob` / `listJobs` | Jobs |
| `JS0010` | .NET interop | `loadAssembly` / `getDotNetType` / `dotnetNew` / `loadNativeModule` / `createNativeCallback` | .NET interop, `dotnet-interop` |
| `JS0011` | Host processes and drivers | `runCommand` / `runMALDA` / `compileMALDA` / `checkMalda` / `getSymbols` / `exit` / `getMaldaHome` / `getCommandLineArgs` …, `new SqliteClient` / `PostgresClient` / `SqlServerClient` / `SerialConnection` / `ArduinoConnection` | `host-interop` |
| `JS0012` | Closed api | `api` declarations, `runProgram` | Closed `api` |
| `JS0013` | Agentic runs | `within (…) { }`, `context`, `policy`, `suite` | `within` scopes / `context` / `policy`, `suite` |
| `JS0014` | Host-only stdlib helpers | `markdownToHtml` / `extractHTML` / `hashPassword` / `verifyPassword` / `createJwt` / `verifyJwt` / `race` / `firstOk` / `stream`, `AnsiConsole.*` | Host-only stdlib helpers |

The `actors` property tag is `no` for `@property` runs only; the JavaScript backend runs actors
process-local, so the pre-check does not reject them.

When in doubt, smoke both interpreter and transpile for shippable `.exe`s, and treat JS as browser-only. Silent interpreter≠transpile footguns: [`docs/llm/malda-gotchas.md`](../llm/malda-gotchas.md).

## Related

- Architecture overview: [`docs/architecture.md`](../architecture.md)
- JS backend notes: [`docs/javascript-backend.md`](../javascript-backend.md)
- Server-driven UI host: [`docs/ui-framework.md`](../ui-framework.md)
- Examples catalog: [`Examples/README.md`](../../Examples/README.md)
