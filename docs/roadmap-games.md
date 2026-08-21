# MALDA games platform plan

**Status:** G0 landed (this file); G1–G9 not started  
**Created:** 2026-08-21  
**Audience:** maintainers extending the JS `game.*` / `three.*` surface after Final 1.0

This is the forward plan for making MALDA a **good platform for games people
finish** — Love2D / Pico-8 / Phaser, not Unity. It is not a wishlist and not
a substitute for [`docs/javascript-backend.md`](javascript-backend.md) or
[Reference Manual 26](../ReferenceManual/26-browser-javascript-backend.html).

**Bar today:** JS-only canvas loop (`fillRect` / `fillCircle` / `drawText` /
pixel blit), keyboard+mouse (touch aliased to the left button), Audio Spec v1
(synth + one music track), curated `three.*` primitives + `@shader()`.
Examples (`game_bounce`, `maldanoid`, `maldadash`, `three_cube`) already hit
that ceiling: `maldadash` paints every tile with `fillRect`; `three.*` docs
state there are no textures, models, or orbit controls.

**Not in scope:** new syntax / keywords / `on update`, interpreter or C# game
loops, a Box2D-class physics engine in core, a second 2D renderer (Pixi /
WebGL batcher) before Canvas2D images work, native SDL/Raylib (optional pack
**out of tree**), new top-level globals, Web IDE Desktop parity, product apps
or vertical packs (`AGENTS.md`).

---

## Guiding principles

1. **JavaScript backend is the product path for games.** Capability tag
   `game-canvas` stays JS-only
   ([`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md)).
   Do not add host stubs that pretend to run `game.start` in the interpreter.
2. **Extend `game.*` and `three.*`.** Stdlib is soft-frozen: deepen existing
   namespaces; do not add `sprite.*` or flat `drawImage()`.
3. **Functions beat keywords.** If it can be a runtime helper, it does not
   enter the parser ([`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md)).
4. **One focused PR per workstream.** Land G1 before rewriting `maldadash` to
   use sprites (that is G7).
5. **JS-only registration, not the host builtin checklist.** `game.*` is not
   in `BuiltInRegistry`. New names land in `malda-js-runtime.js` + chapter 26 +
   `JavaScriptBackendTests`. `JsTranspiler` already routes every `game.*` /
   `three.*` member; do not special-case each new call.
6. **Lean on MALDA where engines are weak.** `@client` / `@server`, `schema`
   / `validate()`, host prompts, and PWA are the differentiator — after the 2D
   kit exists.

---

## Themes and priority

| Rank | Workstream | Why | Syntax? |
|------|------------|-----|---------|
| 0 | **G0** Roadmap file | One place to track this work (this document) | No |
| 1 | **G1** Sprites, camera, draw extras | `fillRect` is the 2D ceiling; platformers need images + a camera | No |
| 2 | **G2** Input edges, gamepad, touch | `isKeyDown` cannot express “just jumped”; touch is a mouse lie | No |
| 3 | **G3** Collision helpers | AABB / circle queries cut most of `maldanoid` boilerplate | No |
| 4 | **G4** Sample SFX | Audio v1 is tones + one track; games need overlapping file hits | No |
| 5 | **G5** Fixed timestep + save/load | Every example reclamps `dtMs`; high scores have no API | No |
| 6 | **G6** `malda new game` + `malda play` | No game scaffold; compile+host HTML is a three-file ritual | No |
| 7 | **G7** Showcase that uses G1–G5 | Prove the kit with one game that is not all `fillRect` | No |
| 8 | **G8** `three.*` textures, glTF, look-at | 3D games stay colored boxes without assets | No |
| 9 | **G9** Full-stack scores template | MALDA-specific: client loop + server authority + schema | No |

```text
G0  roadmap file
  └─ G1  sprites / camera / draw extras
       ├─ G2  input edges / gamepad / touch     (after or parallel with G1)
       ├─ G3  overlap helpers                   (parallel)
       ├─ G4  audioPlaySample                   (parallel; keep Audio v1)
       └─ G5  startFixed + save/load            (parallel)
            └─ G6  malda new game + malda play  (template may start on G1)
                 └─ G7  showcase example
G8  three.* assets                              (independent of G1–G7)
G9  fullstack scores                            (after G6; uses @client/@server)
```

G2–G5 must not block each other. G7 must not start until G1 + G3 are in.
G6 may ship a bounce-style template on G1 alone, then pick up G2–G5.

---

## JS-only landing checklist (every G1–G5 / G8 API PR)

