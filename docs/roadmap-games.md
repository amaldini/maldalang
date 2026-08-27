# MALDA games platform plan

**Status:** G0–G17 landed  
**Created:** 2026-08-21  
**Audience:** maintainers extending the JS `game.*` / `three.*` surface after Final 1.0

This is the plan that made MALDA a **good platform for games people finish** —
Love2D / Pico-8 / Phaser, not Unity. G0–G17 are landed. Prefer
[`docs/javascript-backend.md`](javascript-backend.md),
[Reference Manual 26](../ReferenceManual/26-browser-javascript-backend.html),
and the deferred list at the end for engines that stay out of core.

**Bar today:** JS-only canvas kit — images / atlas blit / camera / draw extras,
key edges + gamepad + real touches, AABB / circle helpers, Audio Spec v1 plus
overlapping file samples, `startFixed` + origin `localStorage` save, `malda new
game` / `malda play`, curated `three.*` primitives + textures / glTF / `lookAt`
+ `@shader()`. Featured 2D showcase: `malda_platform`. Canvas + score API:
`malda new game --fullstack`. Primitive-draw track (`game_bounce`, `maldanoid`)
stays. Post-kit 2D: `sweepRect` / `sweepRects`, `drawImageEx`, camera stack /
zoom / `followCamera`, G10 size queries (`imageWidth`, `getCanvasWidth`,
`measureText`), `setPixelated`, `strokeCircle`, sample pan / `playbackRate`,
gamepad release + analog deadzone, `malda new game` on `startFixed`, G16
image `tint` / `tintFill` plus `setBlend` (`alpha` / `add` / `multiply` /
`screen`), and G17 tile helpers (`drawTiles` / `tileAt` / `sweepTiles`).

**Not in scope:** new syntax / keywords / `on update`, interpreter or C# game
loops, a Box2D-class physics engine in core, a second 2D renderer (Pixi /
WebGL batcher), native SDL/Raylib (optional pack **out of tree**), new
top-level globals, Web IDE Desktop parity, product apps or vertical packs
(`AGENTS.md`).

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
4. **One focused PR per workstream.** G1–G9 shipped that way; keep future
   slices the same size.
5. **JS-only registration, not the host builtin checklist.** `game.*` is not
   in `BuiltInRegistry`. New names land in `malda-js-runtime.js` + chapter 26 +
   `JavaScriptBackendTests`. `JsTranspiler` already routes every `game.*` /
   `three.*` member; do not special-case each new call.
6. **Lean on MALDA where engines are weak.** `@client` / `@server`, `schema`
   / `validate()`, host prompts, and PWA are the differentiator — G9 is the
   first scores template; prompts / LLM NPCs stay optional.

---

## Themes and priority

| Rank | Workstream | Status | Why |
|------|------------|--------|-----|
| 0 | **G0** Roadmap file | Landed | One place to track this work (this document) |
| 1 | **G1** Sprites, camera, draw extras | Landed | `fillRect` was the 2D ceiling; platformers need images + a camera |
| 2 | **G2** Input edges, gamepad, touch | Landed | `isKeyDown` cannot express “just jumped”; touch was a mouse lie |
| 3 | **G3** Collision helpers | Landed | AABB / circle queries cut most of `maldanoid` boilerplate |
| 4 | **G4** Sample SFX | Landed | Audio v1 is tones + one track; games need overlapping file hits |
| 5 | **G5** Fixed timestep + save/load | Landed | Every example reclamped `dtMs`; high scores had no API |
| 6 | **G6** `malda new game` + `malda play` | Landed | No game scaffold; compile+host HTML was a three-file ritual |
| 7 | **G7** Showcase that uses G1–G5 | Landed | Prove the kit with one game that is not all `fillRect` |
| 8 | **G8** `three.*` textures, glTF, look-at | Landed | 3D games stayed colored boxes without assets |
| 9 | **G9** Full-stack scores template | Landed | MALDA-specific: client loop + server authority + schema |
| 10 | **G10** Size queries, pixelated, `strokeCircle` | Landed | Atlas games hardcoded 16×16; pixel art needed host CSS; no stroke twin of `fillCircle` |
| 11 | **G11** Multi-obstacle sweep | Landed | `malda_platform` copies a loop over `sweepRect`; still a helper, not physics |
| 12 | **G12** Camera follow clamp | Landed | Every camera game copies 8 lines of min/max; not a camera object |
| 13 | **G13** Sample pan / playbackRate | Landed | SFX are volume-only; spatial hits and chip-tune pitch need options |
| 14 | **G14** Gamepad completeness | Landed | `wasGamepadButtonReleased` missing; analog sticks have no deadzone |
| 15 | **G15** Starter uses `startFixed` | Landed | `malda new game` still emits `game.start`; the kit loop is `startFixed` |
| 16 | **G16** Tint + blend | Landed | Highest remaining kit gap after G15: hit-flash, additive sparks, night multiply |
| 17 | **G17** Tile helpers | Landed | Pico-8 `map`/`mget` hole; `maldadash` was `fillRect` cells with no `drawTiles` / `sweepTiles` |

