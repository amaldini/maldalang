# Server-driven UI framework (runtime / host)

Engine and host notes for MALDA’s Blazor-like server-driven UI. For the language API
(`ui.*`, components, controls, lifecycle), use
[`ReferenceManual/24-web-ui.html`](../ReferenceManual/24-web-ui.html)
(start from [`23-web-ui-hub.html`](../ReferenceManual/23-web-ui-hub.html)).

## Runtime model

- MALDA code builds UI trees through `ui.*` helpers.
- UI API V2 uses normalized control signatures (`props`, `children`, optional `key`).
- Trees are `UiNode` objects in the runtime kernel (`MaldaLang/Runtime/UI/`).
- Sessions live in `UiSessionRegistry`, each holding:
  - last rendered tree
  - queued UI events
- `UiDiffEngine` computes incremental patches between old and new trees.
- `UiControlSpecRegistry` validates prop/event contracts per control type.

## Data flow

1. `ui.mount(root, sessionId)` stores the initial tree and returns a `mount` patch envelope.
2. `ui.render(root, sessionId)` diffs against the prior tree and returns a `patch` envelope.
3. The browser host receives the envelope and applies patches.
4. Browser interactions are sent back as `event` messages.
5. Server code calls `ui.dispatchEvent(...)` and `ui.pullEvent(...)` to drain event queues.

## Event loop contract

For interactive `ui.*` sessions, keep this order after each client event:

1. `ui.dispatchEvent(event, sessionId?, sequence?)` (host/runtime enqueues; examples may simulate). `sessionId` is always the second argument.
2. `ui.pullEvent(sessionId)` — drain before rebuilding
3. Update `ui.setState` / locals from the payload
4. Rebuild the tree
5. `ui.render(nextTree, sessionId)`

Skipping `pullEvent` (or updating state) before `render` leaves the queue full and the UI looks stuck. IDE/LSP reports **UI1001** for linear `dispatchEvent` → `render`/`mount` without an intervening `pullEvent`.

Canonical offline example: [`Examples/Web/ui_event_loop.malda`](../Examples/Web/ui_event_loop.malda)  
Showcase: [`Examples/Web/ui_counter_dashboard.malda`](../Examples/Web/ui_counter_dashboard.malda)  
Agent-facing gotcha: [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md)

## One model per surface

| Model | Use when |
|-------|----------|
| `@PAGE` / `@AIPAGE` + HTML strings | Route-first pages (`pageLayout`, forms, redirects) |
| `ui.*` trees + `ui.mount` / `ui.render` | Server-driven component patches / UIHost |

Do not pass HTML strings into `ui.mount`/`ui.render`, or treat a `@PAGE` return value as a `UiNode` tree. Combining models in one product is fine when boundaries are explicit (e.g. marketing `@PAGE` + app shell `ui.*`). IDE reports **UI1002** (Info) when a file mixes `@PAGE`/`@AIPAGE` with `ui.mount`/`ui.render`. Chooser: [`ReferenceManual/23-web-ui-hub.html`](../ReferenceManual/23-web-ui-hub.html).

## Protocol shape

`mount` and `patch` envelopes include:

- `type`
- `version`
- `sequence`
- `envelopeId`
- `sessionId`
- `patches[]`

Control envelopes: `ack` / `nack`, `error`, `resync`.

Patch operations: `ReplaceNode`, `SetProp`, `RemoveProp`, `InsertChild`, `RemoveChild`.

## State model

State uses the component state store (`HttpServerInstance`), wrapped by:

| API | Behavior |
|-----|----------|
| `ui.state(id, key, default, scope?)` | **Get-or-create** — if the key is missing, **persists** `default` |
| `ui.getState(id, key, default?, scope?)` | **Peek** — returns `default` without writing (same as `componentStateGet`) |
| `ui.setState(id, key, value, scope?)` | Write |
| `ui.pinState(id, scope?)` | Mark the scoped entry as **pinned** (exempt from TTL + LRU) |
| `ui.unpinState(id, scope?)` | Clear the pin flag (values kept) |

Flat aliases: `componentStateGet/Set/Object/Clear/Configure/Pin/Unpin`.

Defaults are conservative (`maxComponents=512`, TTL 30 minutes). Conversation-scoped
entries should stay unpinned; process-lifetime data (brain catalog, server config)
should be **pinned** after the first write. Anti-pattern: `ui.state(id, "x", null)` or
`ui.state(id, "x", {})` on critical keys — after eviction that poisons the store.
IDE/LSP reports **UI1003** (Warning) when the get-or-create default is a literal
`null` or `{}` (flat alias `uiState` included). Prefer `ui.getState` for optional
reads, or non-null initializers (`[]` / `0` / `""`).

Canonical offline example: [`Examples/Web/ui_state_lifecycle.malda`](../Examples/Web/ui_state_lifecycle.malda)

`ui.invalidate(channel, payload?)` emits a live invalidation message over SSE.

## Host

`MaldaLang.UIHost` is a minimal web host with:

- static browser runtime (`wwwroot/malda-ui-client.js`)
- WebSocket endpoint (`/ui/ws/{sessionId}`)
- mount/patch forwarding (`/ui/mount/{sessionId}`, `/ui/patch/{sessionId}`)
- heartbeat ping/pong and stale-connection eviction
- optional auth token (`MALDA_UI_AUTH_TOKEN`)
- CORS policy (`MALDA_UI_ALLOWED_ORIGIN`)

The same host surface can be embedded from the CLI/Desktop when a program uses `ui.mount`
(see `MaldaLang.UIHost/EmbeddedUiHostRuntime.cs`). Publish/transpile paths may also emit
host wiring from `MaldaLang.Compiler`.

## Code map

| Concern | Primary path |
|---------|----------------|
| Nodes / diff / sessions / control specs | `MaldaLang/Runtime/UI/` |
| `ui.*` builtins | `MaldaLang/BuiltIns/BuiltInFunctions.cs` (UI section) |
| Standalone / embedded host | `MaldaLang.UIHost/` |
| Browser client | `MaldaLang.UIHost/wwwroot/malda-ui-client.js` |
| Language API (user docs) | `ReferenceManual/24-web-ui.html` |
