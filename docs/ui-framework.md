# Server-driven UI framework (runtime / host)

Engine and host notes for MALDA’s Blazor-like server-driven UI. For the language API
(`ui.*`, components, controls, lifecycle), use
[`ReferenceManual/16-web-ui.html`](../ReferenceManual/16-web-ui.html)
(start from [`16-web-ui-hub.html`](../ReferenceManual/16-web-ui-hub.html)).

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

- `ui.state(componentId, key, defaultValue, scope?)`
- `ui.setState(componentId, key, value, scope?)`

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
| Language API (user docs) | `ReferenceManual/16-web-ui.html` |