```text
G0  roadmap file                          (landed)
  └─ G1  sprites / camera / draw extras   (landed)
       ├─ G2  input edges / gamepad / touch
       ├─ G3  overlap helpers
       ├─ G4  audioPlaySample
       └─ G5  startFixed + save/load
            └─ G6  malda new game + malda play
                 └─ G7  showcase example  (malda_platform)
G8  three.* assets                        (landed; independent of G1–G7)
G9  fullstack scores                      (landed; after G6)
G10 query / pixel / strokeCircle          (landed; post-kit 2D)
  └─ G11 sweepRects
       ├─ G12 camera follow clamp
       ├─ G13 sample pan / rate
       ├─ G14 gamepad released + deadzone
       └─ G15 malda new game uses startFixed
G16 tint + setBlend                    (landed; post-kit 2D juice)
G17 tile helpers                       (landed; Pico-8 map/mget)
```

G11–G17 landed as the ranked 2D slices after G10 — not Tiled/LDtk, particles, or a
physics engine (those stay in **After G9 (deferred)** and **Explicit non-goals**).

---

## JS-only landing checklist (every future `game.*` / `three.*` API PR)

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

**Landed:** this file; [`docs/architecture.md`](architecture.md) Docs layout
links here; [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) has the tracking
rows.

---

## G1 — Sprites, camera, draw extras