Host builtin steps 1–5 (`BuiltInFunctions` / `CallBuiltIn` / `IsBuiltIn` /
C# transpiler) **do not apply**. Do this instead:

1. Implement on `mlRuntime.game` or `mlRuntime.three` in
   [`Examples/Web/wwwroot/malda-js-runtime.js`](../Examples/Web/wwwroot/malda-js-runtime.js)
2. Add a `JsTranspiler_Maps…` codegen test **and** a `GameRuntime_…` (or
   `ThreeRuntime_…`) Node harness in
   [`MaldaLang.Tests/JavaScriptBackendTests.cs`](../MaldaLang.Tests/JavaScriptBackendTests.cs)
   — same fake-canvas pattern as `GameRuntime_SetPixelAndBlitPixels_WritesImageData`
3. Name every new call in
   [`ReferenceManual/26-browser-javascript-backend.html`](../ReferenceManual/26-browser-javascript-backend.html)
   (and [`ReferenceManual/it/26-…`](../ReferenceManual/it/26-browser-javascript-backend.html))
4. Document contract + guardrails in [`docs/javascript-backend.md`](javascript-backend.md)
5. Small `.malda` smoke under `Examples/Games/` (or extend an existing host
   HTML); do not hand-edit generated `.js`
6. Filtered test: `dotnet test MaldaLang.Tests --filter "FullyQualifiedName~JavaScriptBackendTests"`

Do **not** add interpreter/`CSharpTranspiler` cases. Property tests that need
the canvas stay tagged `game-canvas`.

---

## G0 — Roadmap file

**Done when:** this file exists; [`docs/architecture.md`](architecture.md)
Docs layout links here; [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) has a
PATCH tracking-only row.

---

## G1 — Sprites, camera, draw extras

**Done when:** a MALDA program can load a PNG, blit atlas frames, scroll a
camera, and draw a line, without per-tile `fillRect`.

| Call | Contract |
|------|----------|
| `game.loadImage(url)` | Returns an opaque handle immediately. Decode is async; draws no-op until ready. Missing URL / decode failure: handle stays unready (no throw on the hot path). |
| `game.imageIsReady(handle)` | `true` when the bitmap can be drawn. |
| `game.drawImage(handle, x, y, w?, h?)` | Destination size defaults to image size. Camera applies. |
| `game.drawImageRect(handle, sx, sy, sw, sh, dx, dy, dw?, dh?)` | Atlas source rect. `dw`/`dh` default to `sw`/`sh`. |
| `game.drawLine(x1, y1, x2, y2, color?, width?)` | Stroke; default color `#ffffff`, width `1`. |
| `game.strokeRect(x, y, w, h, color?, width?)` | |
| `game.setAlpha(a)` | Clamp `[0, 1]`; applies to subsequent draws until changed. |
| `game.setCamera(x, y)` | World origin in screen space; subsequent draws subtract camera. Default `(0, 0)`. |
| `game.getCameraX()` / `game.getCameraY()` | |

**Guardrails**

- Call `createCanvas` before image/draw/camera APIs (same as `fillRect`).
- Do not block `game.start` on images; the loop runs, unready draws skip.
- Camera does **not** affect `setPixel` / `blitPixels` (those stay buffer-local).
- No sprite *objects* in the runtime — handles + draw calls only.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`, a tiny
`Examples/Games/game_sprite_smoke.malda` + host HTML (checker PNG or
existing example asset). Optional: reuse in `maldadash` is **G7**, not this PR.

---

## G2 — Input edges, gamepad, touch

**Done when:** jump/menu code can use “pressed this tick”, a gamepad moves a
paddle, and a touch is not only `isMouseDown(0)`.

| Call | Contract |
|------|----------|
| `game.wasKeyPressed(key)` | `true` on the first update after key-down. Same key names as `isKeyDown` (`"arrowleft"`, `" "`, `"r"`). |
| `game.wasKeyReleased(key)` | First update after key-up. |
| `game.getTouches()` | Array of `{ id, x, y }` in **canvas** pixels (same scale as `getMouseX`). Empty when none. |
| `game.isGamepadConnected(index?)` | Default index `0`. |
| `game.getGamepadAxis(index, axis)` | Standard mapping; missing device → `0`. |
| `game.isGamepadButtonDown(index, button)` | |
| `game.wasGamepadButtonPressed(index, button)` | Edge, same clock as keys. |

**Guardrails**

- Snapshot previous vs current at the start of `update` (or the combined
  update+render tick). Edges are **false** if sampled from `render` after
  they were consumed — document “read input in `update` only”.
- Keep mapping first touch → mouse for existing games (`game_bounce`).
  `getTouches()` is additive.
- No-op / zeros when the Gamepad API is missing (Node harness).
- `game.stop()` still clears key and button sets (today’s behavior).

**Files:** runtime input listeners (keys already on `window`; add
`gamepadconnected` + per-frame `navigator.getGamepads()` poll), chapter 26.9,
tests that fake `keydown`/`keyup` around one `update` tick.

---

## G3 — Collision helpers

**Done when:** overlap tests are one call, with tests for touching edges and
zero-size rects.

| Call | Contract |
|------|----------|
| `game.overlapRect(x1, y1, w1, h1, x2, y2, w2, h2)` | Inclusive AABB; `w`/`h` ≤ 0 → `false`. |
| `game.overlapCircle(x1, y1, r1, x2, y2, r2)` | `r` ≤ 0 → `false`. |
| `game.pointInRect(px, py, x, y, w, h)` | |
| `game.pointInCircle(px, py, x, y, r)` | |

These are pure functions (no canvas required) so the Node harness does not
need a fake 2D context. Keep them on `game.*` for discoverability, not
`math.*`.

**Out of scope here:** swept AABB, tileset collision, rigid-body physics.

---

## G4 — Sample SFX

**Done when:** two overlapping WAV/OGG one-shots can play without stopping
the Audio v1 track or pattern.

| Call | Contract |
|------|----------|
| `game.audioPlaySample(url, volume?, options?)` | `volume` default `1`, clamp `[0, 1]`. Options: `{ loop?: bool }` (default false). Returns `null`. Decode once per URL and cache. Autoplay-blocked: no-op until `audioInit` succeeds (same as v1). |
| `game.audioStopSample(url?)` | Omit URL → stop all samples. Does not stop the v1 track/pattern. |

**Guardrails**

- Audio Spec v1 signatures stay backward compatible
  ([`docs/javascript-backend.md`](javascript-backend.md) § Audio Spec v1).
- Shared node cap still applies (today’s runaway-SFX limit).
- `game.stop()` still does **not** implicit-stop audio.

**Files:** runtime audio graph, chapter 26.9 (English lists play/stop all;
add sample + the v1 track methods that the English list still omits), a
short beep asset under `Examples/Games/` or reuse
`Examples/Web/wwwroot/audio/`.

---

## G5 — Fixed timestep + save/load

**Done when:** a game can step at a fixed tick without copying the `dt > 50`
clamp, and a high score survives reload.

| Call | Contract |
|------|----------|
| `game.startFixed(updateFn, renderFn?, tickMs?)` | Default `tickMs` = `1000 / 60`. Accrue `dtMs`, call `update(tickMs)` zero or more times per rAF, then `render` once. Cap spiral-of-death (e.g. max 5 updates per frame). Mutually exclusive with `game.start` — same “already running” error. |
| `game.save(key, value)` | JSON to `localStorage` under a `malda.game.` prefix. Non-JSON values: coerce via existing `toJSON` / `coerceToString` rules. Quota / missing `localStorage`: no-op, no throw. |
| `game.load(key)` | Parsed JSON, or `null` if missing/corrupt. |
| `game.removeSave(key)` | |

`TargetPartitioner` already lists `localStorageGet` / `Set` as client-only
names; those are **not** a public MALDA API today. Prefer `game.save` over
resurrecting those identifiers.

**Guardrails**

- `startFixed` uses the same canvas/input lifecycle as `start`.
- Saves are origin-scoped (browser), not files. Document that.

---

## G6 — `malda new game` + `malda play`

**Done when:** a newcomer can scaffold and play a canvas game without writing
a host HTML file.

| Piece | Contract |
|-------|----------|
| `malda new game [directory]` | Third template beside `webapi` / `fullstack`. Emits `app.malda`, `index.html` (runtime + compiled script load order), `README.md`, optional `assets/`. Next-step text: `malda play app.malda`. |
| `malda play <file.malda>` | `compile --mode js` into a temp or sibling preview dir, copy `malda-js-runtime.js`, write/serve a host page, print a local URL. `--open` may launch the default browser when the OS allows. Ctrl+C stops the server. |

**Files:** [`MaldaLang/Scaffolding/TemplateScaffolder.cs`](../MaldaLang/Scaffolding/TemplateScaffolder.cs)
(`SupportedTemplates`), [`NewCommandOptions.cs`](../MaldaLang/Scaffolding/NewCommandOptions.cs)
help text, [`MaldaLang/Program.cs`](../MaldaLang/Program.cs) `new` / new
`play` command, `Templates/game/`, filtered scaffolder tests, `docs/start-here.md`
path “Build a browser game”.

PWA remains `malda compile --mode pwa` for itch.io-style folders; `play` is
the inner loop, not a second packaging format.

**Out of scope:** Web IDE playground parity with Desktop F5 (separate; do not
block G6).

---

## G7 — Showcase that uses the kit

**Done when:** one featured example uses images, camera, AABB, key edges,
and a sample SFX — and still compiles with `--mode js`.

- New file, e.g. `Examples/Games/malda_platform.malda` (short side-scroller
  or top-down), **or** a measured `maldadash` increment that draws a tileset
  via `drawImageRect` instead of per-cell `fillRect`.
- Host HTML + `metadata.json` catalog entry.
- Do not delete Bounce / Maldanoid; they remain the primitive-draw track.
- Transpile smoke: extend `JsTranspiler_*Example_Emits…` like `maldadash`.

Pick the smaller of (a) tileset `maldadash` and (b) a new 150–250 line
platformer. Prefer (a) if G1 atlases make the existing cave cheaper.

---

## G8 — `three.*` textures, glTF, look-at

**Done when:** a textured mesh loads from a URL and a camera can `lookAt` a
point. Independent of G1–G7.

| Call | Contract |
|------|----------|
| `three.createTexture(url)` | Handle; material no-ops map until loaded (same async story as G1). |
| `three.createStandardMaterial` | Accept `"map"` (texture handle) beside `"color"` / `"roughness"` / `"metalness"`. |
| `three.loadGLTF(url)` | Returns a group handle immediately; `three.modelIsReady(handle)` when the scene can be `add`ed. Failures stay unready. |
| `three.lookAt(object, x, y, z)` | Requires `lookAt` on the three.js object. |
| `three.setTexture` / orbit controls | **Not this workstream.** Orbit stays out until look-at + one example prove the gap. |

Keep the curated wrapper (no raw `THREE.*` in MALDA source). Host pages still
load `three.min.js` first; GLTF needs a documented extra vendor script
(`GLTFLoader`) or a bundled helper on `mlRuntime.three` — prefer **one**
runtime-owned loader so examples do not grow a fourth `<script>`.

**Files:** runtime `three` IIFE, chapter 26.10 (replace the “no textures,
model loading, orbit controls” sentence), `Examples/Games/three_textured.malda`
or extend `three_cube`.

---

## G9 — Full-stack scores template

**Done when:** `malda new game --fullstack` (or `malda new game-fullstack`)
emits `@client()` canvas + `@GET` / `@POST` scores + a `schema` for the save
blob, and `docs/start-here.md` links it.

- Client: G1/G2/G5 loop, `http.post` for scores.
- Server: in-memory or SQLite list, `validate("Score", …)`.
- Host prompts / LLM NPCs stay **optional commentary** in the README, not
  required to run (games must work offline).

Depends on G6 scaffolding patterns. Do not invent a fourth `malda new`
family if a flag on `game` is enough.

---

## Explicit non-goals (revisit only with a new roadmap)

| Idea | Why not now |
|------|-------------|
| Interpreter / C# `game.start` | AST walk will not hit 60 Hz; matrix already says n/a |
| Native desktop window (SDL, Raylib, Silk.NET) | Optional pack **out of this repo** |
| Box2D / tile collision / particles / scene graph in core | Helpers first (G3); engines later as a pack |
| WebGL 2D sprite batcher | Canvas2D images (G1) first |
| New keywords (`on update`, `entity`) | Violates construct-plan principle 5 |
| Flat aliases (`drawImage()`) | Deprecated; coverage/style guards |
| JS ↔ host actor bridging for multiplayer | JS actors are process-local; G9 uses HTTP |
| Rewriting Desktop IDE into a game editor | Out of scope |

---

## Suggested PR slices

Ship in this order unless a slice is blocked:

1. **G0** — this document (docs only)
2. **G1** — runtime + smoke example
3. **G3** — small pure-function PR (can land beside G1)
4. **G2 + G4 + G5** — three small PRs, any order
5. **G6** — CLI + `Templates/game`
6. **G7** — showcase
7. **G8** — 3D assets (any time after G0)
8. **G9** — fullstack template

Each MINOR runtime slice updates chapter 26 and `docs/javascript-backend.md`
in the same PR. Spec bump: JS `game.*` is product/Tier-2-ish relative to
Tier 0; treat new names as **MINOR** additive on the JS backend and add a
CHANGELOG Unreleased row (product / docs), not a Tier 0 conformance case.

---

## Related

- JS backend contract: [`docs/javascript-backend.md`](javascript-backend.md)
- Routing note: [`docs/js-game-api-design.md`](js-game-api-design.md)
- Capability matrix: [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md)
- Examples: [`Examples/Games/README.md`](../Examples/Games/README.md)
- Language constructs (do not add game syntax there): [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md)
