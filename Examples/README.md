# MALDA Examples

Learning paths: [`docs/start-here.md`](../docs/start-here.md).  
Fullstack sessions/auth/jobs walkthrough: [`docs/tutorials/fullstack-sessions-auth.md`](../docs/tutorials/fullstack-sessions-auth.md).

Run from the repo root (or a distribution that ships `Examples/`):

```bash
malda Examples/Basics/first_look.malda
# or: dotnet run --project MaldaLang -- Examples/Basics/first_look.malda
```

`first_look.malda` is the characteristic offline sample (`prompt … -> Review` binds the schema; `validate` is the same check without `await`).
Cross-platform CI smoke is `Examples/Basics/first_look.malda`. `hello_world.malda` remains the Learn Programming one-liner.

## `requires` labels

Each folder’s `metadata.json` may tag examples with `requires`:

| Value | Meaning |
|-------|---------|
| `offline` | No API key, DB, or long-lived network (default if omitted) |
| `network` | Opens a local HTTP port or uses the network |
| `api-key` | Needs OpenRouter (or configured LLM provider) |
| `db` | Needs a database (SQLite file, Postgres, SQL Server, …) |

## Folders

| Folder | Focus | Typical requires | Track(s) |
|--------|--------|------------------|----------|
| [Basics](Basics/) | Syntax, control flow, functions | offline | student |
| [OOP](OOP/) | Classes and inheritance | offline | student |
| [Prompts](Prompts/) | `prompt` declarations | api-key (most) | ai-builder |
| [AI_LLM](AI_LLM/) | Clients and conversations | api-key | ai-builder, showcase |
| [Agents](Agents/) | Agents and tools | api-key | ai-builder, showcase |
| [Web](Web/) | HttpServer, REST, UI, JS DOM target, auth, jobs | network (servers); offline (job queue demo) | student, ai-builder, showcase |
| [Games](Games/) | Canvas `game.*` and `three.*` graphics (JS target) | offline | student, showcase |
| [Workflows](Workflows/) | Durable `workflow` / `step` | offline | ai-builder |
| [Testing](Testing/) | `malda test` and property tests | offline | student, showcase |
| [Actors](Actors/) | `spawn` / `send` / `on` | offline | — |
| [ACP](ACP/) | Agent Client Protocol | network / api-key | — |
| [MCP](MCP/) | Model Context Protocol | network | — |
| [SpectreConsole](SpectreConsole/) | Rich terminal UI | offline | — |
| [Databases](Databases/) | SQLite / Postgres / SQL Server | db | student, showcase |
| [Graphs](Graphs/) | Graph algorithms | offline | — |
| [VectorDB](VectorDB/) | Embeddings store | offline / api-key | — |
| [Memory](Memory/) | Shared GraphMemory | api-key | — |
| [Tools](Tools/) | File and custom tools | offline | — |
| [Plan](Plan/) | Plan / decompose prompts | api-key | — |
| [LLM_Servers](LLM_Servers/) | Local LLM server patterns | network / api-key | — |
| [Devices](Devices/) | Device / IoT-style demos | offline | — |
| [Assistant](Assistant/) | Assistant + skills | api-key | — |
| [RalphWiggum](RalphWiggum/) | PRD-driven agent loop | api-key | ai-builder |
| [Arduino](Arduino/) | Arduino / ESP32 bridge sketches (`.ino`) | — (hardware) | — |

Per-example detail lives in each folder’s `metadata.json` (and short README where present).

## Root demos

Loose `.malda` files in this directory (not under a subfolder):

| File | Notes |
|------|--------|
| `countwords.malda` | Small CLI-style demo |
| `program.malda` | Generic sample entry |
| `rest_api_example.malda` | Older REST sample; prefer `Web/rest_api_server.malda` |
| `ui_contact_form.malda` | Contact form on HttpServer (README gif pattern) |
| `ui_dashboard.malda` | Dashboard UI demo |

Prefer the folder catalog above for structured learning.