**Landed:** `Examples/Games/game_sprite_smoke.malda`. A MALDA program can load
a PNG, blit atlas frames, scroll a camera, and draw a line, without per-tile
`fillRect`.

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

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`. Showcase reuse
is **G7** (`malda_platform`), not a `maldadash` rewrite.

---

## G2 — Input edges, gamepad, touch

**Landed:** `Examples/Games/game_input_smoke.malda`. Jump/menu code can use
“pressed this tick”, a gamepad moves a paddle, and a touch is not only
`isMouseDown(0)`.

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

**Files:** runtime input listeners (keys on `window`; `gamepadconnected` +
per-frame `navigator.getGamepads()` poll), chapter 26.9, tests that fake
`keydown`/`keyup` around one `update` tick.

---

## G3 — Collision helpers

**Landed:** `Examples/Games/game_collision_smoke.malda`. Overlap tests are one
call, with tests for touching edges and zero-size rects.

| Call | Contract |
|------|----------|
| `game.overlapRect(x1, y1, w1, h1, x2, y2, w2, h2)` | Inclusive AABB; `w`/`h` ≤ 0 → `false`. |
| `game.overlapCircle(x1, y1, r1, x2, y2, r2)` | `r` ≤ 0 → `false`. |
| `game.pointInRect(px, py, x, y, w, h)` | |
| `game.pointInCircle(px, py, x, y, r)` | |

These are pure functions (no canvas required) so the Node harness does not
need a fake 2D context. Keep them on `game.*` for discoverability, not
`math.*`.

**Out of scope here:** tileset collision, rigid-body physics
(see After G9). Swept AABB landed later as `game.sweepRect` (post-kit helper).

---

## G4 — Sample SFX

**Landed:** `Examples/Games/game_audio_sample_smoke.malda`. Two overlapping
WAV/OGG one-shots can play without stopping the Audio v1 track or pattern.

| Call | Contract |
|------|----------|
| `game.audioPlaySample(url, volume?, options?)` | `volume` default `1`, clamp `[0, 1]`. Options: `{ loop?: bool }` (default false). Returns `null`. Decode once per URL and cache. Autoplay-blocked: no-op until `audioInit` succeeds (same as v1). |
| `game.audioStopSample(url?)` | Omit URL → stop all samples. Does not stop the v1 track/pattern. |

**Guardrails**

- Audio Spec v1 signatures stay backward compatible
  ([`docs/javascript-backend.md`](javascript-backend.md) § Audio Spec v1).
- Shared node cap still applies (today’s runaway-SFX limit).
- `game.stop()` still does **not** implicit-stop audio.

**Files:** runtime audio graph, chapter 26.9, beep asset under
`Examples/Games/` or `Examples/Web/wwwroot/audio/`.

---

## G5 — Fixed timestep + save/load

**Landed:** `Examples/Games/game_fixed_save_smoke.malda`. A game can step at a
fixed tick without copying the `dt > 50` clamp, and a high score survives
reload.

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

**Landed:** `Templates/game/`. A newcomer can scaffold and play a canvas game
without writing a host HTML file.

| Piece | Contract |
|-------|----------|
| `malda new game [directory]` | Third template beside `webapi` / `fullstack`. Emits `app.malda`, `index.html` (runtime + compiled script load order), `README.md`, optional `assets/`. Next-step text: `malda play app.malda`. |
| `malda play <file.malda>` | `compile --mode js` into a sibling `.malda-play/` dir, copy `malda-js-runtime.js` and `assets/` when present, write/serve a host page, print a local URL. `--open` may launch the default browser when the OS allows. Ctrl+C stops the server. Refuses fullstack sources. |

**Files:** [`MaldaLang/Scaffolding/TemplateScaffolder.cs`](../MaldaLang/Scaffolding/TemplateScaffolder.cs)
(`SupportedTemplates`), [`NewCommandOptions.cs`](../MaldaLang/Scaffolding/NewCommandOptions.cs)
help text, [`MaldaLang/Program.cs`](../MaldaLang/Program.cs) `new` / `play`
command, `Templates/game/`, filtered scaffolder tests, `docs/start-here.md`
path “Build a Browser Game”.

PWA remains `malda compile --mode pwa` for itch.io-style folders; `play` is
the inner loop, not a second packaging format.

**Out of scope:** Web IDE playground parity with Desktop F5 (separate; still
deferred).

---

## G7 — Showcase that uses the kit

**Landed:** `Examples/Games/malda_platform.malda` — short side-scroller that
uses images, camera, AABB, key edges, sample SFX, and `startFixed`, and still
compiles with `--mode js`.

- Host HTML + `metadata.json` catalog entry.
- Bounce remains the primitive-draw `game.start` loop. Maldanoid stays
  primitive-draw (no sprites) but now uses G2/G3/G5 helpers. `maldadash` stays a
  tile cave on `fillRect` (the cheaper G1-atlas rewrite was not taken).
- Transpile smoke: `JsTranspiler_*Example_Emits…` like `maldadash`.

---

## G8 — `three.*` textures, glTF, look-at

**Landed:** `Examples/Games/three_textured.malda`. A textured mesh loads from
a URL and a camera can `lookAt` a point. Independent of G1–G7.

| Call | Contract |
|------|----------|
| `three.createTexture(url)` | Handle; material no-ops map until loaded (same async story as G1). |
| `three.createStandardMaterial` | Accept `"map"` (texture handle) beside `"color"` / `"roughness"` / `"metalness"`. |
| `three.loadGLTF(url)` | Returns a group handle immediately; `three.modelIsReady(handle)` when the scene can be `add`ed. Failures stay unready. |
| `three.lookAt(object, x, y, z)` | Requires `lookAt` on the three.js object. |
| `three.setTexture` / orbit controls | **Not this workstream.** Still deferred until look-at + the textured example prove the gap. |

Keep the curated wrapper (no raw `THREE.*` in MALDA source). Host pages still
load `three.min.js` first; GLTF uses a runtime-owned loader so examples do
not grow a fourth `<script>`.

**Files:** runtime `three` IIFE, chapter 26.10.

---

## G9 — Full-stack scores template

**Landed:** `malda new game --fullstack` (alias `malda new game-fullstack`)
emits `@client()` canvas + `@GET` / `@POST` scores + a `schema` for the save
blob. `docs/start-here.md` links it. Template: `Templates/game-fullstack/`.

- Client: G1/G2/G5 loop, `http.post` for scores.
- Server: in-memory list (top 10), `validate("Score", …)`.
- Host prompts / LLM NPCs stay **optional commentary** in the README, not
  required to run (games must work offline).
- Compile `--mode fullstack`; `malda play` refuses those sources.

---

## Post-kit — `game.sweepRect`

**Landed:** `Examples/Games/game_collision_smoke.malda` (fast dart vs thin gate) and
`malda_platform` axis-separated landings. Discrete `overlapRect` still tunnels
through a wall thinner than this tick's delta.

| Call | Contract |
|------|----------|
| `game.sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh)` | First contact along the delta. Returns `{ hit, t, nx, ny, x, y }`. Miss: `hit` false, `t` 1, `x`/`y` at the end pose. Hit: `t` in `[0, 1]`, position at impact, `nx`/`ny` pointing out of the obstacle (canvas Y+ down: floor is `ny = -1`). Zero/negative sizes miss. Positive-area overlap at the start: `t` 0 plus a minimum-translation normal. Touching a floor and sweeping X is not a hit. |

**Guardrails**

- Pure function (no canvas, no camera) like G3.
- Not tileset collision, swept circles, or a physics engine.
- Resolve X then Y against each obstacle and keep the earliest `t`.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`.

---

## Post-kit — `game.drawImageEx`

**Landed:** `Examples/Games/game_sprite_smoke.malda` (spinning green tile +
flipped magenta tile) and spinning coins in `malda_platform`. Axis-aligned
`drawImage` / `drawImageRect` cannot face left or rotate around a pivot.

