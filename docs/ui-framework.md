# Server-driven UI framework (runtime / host)

Engine and host notes for MALDA’s Blazor-like server-driven UI. For the language API
(`ui.*`, components, controls, lifecycle), use
[`ReferenceManual/26-web-ui.html`](../ReferenceManual/26-web-ui.html)
(start from [`25-web-ui-hub.html`](../ReferenceManual/25-web-ui-hub.html)).

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
3. When the host runs in process, both envelopes also go to every browser on
   `/ui/ws/{sessionId}`, which applies the patches. The return value stays for tests and
   offline programs.
4. Browser interactions come back as `event` messages and land in the session's queue.
5. Server code drains the queue with `ui.pullEvent(sessionId)`. `ui.dispatchEvent(...)`
   queues an event by hand, for offline programs and tests.

The link between sessions and host is `UiHostLink` (`MaldaLang/Runtime/UI/`). The CLI, the
Desktop IDE and transpiled programs start the host through `UiHostLink.StartAsync()`; after
that, every `UiSessionRegistry` session publishes its mount / patch envelopes to
`UiHostHub.Publish`, and `UiHostHub.ClientEvent` feeds browser events to
`UiSession.EnqueueEvent`. The link lives in `MaldaLang` because `MaldaLang` references the
host, not the other way round. A `UiSession` built by hand publishes nothing.

## Event loop contract

For interactive `ui.*` sessions, keep this order after each client event:

1. The event arrives: from the browser through the host, or from
   `ui.dispatchEvent(event, sessionId?, sequence?)` in offline examples. `sessionId` is always
   the second argument.
2. `ui.pullEvent(sessionId)` — drain before rebuilding. It returns `null` at once when the
   queue is empty, so a live program polls with a short `sleep` between empty pulls.
3. Update `ui.setState` / locals from the payload
4. Rebuild the tree
5. `ui.render(nextTree, sessionId)`

Skipping `pullEvent` (or updating state) before `render` leaves the queue full and the UI looks stuck. IDE/LSP reports **UI1001** for linear `dispatchEvent` → `render`/`mount` without an intervening `pullEvent`.

`ui.run(state, view, update, sessionId?)` is this loop as one call:

```malda
function update(count, e) {
    if (e["type"] == "close") {
        return null;  // ends ui.run, which returns the last state
    }
    return count + 1;
}

var last = ui.run(0, view, update, sid);
```

It mounts `view(state)`, then for each event sets `state = update(state, event)` and renders
`view(state)`. While `UiHostLink` is attached it waits on the session queue
(`UiSession.WaitForEventAsync`, no polling) and stops only when `update` returns `null`; with
no host it also stops on an empty queue, since nothing can arrive any more. The CLI and
transpiled programs always attach, so offline programs end the loop with an event that
`update` answers with `null`. In transpiled programs `update` is a two-argument delegate:
`WrapTranspiledDelegate` sets `FunctionValue.TranspiledArgsDelegate`, and built-ins call it
through `Interpreter.InvokeTranspiledFunctionAsync`. `UiHostProtocolTests.Program_AnswersBrowserClicks`
runs the counter both ways (hand-written loop and `ui.run`) on both backends.

Canonical offline example: [`Examples/Web/ui_event_loop.malda`](../Examples/Web/ui_event_loop.malda)  
Showcase: [`Examples/Web/ui_counter_dashboard.malda`](../Examples/Web/ui_counter_dashboard.malda)  
Agent-facing gotcha: [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md)

## One model per surface

| Model | Use when |
|-------|----------|
| `@PAGE` / `@AIPAGE` + HTML strings | Route-first pages (`pageLayout`, forms, redirects) |
| `ui.*` trees + `ui.mount` / `ui.render` | Server-driven component patches / UIHost |

Do not pass HTML strings into `ui.mount`/`ui.render`, or treat a `@PAGE` return value as a `UiNode` tree. Combining models in one product is fine when boundaries are explicit (e.g. marketing `@PAGE` + app shell `ui.*`). IDE reports **UI1002** (Info) when a file mixes `@PAGE`/`@AIPAGE` with `ui.mount`/`ui.render`. Chooser: [`ReferenceManual/25-web-ui-hub.html`](../ReferenceManual/25-web-ui-hub.html).

## Protocol shape (1.1)

Session envelopes (`ui.mount` → `mount`, `ui.render` → `patch`, `ui.resync` → `resync`) include
`type`, `version`, `sessionId`, `envelopeId`, `revision`, `patches[]`, and on `patch` also
`baseRevision`. `revision` versions the tree: mount and render each add one, and a patch
applies only on top of `baseRevision`.

The host (`MaldaLang.UIHost/UiHostHub.cs`, shared by the embedded and the standalone host)
wraps each session envelope as the `payload` of a socket message. Two counters, two jobs:

| Counter | Scope | Purpose |
|---------|-------|---------|
| `sequence` | one WebSocket, each direction | message order; restarts at 1 on every connection (`connected` = 1) |
| `revision` / `baseRevision` | one session | tree version; the client asks for a resync when a patch's `baseRevision` is not the revision it holds |

