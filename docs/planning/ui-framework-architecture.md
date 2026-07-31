# Server UI Framework Architecture

This document describes the built-in MALDA server UI framework introduced for Blazor-like server-driven rendering.

## Runtime Model

- MALDA code builds UI trees through `ui.*` helpers.
- UI API V2 follows normalized control signatures (`props`, `children`, optional `key`).
- Trees are represented as `UiNode` objects in the runtime kernel.
- Sessions are tracked by `UiSessionRegistry`, each with:
  - last rendered tree
  - queued UI events
- `UiDiffEngine` computes incremental patches between old/new trees.
- `UiControlSpecRegistry` validates prop/event contracts per control type.

## Data Flow

1. `ui.mount(root, sessionId)` stores initial tree and returns a `mount` patch envelope.
2. `ui.render(root, sessionId)` diffs against prior tree and returns a `patch` envelope.
3. Browser host receives envelope and applies patches.
4. Browser interactions are sent back as `event` messages.
5. Server code calls `ui.dispatchEvent(...)` and `ui.pullEvent(...)` to process event queues.

## Protocol Shape

- `mount` and `patch` envelopes contain:
  - `type`
  - `version`
  - `sequence`
  - `envelopeId`
  - `sessionId`
  - `patches[]`
- Control envelopes:
  - `ack` / `nack`
  - `error`
  - `resync`
- Patch operations:
  - `ReplaceNode`
  - `SetProp`
  - `RemoveProp`
  - `InsertChild`
  - `RemoveChild`

## State Model

- State storage uses existing component state store (`HttpServerInstance`), wrapped by:
  - `ui.state(componentId, key, defaultValue, scope?)`
  - `ui.setState(componentId, key, value, scope?)`
- `ui.invalidate(channel, payload?)` emits a live invalidation message through SSE.

## Host

`MaldaLang.UIHost` provides a minimal web host with:
- static browser runtime (`wwwroot/malda-ui-client.js`)
- WebSocket endpoint (`/ui/ws/{sessionId}`)
- mount/patch forwarding endpoints (`/ui/mount/{sessionId}`, `/ui/patch/{sessionId}`)
- heartbeat ping/pong and stale connection eviction
- optional auth token (`MALDA_UI_AUTH_TOKEN`)
- CORS policy (`MALDA_UI_ALLOWED_ORIGIN`)