| Call | Contract |
|------|----------|
| `game.drawImageEx(handle, x, y, options?)` | Draw with optional atlas source, dest size, origin, rotation, flip, and tint. `x`/`y` are the origin in world space. Options: `{ sx?, sy?, sw?, sh?, w?, h?, ox?, oy?, angle?, flipX?, flipY?, tint?, tintFill? }`. Omit options (or dest size): full image, dest size = source size, origin `(0, 0)`. `angle` radians; canvas Y+ down so positive is clockwise. `flipX`/`flipY` scale around `(ox, oy)`. Default origin is dest top-left, so `flipX` without `ox` draws to the left of `x`. For in-place facing set `ox` to `w / 2`. `tint` / `tintFill` landed in **G16**. Unready handles no-op. Camera and `setAlpha` apply. |

**Guardrails**

- Call `createCanvas` first (same as `drawImage`).
- Not a sprite object or scene graph. Tint / blend modes are **G16**.
- `save`/`restore` so later `fillRect` / HUD draws are not left rotated.
- `drawImage` / `drawImageRect` signatures stay unchanged.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`.

---

## Post-kit — camera space and mouse edges

**Landed:** `Examples/Games/game_sprite_smoke.malda` (HUD via `pushCamera`, click
marker via `getMouseWorldX`) and `Examples/Games/game_input_smoke.malda`
(`wasMousePressed` jump). `setCamera` offset draws but left mouse and HUD in
canvas pixels, so every camera game reset the camera by hand and click-to-world
was `getMouseX() + getCameraX()`.

| Call | Contract |
|------|----------|
| `game.pushCamera()` / `game.popCamera()` | Stack the current camera pan and zoom. HUD: push, `setCamera(0, 0)`, `setCameraZoom(1)`, draw, pop. Empty pop is a no-op. `createCanvas` clears the stack. |
| `game.screenToWorld(x, y)` / `game.worldToScreen(x, y)` | `{ x, y }`. Screen is canvas pixels. Honor pan and zoom. |
| `game.getMouseWorldX()` / `game.getMouseWorldY()` | Canvas mouse converted through the current camera pan and zoom. |
| `game.wasMousePressed(button?)` / `game.wasMouseReleased(button?)` | Same clock as `wasKeyPressed`; default button `0`. First touch still aliases button 0. |

**Guardrails**

- Overlap / `sweepRect` stay pure numbers (no camera).
- `getMouseX` / `getTouches` stay canvas pixels.
- Read mouse edges in `update` only (false in `render`).
- `drawImage` / `drawImageRect` signatures stay unchanged.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`.

---

## Post-kit — `game.setCameraZoom`

**Landed:** `Examples/Games/game_sprite_smoke.malda` (`[` / `]` zoom, HUD resets
zoom to 1, click marker via `getMouseWorldX`). Pan + screen/world conversion
could not magnify a scene; every draw size stayed 1:1 with canvas pixels.

| Call | Contract |
|------|----------|
| `game.setCameraZoom(z)` | Default `1`. Non-positive / non-finite → `1`. Clamp `[0.05, 100]`. Subsequent world draws scale sizes, radii, and stroke widths after the pan offset. |
| `game.getCameraZoom()` | |

**Guardrails**

- `pushCamera` / `popCamera` stack pan **and** zoom. HUD: push, `setCamera(0, 0)`, `setCameraZoom(1)`, draw, pop.
- `screenToWorld` / `worldToScreen` / `getMouseWorldX` honor zoom. `getMouseX` stays canvas pixels.
- Overlap / `sweepRect` stay pure numbers (no camera).
- `createCanvas` resets zoom to `1` and clears the stack.
- Not a camera object, letterboxing, or a second renderer.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`.

---

## G10 — Size queries, pixelated draws, `strokeCircle`

**Landed:** `Examples/Games/game_sprite_smoke.malda` (HUD box via `measureText`,
atlas size via `imageWidth`, marker ring via `strokeCircle`, `setPixelated`).
Games hardcoded bitmap and canvas sizes, injected CSS for crisp pixels, and
had `fillCircle` without a stroke twin.

| Call | Contract |
|------|----------|
| `game.imageWidth(handle)` / `game.imageHeight(handle)` | Bitmap size, or `0` until ready / on a missing or invalid handle. No canvas required. |
| `game.getCanvasWidth()` / `game.getCanvasHeight()` | Backing-store pixels. Requires `createCanvas`. |
| `game.measureText(text, font?)` | `{ width, height }` in **unscaled** font pixels. Ignores camera pan/zoom. Height uses canvas bounding boxes when present, else the `px` size in `font` (default `"16px sans-serif"`). Requires canvas. |
| `game.setPixelated(enabled)` | `true`: `imageSmoothingEnabled = false` and CSS `image-rendering: pixelated`. `false` / `createCanvas`: browser smoothing. |
| `game.strokeCircle(x, y, radius, color?, width?)` | Stroke; default color `#ffffff`, width `1`. Camera pan and zoom apply. |

