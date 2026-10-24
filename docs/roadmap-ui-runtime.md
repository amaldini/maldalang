# MALDA web UI runtime roadmap

**Status:** U0–U6 landed 2026-10-22; U7 landed 2026-10-24 (complete)  
**Created:** 2026-10-22  
**Audience:** maintainers working on `ui.*`, `MaldaLang.UIHost` and `malda-ui-client.js`

This plan fixes the server-driven UI stack end to end: the wire protocol, the host, the
browser→session event path, the browser client's DOM updates, and the host's security
defaults. The engine notes stay in [`docs/ui-framework.md`](ui-framework.md); the language
API in [`ReferenceManual/26-web-ui.html`](../ReferenceManual/26-web-ui.html).

**Decisions (2026-10-22):**

- The protocol may break backward compatibility. Nobody runs the client against another
  host yet, so `1.0` → `1.1` ships without a compatibility path.
- Security is on by default (U6): origin check, no token in query strings, refuse to listen
  off-loopback without a token.
- `ui.run` lands as a new built-in in the `ui` namespace (U3b), with the full built-in
  checklist.

---

## Problems this plan addresses

Found by reading the code on 2026-10-22; U0 turns each one into a failing test first.

1. **Sequence handshake.** The client ignores `connected` (sequence 1) and keeps expecting 1,
   while `NextServerSequence` gives a new socket sequence 2, so the first mount is rejected.
2. **Resync loop.** The host sends `resync` with `sequence = 0`; the client rejects anything
   below its expected sequence and asks for another resync.
3. **Resync payload.** The host replays the whole cached envelope (`{type, payload:{patches}}`),
   so the client treats the envelope itself as the tree. When the cached envelope is a patch,
   a reconnecting client gets a delta with no base.
4. **Three sequence counters.** Per-socket `SocketState.NextOutboundSequence`, the shared
   `NextServerSequence`, and `UiSession._nextOutSequence`.
5. **Three hosts.** `MaldaLang.UIHost/Program.cs` (sequences, ack/nack, heartbeat),
   `EmbeddedUiHostRuntime.cs` (none of that; rebroadcasts pong/ack/resync as `event`), and a
   string copy in `MaldaLang.Compiler/Compiler.UiHost.cs` emitted into transpiled programs.
6. **No event bridge.** Nothing moves browser events into `UiSession.EnqueueEvent`; programs
   only see events they inject with `ui.dispatchEvent`.
7. **Full DOM rebuild.** `schedulePatchFlush` applies patches to the in-memory tree, then
   `renderRoot()` clears `#app` and rebuilds it. Caret, selection, scroll and listeners are
   lost on every patch.
8. **Input events.** Every keystroke is an event and a server render; a late server `value`
   can roll back what the user typed; index paths can retarget an in-flight event.
9. **Security.** Token in `?token=`; CORS and WebSocket open to any origin by default;
   blacklist sanitizer in `UiNode` (misses `data:`, `vbscript:`, `srcdoc`, `formaction`,
   `style` with `url(`); `POST /ui/patch` open when no token is set.
10. **Robustness.** `treeHash` uses `string.GetHashCode()` (per-process) and the client never
    checks it; payload size reserializes the tree; event overflow drops silently;
    `Examples/Web/wwwroot/malda-ui-client.js` is a stale copy; no connection indicator.

---

## Workstreams

| ID | Workstream | Depends on | Size |
|----|------------|-----------|------|
| U0 | Reproduce protocol bugs as tests | — | S |
| U1 | Protocol 1.1: sequence + revision, snapshot resync | U0 | M |
| U2 | One host implementation | U1 | M |
| U3 | Browser → session event bridge, auto-publish | U2 | M |
| U3b | `ui.run(view, update, sessionId?)` built-in | U3 | M |
| U4 | Patch the DOM instead of rebuilding it | U1 | L |
| U5 | Input events: debounce, stale values, key-checked targets | U4 | S–M |
| U6 | Secure-by-default host | U2 | M |
| U7 | Robustness and cleanup | U1 (hash) | S |

```
U0 → U1 → U2 ─┬→ U3 → U3b
              ├→ U6
              └→ U4 → U5
U7: any time (treeHash after U1)
```

### U0 — Reproduce protocol bugs as tests

- Extract the client's protocol state machine into a DOM-free `MaldaUiProtocol` next to
  `MaldaUiCore` in `malda-ui-client.js`: message in → actions (apply / ack / resync) + state.
- `UiHostProtocolTests`: a C# side that starts the host on a random port and talks to it with
  `ClientWebSocket`, and a Node side that feeds scripted message sequences to `MaldaUiProtocol`.
- Cases: first mount after `connected`; resync sequence; resync after a patch; reconnect; two
  tabs on one session.