The hub keeps each session's tree by applying the published patches (`UiTreePatcher`), so
`resync` always answers with one root `ReplaceNode` of the current tree (payload `null` before
the first mount). The client keeps at most one resync in flight. Socket messages from the
host: `connected`, `mount`, `patch`, `resync`, `ack` (for client `event`s), `nack`, `error`,
`ping`. From the client: `event`, `resync`, `pong`. A version mismatch is reported, never
answered with a resync. Publishing: `POST /ui/mount/{sessionId}` and `/ui/patch/{sessionId}`
with the session envelope as the body; a body without `patches` or `revision` gets 400.

Patch operations: `ReplaceNode`, `SetProp`, `RemoveProp`, `InsertChild`, `RemoveChild`,
`MoveChild` (`from` → `index`: take the child out, then insert it into the shortened list).

Patches apply in order and every index is the position at that moment. Children are matched
by key when every child of both lists has a distinct non-empty `key`; otherwise by index.
The keyed diff first removes vanished keys (highest index first), then walks the new order,
moving or inserting each child into place before diffing it. `UiPatchFuzzTests` checks that
the patches rebuild the new tree in both the host's `UiTreePatcher` and `MaldaUiCore` (the pure
patch core at the top of `malda-ui-client.js`, loadable from Node). `MaldaUiProtocol`, next to
it, is the client's sequence / ack / resync state machine; `UiHostProtocolTests` drives it over a
real WebSocket against the embedded and the standalone host.

The browser applies each patch to the DOM in place (`MaldaUiDom`, also in
`malda-ui-client.js`, created with the page's `document`). The DOM mirrors the tree: the
element at `/1/0/` is child 0 of child 1 of the root, and `data-ui-path` names it.

- `SetProp` / `RemoveProp` copy a fresh render's attributes, class and style onto the live
  element, which keeps focus, caret and selection. The input's value and checked state change
  only when the patch is to `value` / `defaultValue` or `checked` / `defaultChecked`.
- `InsertChild`, `RemoveChild` and `MoveChild` insert, remove or move one DOM node and renumber
  the `data-ui-path` of the siblings after it.
- `ReplaceNode` renders that subtree again.
- `text`, `dataGrid` and `treeView` do not mirror their children in the DOM: a patch on or
  inside one renders that node again. `dataGrid` keeps scroll, sort and selection in its
  per-path control state, and a replaced node keeps its scroll position.
- Listeners read the element's current node and `data-ui-path` when the event fires, so
  patches never re-bind them.
- A snapshot, or a patch the DOM cannot follow, renders the whole tree.

`UiPatchFuzzTests` renders each fuzz case's first tree into a fake DOM, applies the patches
with `MaldaUiDom`, and requires the result, and the paths its listeners report, to equal a
fresh render of the second tree.

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

There is one host, `MaldaLang.UIHost`. `UiHostRoutes.MapUiHost` maps its surface and
`UiHostHub` runs the protocol:

- page and browser client from embedded resources (`/`, `/malda-ui-client.js`)
- WebSocket endpoint (`/ui/ws/{sessionId}`)
- publishing (`POST /ui/mount/{sessionId}`, `POST /ui/patch/{sessionId}`)
- heartbeat ping/pong and stale-connection eviction
- optional auth token (`MALDA_UI_AUTH_TOKEN`)

Two ways to run it. `UiHostLink.StartAsync()` starts it in process through
`EmbeddedUiHostRuntime.TryStartAsync()` (`MALDA_UI_HOST_URL`, default
`http://localhost:50114`) and links the sessions to it: the CLI does when a program uses
`ui.*`, the Desktop IDE for web preview, and transpiled programs through one emitted call —
the compiler references `MaldaLang.UIHost.dll` and copies it from beside `malda.dll`. Only
the in-process host sees the program's sessions; a program talks to a standalone host by
posting the envelopes itself. The
`MaldaLang.UIHost` executable is the same routes plus a CORS policy
(`MALDA_UI_ALLOWED_ORIGIN`) and a per-IP rate limit.

## Code map

| Concern | Primary path |
|---------|----------------|
| Nodes / diff / sessions / control specs | `MaldaLang/Runtime/UI/` |
| `ui.*` builtins | `MaldaLang/BuiltIns/BuiltInFunctions.Ui.cs`, `.UiCrud.cs`, `.UiGenerate.cs` |
| Session ↔ host link (publish, browser events) | `MaldaLang/Runtime/UI/UiHostLink.cs` |
| Host (routes, protocol hub, tree patcher) | `MaldaLang.UIHost/` (`UiHostRoutes`, `UiHostHub`, `UiTreePatcher`, `EmbeddedUiHostRuntime`) |
| Browser client | `MaldaLang.UIHost/wwwroot/malda-ui-client.js` |
| Language API (user docs) | `ReferenceManual/26-web-ui.html` |