**Guardrails**

- Call `createCanvas` before canvas-size / measure / pixelated / `strokeCircle`.
- `imageWidth` / `imageHeight` may run without a canvas (same as `imageIsReady`).
- `measureText` is HUD metrics, not a world-space size — measure after `pushCamera` + zoom 1.
- Not HiDPI backing-store scale, letterboxing, or a second renderer.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`.

---

## G11 — Multi-obstacle sweep (landed)

**Why:** `malda_platform` still loops `sweepRect` over every platform and keeps
the earliest `t`. That loop is the landing boilerplate G3 / `sweepRect` left
behind. A fast dart vs two thin gates should not copy `earliestHit` either
(`game_collision_smoke.malda`).

| Call | Contract |
|------|----------|
| `game.sweepRects(x, y, w, h, dx, dy, obstacles)` | `obstacles` is an array of `{ x, y, w, h }` (quoted keys, same as `drawImageEx` options). Returns the same `{ hit, t, nx, ny, x, y }` as `sweepRect` against the **earliest** hit (`smallest t`; ties: first in array order). Empty / missing / non-array: miss (`hit` false, `t` 1, end pose). Skip entries that are not objects or that have `w`/`h` ≤ 0. Each object is one AABB; missing numeric fields coerce like `sweepRect` (`0`). |

**Guardrails**

- Pure function: no canvas, no camera (same as `sweepRect`).
- Do **not** accept four parallel arrays (`platX` / `platY` / …). Convert once at load; keep one call shape.
- Not tileset collision, swept circles, a physics world, or a spatial index.
- Axis-separated use stays the caller's job: `sweepRects(..., dx, 0, …)` then `sweepRects(..., 0, dy, …)`.

**Smoke:** extend `Examples/Games/game_collision_smoke.malda` (dart vs wall **and** gate; ghost still tunnels). Showcase: `malda_platform` drops `sweepPlatforms` and builds one `plats` array of `{ x, y, w, h }`.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`, `docs/llm/malda-gotchas.md` (same tunneling note as `sweepRect`). Tests: `JsTranspiler_Maps…` + `GameRuntime_SweepRects_PicksEarliestHit` (no canvas). Filtered: `JavaScriptBackendTests`.

**PR size:** one runtime helper + smoke + showcase rewrite. Do not fold G12 into this PR (`malda_platform` will change twice if G12 follows).

---

## G12 — Camera follow clamp (landed)

**Why:** `malda_platform` render copies min/max so the camera stays inside the
world (`playerX - 280`, then clamp). Pixel-art games also want integer pan to
avoid subpixel shimmer. `math.clamp` already exists — do not add `game.clamp`.

| Call | Contract |
|------|----------|
| `game.followCamera(targetX, targetY, viewW, viewH, worldW, worldH, options?)` | Sets pan so `target` sits at `(screenX, screenY)` in the view, then clamps so the view stays inside `(0, 0)–(worldW, worldH)`. Options: `{ screenX?, screenY?, snap? }`. Defaults: `screenX = viewW / 2`, `screenY = viewH / 2` (center follow). `snap: true` floors pan after clamp. If `worldW ≤ viewW`, `camX = 0` (same for Y). Does **not** change zoom. Requires `createCanvas` (calls `setCamera`). Returns `null`. |

Today's platform lead (`camX = playerX - 280`) is `followCamera(playerX, playerY, canvasW, canvasH, worldW, canvasH, { "screenX": 280 })`. With `worldH = viewH`, `camY` clamps to `0`.

**Guardrails**

- Not a camera object, lerp/look-ahead, letterboxing, or dead-zone follow.
- HUD still uses `pushCamera` / zoom 1. Follow runs in `render` (or `update`) **before** world draws; do not follow after `pushCamera`.
- `snap` is integer world pan, not a second renderer. Zoom stays whatever `setCameraZoom` last set.
- View size is the caller's (`getCanvasWidth()` / `getCanvasHeight()` after G10) — do not implicit-read canvas inside the helper so tests stay numeric.