- Exit: the tests fail on problems 1–3. Mark them skipped with a pointer to U1 until it lands.
- **Landed:** all 10 runs (5 scenarios × embedded / standalone) fail. Embedded: the client's
  own resync and ack come back as `event` and use up sequence numbers, so every mount and
  patch is rejected and the tree stays null. Standalone: ~10k resync / ack pairs per second
  (resync sequence 0).

### U1 — Protocol 1.1

| Counter | Owner | Purpose |
|---------|-------|---------|
| `sequence` | transport, per socket | ordering and duplicates; `connected` = 1 and the client accepts it |
| `revision` / `baseRevision` | `UiSession`, per session | tree version; the client applies a patch only when `baseRevision` matches its revision, else it asks for a resync |

- `resync` carries a normal sequence and the full snapshot from `UiSession.BuildResyncEnvelope()`.
- One payload shape: the host forwards the session envelope as is, never nested in `payload`.
- Files: `UiSession.cs`, `UiProtocol.cs`, `malda-ui-client.js`, `MaldaLang.UIHost/Program.cs`.
- Tests: U0 green; `UiSessionProtocolTests`, `UiPatchFuzzTests`.
- **Landed:** `MaldaLang.UIHost/UiHostHub.cs` holds the host side for both hosts (so part of
  U2 came early): per-socket send pump and sequence, inbound gap reported but accepted,
  session tree kept with `UiTreePatcher` (the fuzz test's applier, moved into the host) for
  snapshot resyncs, heartbeat, `ClientEvent` hook for U3. The client keeps one resync in
  flight and never answers a version mismatch. Client `ack`s are gone.

### U2 — One host

- `EmbeddedUiHostRuntime` becomes the only implementation (sequences, ack/nack, heartbeat,
  payload limits move in from `Program.cs`); `Program.cs` becomes a thin standalone wrapper.
- Delete the string host in `Compiler.UiHost.cs`: transpiled programs already load
  `MaldaLang.dll`, which references `MaldaLang.UIHost`, so they call
  `MaldaLang.UIHost.EmbeddedUiHostRuntime.TryStartAsync()` directly.
- Guard test: the compiler contains no `/ui/ws` route as a string.
- Update the "Runtime Material" list in `LICENSE-RUNTIME-EXCEPTION` (emitted code changes).
- Tests: `TranspiledUiHostEmbeddingTests`, `UiHostProtocolTests`, `LicenseHeaderGuardTests`,
  `Tier0CsharpShipSmokeTests`.
- **Landed:** `UiHostRoutes.MapUiHost` is the one route table; `EmbeddedUiHostRuntime` and the
  standalone `Program.cs` (which adds CORS and the rate limit) both call it. Transpiled
  programs emit one `await global::MaldaLang.UIHost.EmbeddedUiHostRuntime.TryStartAsync();`;
  the transpile project references `MaldaLang.UIHost.dll`, staged from beside `malda.dll`,
  with a clear error when it is missing. The standalone host no longer serves `wwwroot` as
  static files; page and client come from the embedded resources like the in-process host.

### U3 — Event bridge and auto-publish

- `MaldaLang` references `MaldaLang.UIHost`, so the host cannot call `UiSessionRegistry`.
  Define `IUiSessionBridge` (`OnClientEvent(sessionId, json)`, `GetSnapshot(sessionId)`) in
  `MaldaLang.UIHost`; MaldaLang registers an implementation before `TryStartAsync`
  (`Program.Run.cs` and the transpiled startup).
- Browser events go to `UiSession.EnqueueEvent` and are no longer rebroadcast to sockets.
- `ui.mount` / `ui.render` publish their envelope when the host runs, and still return it.
- Host resync reads `GetSnapshot`; `lastEnvelopeBySession` goes away.
- Interpret / transpile parity: a UI case in `InterpretTranspilePairTests`.
- Docs: `docs/ui-framework.md` (event loop contract), `ReferenceManual/26-web-ui.html`,
  `docs/llm/malda-gotchas.md`; review whether UI1001 still earns its place.

**Landed:** no bridge interface was needed. Publishing goes the way the reference already
points: `UiHostLink` (`MaldaLang/Runtime/UI/`) calls `UiHostHub.Publish` in process and
subscribes to `UiHostHub.ClientEvent`; `UiHostLink.StartAsync()` replaces the direct
`EmbeddedUiHostRuntime.TryStartAsync()` call in the CLI, the Desktop IDE and the transpiled
startup. `UiSessionRegistry` sessions publish under the session lock, so publishes keep
revision order; hand-built `UiSession`s publish nothing. The hub keeps its own tree since U1,
so `GetSnapshot` was unnecessary. The 256 KB cap moved to the HTTP route; in-process publishes
follow the session's `maxPayloadBytes`. Browser events and `ui.dispatchEvent` share
`EnqueueUiEvent` (so `error` events fire `onError` either way). The parity case found that
transpiled `ui.dispatchEvent` rejected dict literals (`DictionaryInstance`), now coerced.
Tests: `UiHostProtocolTests` (in-process link round trip; a counter program answering two
browser clicks on interpret and transpile) and `InterpretTranspilePairTests.UiEventLoop_SameStdout`.
UI1001 stays: `ui.dispatchEvent` is still the offline and test path.

### U3b — `ui.run`

- `ui.run(view, update, sessionId?)`: mount `view(state)`, then loop pull event →
  `update(state, event)` → render, until the host stops or `update` returns a stop marker.
- Check first whether a blocking or timed `ui.pullEvent` exists; add one if `ui.run` needs it.
- Full new built-in checklist (AGENTS.md): arity, dispatch, registry + inventory, manual,
  `sync-llm-builtins-tsv.ps1`, interpret + transpile smoke.

**Landed:** the signature takes the initial state, `ui.run(state, view, update, sessionId?)`,
and the stop marker is `update` returning `null`; `ui.run` returns the last state. There was no
blocking pull, and none was added as a built-in: `ui.run` waits on `UiSession.WaitForEventAsync`
(a semaphore released by `EnqueueEvent`). Without a linked host it returns on an empty queue.
Transpiled callbacks were one-argument only (`TranspiledDelegate` passed the first argument),
so `WrapTranspiledDelegate` now also sets `FunctionValue.TranspiledArgsDelegate` and
`Interpreter.InvokeTranspiledFunctionAsync` calls it. The interpreter routes `ui.run` through
its async built-in path like `AnsiConsole.status`. Tests: `InterpretTranspilePairTests.UiRun_SameStdout`
and `UiHostProtocolTests.Program_AnswersBrowserClicks` (loop and `ui.run`, interpret and transpile).

### U4 — Patch the DOM

- Keep a path → element map. `SetProp` / `RemoveProp` touch one attribute or listener;
  `InsertChild` / `RemoveChild` / `MoveChild` use `insertBefore` / `remove` and renumber the
  siblings' `data-ui-path`; only `ReplaceNode` rebuilds a subtree.
- Listeners read path and props at event time, not from the closure that created them.
- `dataGrid` / `treeView` stay self-rendering but keep scroll and selection.
- Tests: extend `UiPatchFuzzTests` with a minimal fake DOM in Node; DOM after patches must
  equal a fresh render. Explore with `MALDA_UI_FUZZ_SEEDS=1-200`.
- Manual check: `Examples/Web/ui_form_workflow.malda` — typing keeps caret and selection.

**Landed:** no path → element map: the DOM mirrors the tree, so a path walks `childNodes`.
The renderer moved into `MaldaUiDom.create(document, hooks)` so Node can run it on a fake DOM.
`SetProp` / `RemoveProp` sync a fresh render's attributes onto the live element instead of
setting one attribute, which keeps every prop rule in one place; value and checked change only
for their own props (the headless-browser check caught a class patch overwriting typed text).
Text, `dataGrid` and `treeView` re-render on any patch on or inside them; `dataGrid` local sort
re-renders only the grid, not the page. Tests: `UiPatchFuzzTests` compares the patched fake DOM
and its listener paths with a fresh render (200 seeds clean), and
`DomPatch_KeepsTheEditedInputAndItsListener`. Checked in headless Edge with the real client:
focus, typed value and selection survive sibling patches, inserts, moves and class changes.

### U5 — Input events

- `inputDebounceMs` prop (default ~150 ms for `textField` / `textArea`), coalescing
  consecutive `input` events per path; register it in `UiControlSpecRegistry`.
- Per-input local edit counter: a server `value` overwrites a focused input only when it
  answers the latest event sent.
- Events carry the target node's `key`; the server drops an event whose path and key no
  longer match.
- **Landed:** the debounce lives in `MaldaUiDom` (`hooks.setTimeout` so Node tests drive a
  manual clock) and any non-input event flushes the waiting inputs first, so a click never
  overtakes the typing before it. The edit counter became event ids: the session sets
  `answersEventId` on each envelope to the last event pulled, and the client holds back a
  `value` / `checked` patch to an element whose edits that envelope has not answered, then
  applies it once they are. The rule holds whether or not the input has focus. Events carry
  `targetType` as well as `targetKey`, so unkeyed nodes are checked too; the check runs when the
  program pulls the event, against the tree at that moment, and counts drops in
  `staleEventsDropped`. `treeView` items send no target (their paths are not tree paths).
  Tests: `UiPatchFuzzTests.DomInput_*`, `UiSessionProtocolTests.Session_*Target*` /
  `Session_RenderAnswersTheLastPulledEvent`, and the linked-session test in `UiHostProtocolTests`.
  Not covered: a full render still replaces a focused input with the server value.

### U6 — Secure-by-default host

- WebSocket `Origin` check: same origin and loopback by default; `MALDA_UI_ALLOWED_ORIGIN`
  extends the list. CORS follows the same list.
- `POST /ui/ticket` with the token returns a short-lived single-use ticket used as
  `?ticket=`; the token never goes in a query string.
- The host refuses to listen off-loopback without `MALDA_UI_AUTH_TOKEN`.
- `UiNode`: allowlist instead of blacklist — URL schemes `http`, `https`, `mailto`, `tel`
  and relative; reject `srcdoc`, `formaction`, unknown `on*`; filter `style` for `url(` /
  `expression(`; per-control attributes from `UiControlSpecRegistry`.
- Tests: an XSS payload table in `UiFrameworkTests`; host tests for foreign Origin and
  expired ticket.
- **Landed:** `UiHostSecurity` (`MaldaLang.UIHost`) holds the rules for both hosts. The Origin
  check covers the `POST` routes too (a cross-origin `text/plain` POST needs no preflight);
  requests without `Origin` pass, as they come from no page. Tickets live 30 s and are not bound
  to a session (the token opens every session anyway). The browser takes the token from
  `#token=`, keeps it in `sessionStorage` and asks for a ticket before every connect; a 401 /
  403 stops the reconnect loop. The standalone host checks the configured URLs before
  listening and the bound addresses after (`ASPNETCORE_HTTP_PORTS`, Kestrel endpoints). Props
  went to `UiPropSanitizer`; per-control rules apply to URL props only (`image.src`,
  `form.action`), because examples pass props the specs do not list (`gap`, `level`,
  `required`, …) and the client renders them as attributes. The old `<script` value check is
  gone: the client never uses `innerHTML`, and it dropped legitimate text. The client applies the
  same attribute and style rules, since a standalone host forwards posted trees as they are.
  Tests: `UiHostSecurityTests` (Origin table, listed origin, foreign-origin POST, token / ticket
  flow on a standalone host with a token, expiry, listen URLs, refusal off loopback) and
  `UiFrameworkTests.UiNode_KeepsOnlySafeProps`. Checked in headless Edge: the fragment leaves
  the address bar, the ticket opens the socket, a wrong token stops after one 401.

### U7 — Robustness and cleanup

- Stable `treeHash` (FNV-1a over canonical JSON) in C# and `MaldaUiCore`; mismatch → resync;
  the fuzz checks both hashes agree.
- Measure payload size once on the serialized JSON.
- Event-queue overflow: `droppedEvents` in `ui.snapshot` and an `overflow` event.
- `Examples/Web/wwwroot/malda-ui-client.js`: copy at build time or guard it against the
  canonical file.
- Client: connection state on `#app` (`data-ui-connection` + default CSS) and `?debug=1`
  protocol logging.
- **Landed:** `UiTreeHash` (`MaldaLang.UIHost`) hashes for the session (`UiNode.Hash`, written
  straight from the tree, no reserialization) and for the hub's resync; `MaldaUiCore.treeHash`
  matches it (JavaScript number forms, `JSON.stringify` escapes; a lone surrogate counts as
  U+FFFD, as the envelope serializer sends it). `MaldaUiProtocol.verifyTree` asks for a resync on
  a mismatch, and after a resync only reports it. The envelope is serialized once, for the size
  check and the publish. A full queue drops the oldest event; the next pull is an `overflow`
  event (`{ dropped }`) and `ui.snapshot` reports `droppedEvents`. The stale Examples copy is
  deleted and `UiFrameworkTests.UiClient_HasNoStaleCopies` guards against new ones. `#app` carries
  `data-ui-connection` (`connecting` / `open` / `reconnecting` / `closed`, badge for the last
  two); `?debug=1` logs messages with `console.debug`. Tests: `UiPatchFuzzTests` (hash on every
  case, `TreeHash_IsTheSameOnSessionHostAndClient`), `UiSessionProtocolTests.Session_FullQueue*` /
  `Session_TreeHash*`, `UiHostProtocolTests` (tabs verify every envelope's hash).

---

## Tests per workstream

`UiHostProtocolTests`, `UiSessionProtocolTests`, `UiPatchFuzzTests`, `UiFrameworkTests`,
`TranspiledUiHostEmbeddingTests`; `ReferenceManual` when docs change;
`BuiltInSurfaceGuardTests` for U3b.
