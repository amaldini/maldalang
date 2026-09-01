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
| Standard library (`math` / `str` / `io`, AnsiConsole) | yes | yes | partial (`math` / `str` / `io.print` / `io.input` / JSON) |
| File I/O | yes | yes | no |
| Actors (`spawn` / `send` / `on`) | yes | yes | yes (process-local) |
| Durable workflows (`workflow` / `step`) | yes | yes | no |
| Workflow call-graph determinism (WF1001/WF1002 via in-file helpers) | yes | yes | n/a |
| Agents / prompts / MCP / ACP | yes | yes | no |
| `schema` / `validate()` / `asVariant()` / `evalPrompt()` | yes | yes | yes (validate + asVariant; payload types n/a). evalPrompt n/a (prompts host-only) |
| Typed prompt `response_format` (schema → OpenAI structured output) | yes* | yes* | no |
| Gather-then-extract prompts (`gather:` + `-> Type`) | yes | yes | n/a |
| Prompt attachments (`attachments:` image/pdf) | yes | yes | n/a |
| Closed `api` / `program(Api)` / `runProgram` | yes | yes | n/a |
| `@budget` resource bounds (tokens / tools / cost) | yes | yes | n/a |
| Grounded values (`grounded.wrap` / GraphMemory `ask`) | yes | yes | wrap only (GraphMemory n/a) |
| Capability tokens (`cap.fileRead` / `cap.read`) | yes | yes | mint / `is` / `confine` (file consume n/a) |
| HttpServer / RestServer / sessions | yes | yes | no |
| UIHost / `ui.*` server-driven UI | yes† | yes† | no |
| Jobs (`enqueueJob` / `claimJob` / `completeJob` / `failJob`) | yes | yes | no |
| Browser `dom.*` / `three.*` / game canvas | n/a | n/a | yes |
| `@shader()` / `glsl.compile` → GLSL (JS compile-time) | n/a | n/a | yes |
| .NET interop | yes | yes | no |

\* OpenAI-compatible chat APIs receive `response_format` (and the host appends a `MALDA_OUTPUT_SCHEMA` appendix) when `await prompt(…) -> Type` has **no `gather:`**. Mode B (`tools:` listed) sends format + appendix with tools; if the backend rejects tools+`json_schema`, the host retries once without format (keeps tools) and remembers that backend. Mode C: `gather:` + `-> Type` runs a tool round, then a fresh typed prompt without tools. In-process llama.cpp / LLamaSharp converts the same schema to a GBNF grammar and constrains Mode A / extract sampling (not Mode B tool rounds). Closed `api` / `program(Api)` / `runProgram` is the same host surface (interpreter + C#); JS transpile rejects `api` declarations. Prompt `attachments:` (image/pdf) become OpenAI content parts on HTTP clients; in-process GGUF throws if attachments are present. `@MCPTool` / `@Tool` third argument may be a registered schema or sum-type name (or a JSON schema object string); an attached schema is host-validated on `callTool` / `tools/call` / `execute` / agent invoke; JS: n/a (MCP is host-only).

† Host embed on interpret/transpile via `MaldaLang.UIHost` when the program uses `ui.mount` / related APIs — see [`docs/ui-framework.md`](../ui-framework.md). Not available on the JS backend.

Jobs are a lightweight SQLite queue (`./.malda/jobs.db`), not durable `workflow` / `step` semantics.

When in doubt, smoke both interpreter and transpile for shippable `.exe`s, and treat JS as browser-only. Silent interpreter≠transpile footguns: [`docs/llm/malda-gotchas.md`](../llm/malda-gotchas.md).

## Related

- Architecture overview: [`docs/architecture.md`](../architecture.md)
- JS backend notes: [`docs/javascript-backend.md`](../javascript-backend.md)
- Server-driven UI host: [`docs/ui-framework.md`](../ui-framework.md)
- Examples catalog: [`Examples/README.md`](../../Examples/README.md)