**Smoke:** `game_sprite_smoke.malda` can keep manual pan; showcase `malda_platform` switches the clamp block to `followCamera`. Optional `snap: true` on that call.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`. Tests: fake canvas + `getCameraX` after follow/clamp/snap. Filtered: `JavaScriptBackendTests`.

**Depends on:** none (G10 canvas getters are optional convenience in the example). Land **after G11** if both rewrite `malda_platform` in the same week.

---

## G13 — Sample pan / playbackRate (landed)

**Why:** `audioPlaySample` options are `{ loop }` only (`resolveSamplePlayArgs` /
`startSamplePlayback` in `malda-js-runtime.js`). Side-of-screen coins and pitched
jumps need stereo pan and playback rate without a second audio graph.

| Call | Contract |
|------|----------|
| `game.audioPlaySample(url, volume?, options?)` | **Additive** options: `{ loop?, pan?, playbackRate? }`. `loop` unchanged (default false). `pan` clamp `[-1, 1]`, default `0` (center). `playbackRate` default `1`; non-positive / non-finite → `1`; clamp `[0.25, 4]`. Signature and return (`null`) stay the same. `{ loop: true }` with no pan/rate still works. |
| Decode / cap | Unchanged: decode once per URL, 32-node cap, autoplay no-op until `audioInit`, `audioStopSample` does not stop the v1 track. |

**Implementation notes**

- Set `AudioBufferSourceNode.playbackRate` before `start`.
- Insert `StereoPannerNode` between the sample gain and `audioMasterGain` when `createStereoPanner` exists; if missing, ignore `pan` and still play (Node harness / old engines).
- Pending plays (`enqueueSamplePlay`) must store `pan` and `playbackRate` next to `volume` / `loop`.

**Guardrails**

- Do **not** change v1 `audioPlayTone` / pattern / track signatures.
- `pan` is stereo balance, not a 3D panner or distance model.
- Out of range pan/rate clamp; they do not throw.

**Smoke:** `Examples/Games/game_audio_sample_smoke.malda` — Z/X already overlap two beeps; add pan (Z left, X right) and a third key for a faster `playbackRate`. Pattern must keep playing.

**Files:** runtime audio graph, chapter 26.9, `docs/javascript-backend.md` Audio Spec v1 additive note, `docs/llm/malda-gotchas.md` (stop-sample still does not stop the track). Tests: extend `GameRuntime_AudioPlaySample_OverlapsWithoutStoppingTrack` (fake context records pan/rate). Filtered: `JavaScriptBackendTests`.

**Independent of G11/G12/G15.**

---

## G14 — Gamepad completeness (landed)

**Why:** Keys have press **and** release edges; pads only have
`wasGamepadButtonPressed`. Analog sticks chatter around 0 with no deadzone.
`beginInputFrame` already diffs `gamepadButtonsDown` vs `gamepadButtonsPrev` for
presses and then overwrites `prev` — releases are the other half of that diff.

| Call | Contract |
|------|----------|
| `game.wasGamepadButtonReleased(index, button)` | `true` on the first **update** after the button goes up. Same clock as `wasKeyReleased` / `wasGamepadButtonPressed` (false in `render`). Missing pad / missing Gamepad API: false. |
| `game.getGamepadAxis(index, axis, deadzone?)` | Existing two-arg form **unchanged** (raw `[-1, 1]`, missing → `0`). Optional `deadzone`: clamp `[0, 1]`, default when omitted is `0`. If `abs(value) ≤ deadzone` return `0`; otherwise return the raw value (**no** radial rescale). |

**Guardrails**

- Snapshot releases in `beginInputFrame` **before** replacing `gamepadButtonsPrev`. Clear them in `endInputFrame` and `game.stop()` (same as presses).
- Do not invent a new Standard Gamepad mapping table.
- Deadzone is per-axis, not a 2D stick helper (`getGamepadStick` stays out).
- Read edges in `update` only.

**Smoke:** `Examples/Games/game_input_smoke.malda` — flash on `wasGamepadButtonReleased(0, 0)`; move with `getGamepadAxis(0, 0, 0.2)` so stick noise does not drift the box.

**Files:** runtime input, chapter 26.9, `docs/javascript-backend.md`, `docs/llm/malda-gotchas.md` (edges false in `render`). Tests: fake `navigator.getGamepads` around two `update` ticks (down then up). Filtered: `JavaScriptBackendTests`.

**Independent of G11–G13/G15.**

---

## G15 — Starter uses `startFixed` (landed)

**Why:** `Templates/game/app.malda` still calls `game.start`. Newcomers copy a
variable-dt paddle loop instead of the kit's fixed tick. `malda new game
--fullstack` already uses `startFixed`. Bounce stays the minimal `game.start`
sample.

| Piece | Contract |
|-------|----------|
| `Templates/game/app.malda` | `game.startFixed(updateGame, renderGame)` (default 60 Hz). Keep paddle + `overlapRect` + `wasKeyPressed("r")`. Locals and helpers stay in the `#app` block (JS closure rule). |
| `Templates/game/README.md` | Document `startFixed` (always `tickMs`, max 5 catch-up). Do not tell people to clamp `dt > 50`. |
| Tests | `Scaffold_GameTemplate_CreatesAppHtmlAndReadme` today asserts `game.start`, which also matches `game.startFixed`. Change to `Assert.Contains("game.startFixed")` and `Assert.DoesNotContain("game.start(")` so the old loop cannot sneak back. |

**Guardrails**

- No new runtime names. No rewrite into a platformer.
- `Examples/Games/game_bounce.malda` stays `game.start` (primitive-draw track).
- `docs/start-here.md` path “Build a Browser Game” does not need a new command; mention `startFixed` only if it currently tells people to use `game.start`.

**Files:** `Templates/game/`, `MaldaLang.Tests/ScaffoldingTests.cs`, README. Filtered: `FullyQualifiedName~Scaffold_GameTemplate`.

**Independent.** Can ship before or after G11–G14.

---

## G16 — Tint + blend (landed)

**Why:** After G15 the highest remaining kit gap in
[`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md) was color tint on
images plus a canvas composite mode. `setAlpha` cannot do hit-flash, additive
sparks, or a night multiply overlay. `drawImageEx` had reserved tint as out of
scope.

| Call | Contract |
|------|----------|
| `game.setBlend(mode)` / `game.getBlend()` | Subsequent world draws use a canvas composite. Names: `"alpha"` (default, `source-over`), `"add"` (`lighter`), `"multiply"`, `"screen"`. Aliases `"source-over"` / `"lighter"` map to `"alpha"` / `"add"`. Unknown / empty → `"alpha"` (no throw). Requires canvas. `createCanvas` resets to `"alpha"`. `clear()` always composites as `"alpha"` and does **not** change the current mode. Does not affect `setPixel` / `blitPixels`. |
| `game.drawImageEx(..., { tint?, tintFill? })` | Additive options. `tint` is a CSS color on an offscreen copy (omit / empty → no tint). Default is **multiply** (Love2D `setColor`; white is identity). `tintFill: true` replaces RGB and keeps alpha (`source-in`) — white fill is a hit-flash. `tintFill` without `tint` is ignored. Camera, `setAlpha`, and the current `setBlend` apply to the blit. |

**Guardrails**

- Blend sticks like `setAlpha`: `setBlend("add")`, draw, `setBlend("alpha")`.
- Not a sprite object, scene graph, extra blend names (`overlay`, `subtract`), or a second renderer.
- `drawImage` / `drawImageRect` signatures stay unchanged (tint is `drawImageEx` only).
- Offscreen tint buffer is reused; unready handles still no-op.

**Smoke:** `Examples/Games/game_sprite_smoke.malda` — spinning tile multiply-tints, flipped tile `tintFill` flash, marker glow via `setBlend("add")`, multiply strip. Showcase: `malda_platform` white `tintFill` on coin collect plus an additive spark.

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`, `docs/llm/malda-gotchas.md`. Tests: `JsTranspiler_Maps…` + `GameRuntime_SetBlend_…` + `GameRuntime_DrawImageEx_TintAndTintFill_…`. Filtered: `JavaScriptBackendTests`.

---

## G17 — Tile helpers (landed)

**Why:** After G16 the highest remaining kit gap in
[`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md) was a Pico-8-style
`map` / `mget` hole. `maldadash` stayed on `fillRect` cells; grid games had no
draw/query/sweep against a 2D id array. This is still functions on `game.*`,
not Tiled/LDtk or a tile *engine*.

| Call | Contract |
|------|----------|
| `game.tileAt(cells, col, row, options?)` | Id at cell coordinates (`col` = X, `row` = Y, row 0 at the top). Floors `col`/`row`. Out of range: `out` (default `empty`, default `0`). Nested rows (`cells[row][col]`) or a flat array with `columns`. Options: `{ columns?, rows?, empty?, out? }`. Pure function. |
| `game.drawTiles(handle, cells, tileW, tileH, options?)` | Blit non-empty ids from an atlas. Unready handles no-op. Atlas index is `id - firstId` (default `firstId` 1 so id `0` is empty). Options: `{ x?, y?, columns?, rows?, empty?, srcW?, srcH?, atlasColumns?, firstId? }`. `x`/`y` are the world origin of cell `(0, 0)`. `srcW`/`srcH` default to `tileW`/`tileH`. `atlasColumns` defaults to `floor(imageWidth / srcW)`. Camera, `setAlpha`, and `setBlend` apply. Culls to the current view. Requires canvas. |
| `game.sweepTiles(x, y, w, h, dx, dy, cells, tileW, tileH, options?)` | Same `{ hit, t, nx, ny, x, y }` as `sweepRect` against solid cells. Default: any id other than `empty` is solid. Optional `solids` is an array of ids. Options also take `x`/`y` origin plus the `tileAt` grid fields (`columns` / `rows` / `empty` / `out`). Out-of-bounds cells use `out`; if that id is solid, the map has a solid border. Pure function. |

**Guardrails**

- Not Tiled/LDtk, autotile, animated tiles, or a spatial index. Mutate the array the user owns (`cells[row][col] = id`); there is no `mset`.
- A flat array without `columns` is an empty map (`tileAt` returns `out`, `drawTiles` / `sweepTiles` no-op/miss).
- Axis-separated use stays the caller's job, same as `sweepRects`.
- `drawTiles` dest size is `tileW` × `tileH`; atlas frame size is `srcW`/`srcH` when those differ.

**Smoke:** `Examples/Games/game_tiles_smoke.malda` (atlas cave, gem pickups via `tileAt`, landings via `sweepTiles`). Showcase: `maldadash` `getTile` calls `tileAt` (flat grid + `out: STEEL`).

**Files:** runtime, chapter 26.9, `docs/javascript-backend.md`, `docs/llm/malda-gotchas.md`. Tests: `JsTranspiler_MapsGameTileApis…` + `GameRuntime_TileAt_…` + `GameRuntime_SweepTiles_…` + `GameRuntime_DrawTiles_…` + `JsTranspiler_TilesSmokeExample_…`. Filtered: `JavaScriptBackendTests`.

---

## How G11–G17 shipped

G11–G15 landed together after those contracts were specified. **G16** (tint +
`setBlend`) followed as the next ranked kit gap from
[`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md). **G17** (tile
helpers) is the Pico-8 `map`/`mget` slice from the same file.

---

## After G9 (deferred)

Engine-scale ideas from G1–G9. **G11–G17 above landed.** These rows stay out of
core until a later roadmap:

| Idea | Why it waited |
|------|----------------|
| `three.setTexture` / orbit controls | G8 left them out until look-at + `three_textured` prove the gap |
| Tileset collision / particles / scene graph | Helpers first (G3 + `sweepRect` + G17 `sweepTiles`); Tiled/LDtk and engines later as a pack |
| Box2D-class physics in core | Same — optional pack, not stdlib growth |
| WebGL 2D sprite batcher | Canvas2D images (G1) first |
| Native desktop window (SDL, Raylib, Silk.NET) | Optional pack **out of this repo** |
| Interpreter / C# `game.start` | AST walk will not hit 60 Hz; matrix already says n/a |
| Web IDE playground parity with Desktop F5 | Separate from G6; do not block play/scaffold |
| Host prompts / LLM NPCs in the scores template | README commentary only; games must run offline |
| JS ↔ host actor bridging for multiplayer | JS actors are process-local; G9 uses HTTP |
| Rewriting Desktop IDE into a game editor | Out of scope |

Post-kit helpers that did land: **`game.sweepRect`**, **`game.sweepRects`**,
**`game.drawImageEx`**, camera space (`pushCamera` / world mouse / mouse edges),
**`game.setCameraZoom`**, **`game.followCamera`**, **G10** (`imageWidth` /
`getCanvasWidth` / `measureText` / `setPixelated` / `strokeCircle`), sample
`pan` / `playbackRate`, **`wasGamepadButtonReleased`** plus analog deadzone,
**G15** (`malda new game` uses `startFixed`), **G16** (`drawImageEx` `tint` /
`tintFill` plus `setBlend`), and **G17** (`drawTiles` / `tileAt` / `sweepTiles`).
Still not Tiled/LDtk, particles, a scene graph, or a physics engine.

---

## Explicit non-goals (revisit only with a new roadmap)

| Idea | Why not now |
|------|-------------|
| Interpreter / C# `game.start` | AST walk will not hit 60 Hz; matrix already says n/a |
| Native desktop window (SDL, Raylib, Silk.NET) | Optional pack **out of this repo** |
| Box2D / tile collision / particles / scene graph in core | Helpers first (G3 + `sweepRect` + G17); Tiled/LDtk later as a pack |
| WebGL 2D sprite batcher | Canvas2D images (G1) first |
| New keywords (`on update`, `entity`) | Violates construct-plan principle 5 |
| Flat aliases (`drawImage()`) | Deprecated; coverage/style guards |
| JS ↔ host actor bridging for multiplayer | JS actors are process-local; G9 uses HTTP |
| Rewriting Desktop IDE into a game editor | Out of scope |

---

## How G0–G10 shipped

Historical PR order (already landed):

1. **G0** — this document (docs only)
2. **G1** — runtime + smoke example
3. **G3** — small pure-function PR (beside G1)
4. **G2 + G4 + G5** — three small PRs
5. **G6** — CLI + `Templates/game`
6. **G7** — `malda_platform` showcase
7. **G8** — 3D assets
8. **G9** — fullstack template
9. **G10** — size queries / pixelated / `strokeCircle`
10. **G11–G15** — `sweepRects`, `followCamera`, sample pan/rate, gamepad
    release + deadzone, `malda new game` on `startFixed`
11. **G16** — `drawImageEx` tint / `tintFill` + `setBlend`
12. **G17** — `drawTiles` / `tileAt` / `sweepTiles`

Each MINOR runtime slice updated chapter 26 and `docs/javascript-backend.md`
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
- Post-G17 peer comparison (not a workstream plan): [`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md)
