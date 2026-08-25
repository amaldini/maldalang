# MALDA 2D games kit vs 2D peers — gap analysis

**Status:** Evaluation of the landed JS `game.*` kit (G0–G15)  
**Audience:** maintainers deciding whether to reopen a post-G15 2D roadmap  
**Not a roadmap.** Deferred engine-scale items stay in [`docs/roadmap-games.md`](roadmap-games.md). This file compares the **current** immediate-mode canvas kit with libraries people actually finish 2D games in.

**Bar MALDA set for itself:** Love2D / Pico-8 / Phaser, not Unity. JS-only Canvas2D. Functions, not sprite objects or parser keywords.

---

## 1. What the kit is today

`game.*` is an **immediate-mode** browser loop: you own numbers and arrays; the runtime draws, samples input, and answers geometry queries. There is no scene graph, no `Sprite` type, no physics world.

| Layer | Landed surface |
|-------|----------------|
| Loop | `createCanvas`, `start` / `startFixed` (60 Hz accrue, max 5 catch-up), `stop` |
| Draw | Rect/circle fill + stroke, line, text, alpha, pixelated blit, CPU `setPixel` / `blitPixels` |
| Images | Async `loadImage` handle, atlas `drawImageRect`, `drawImageEx` (origin / rotate / flip) |
| Camera | Pan, zoom, push/pop stack, screen↔world, `followCamera` clamp + optional integer snap |
| Input | Key/mouse edges, touches, Standard Gamepad (press/release, analog + per-axis deadzone) |
| Collision | Inclusive AABB/circle overlap, point tests, `sweepRect` / `sweepRects` |
| Audio | Chip-tune Spec v1 (tones, noise, pattern, one music track) + overlapping file samples (`loop` / `pan` / `playbackRate`) |
| Persist | Origin `localStorage` via `save` / `load` / `removeSave` |
| Tooling | `malda new game` (fixed-tick paddle), `malda play`, PWA compile, `malda new game --fullstack` scores |

**Showcases that prove the ceiling:**

- Primitive-draw: `game_bounce`, `maldanoid` (fixed tick, overlap bounces, save)
- Kit 2D: `malda_platform` (atlas, `followCamera`, axis-separated `sweepRects`, SFX, HUD stack)
- Grid cave without a tile API: `maldadash` (`fillRect` cells + user pause/state machine)

**Explicit non-goals (still correct):** Box2D in core, Pixi/WebGL 2D batcher, native SDL/Raylib, interpreter `game.start`, sprite objects, new keywords (`on update`, `entity`).

---

## 2. Peer set (fair comparisons)

| Peer | Why it is in the set | MALDA relationship |
|------|----------------------|--------------------|
| **Love2D** | Immediate-mode Lua; closest design cousin | Same “you draw every frame” contract; Love2D is deeper (color tint, blend, particles, SpriteBatch, TTF, physics module, native window) |
| **Pico-8 / TIC-80** | Fantasy console: tiny API, games people finish | MALDA audio patterns and pixel buffer rhyme; Pico-8 has `map` / `mget` / `spr` as first-class tile/sprite ops |
| **Raylib** | Immediate-mode C; Camera2D + collision helpers | Same spirit, native + more draw primitives (`DrawTexturePro`, polygons, render textures) |
| **Phaser 3** | Browser 2D that ships products | Object model (scenes, sprites, Arcade/Matter, Tilemap, tweens, cameras-as-objects). MALDA is **not** trying to be this |
| **Kaplay (Kaboom)** | Tiny JS component kit | Closer in size than Phaser; still has `sprite()`, `body()`, `scene()`, tile helpers |
| **Godot 2D / GameMaker / Defold** | Full engines with editors | Out of scope as a product target; useful as a **genre ceiling** (what teams expect once a platformer grows) |
| **PixiJS** | WebGL 2D renderer | Explicitly deferred (second renderer) |

Unity 2D is listed only as “not the bar.” Comparing feature-for-feature with Unity/Godot would punish MALDA for choices the roadmap already made.

---

## 3. Genre feasibility (can you finish it?)

| Genre | Verdict with G0–G15 | What is missing in practice |
|-------|---------------------|-----------------------------|
| Pong / breakout / shmup-lite | **Finishable** | Template + `maldanoid` already do this |
| Short side-scroller | **Finishable** | `malda_platform`; animation, slopes, one-way platforms, and enemies are user code |
| Grid / cave / Sokoban / Boulder Dash | **Finishable, painful** | `maldadash` is ~900 lines of `fillRect` cells; no `map`/`mget` |
| Twin-stick / top-down action | **Possible** | Input yes; no spatial index, particles, or Y-sort |
| Metroidvania / Zelda-like | **Struggle** | Tilemap draw + solid query, rooms/scenes, NPC/dialog text wrap |
| JRPG | **Struggle** | Same + menu UI, bitmap fonts, nine-slice |
| Physics puzzler (Angry Birds-class) | **No (in core)** | Rigid bodies, joints, sleeping, debug draw |
| Fighting game | **No** | Frame data, hitboxes as data, skeletal/cel animation |
| Particle-heavy juice ( juiciness ) | **No without rolling your own** | No emitter; additive blend + tint also missing |
| Mobile hypercasual | **Partial** | Touches exist; no virtual stick, safe-area, letterbox, pause-on-blur helper |
| Realtime multiplayer action | **No** | G9 is HTTP scores; JS actors are process-local |

The kit meets its own bar for **small canvas games people finish**. It does not meet Phaser/Godot for **content-heavy 2D** (maps, animation sets, juice).

---

## 4. Gap matrix

Legend: **kit** = still fits “functions on `game.*`”; **engine** = object/world/editor (pack or never); **host** = language, backend, or tooling — not a new `game.*` name.

### 4.1 Drawing and sprites

| Capability | Love2D / Raylib | Phaser / Godot | MALDA today | Class |
|------------|-----------------|----------------|-------------|-------|
| Image blit + atlas rect | yes | yes | `drawImage` / `drawImageRect` | landed |
| Rotate / flip / origin | `draw` / `DrawTexturePro` | sprite transform | `drawImageEx` | landed |
| Color **tint** on images | `setColor` tints draws | sprite.tint | **no** (`setAlpha` only; `drawImageEx` explicitly not tint) | **kit** |
| Blend modes (add / multiply) | `setBlendMode` | blendMode | **no** | **kit** |
| Offscreen render target | Canvas / render texture | RenderTexture | pixel buffer only (CPU); cannot composite a layer then blit with blend | **kit** (light) / engine (full) |
| Sprite **objects** / scene graph | no (Love2D) | yes | no by design | engine |
| Frame animation from atlas | libs (`anim8`) | AnimationPlayer / `anims` | manual `sx` + `dt` | **kit** |
| Spine / skeletal | no / plugins | plugins / AnimationPlayer | **no** | engine |
| Nine-slice | libs | yes | **no** | **kit** |
| Polygon / ellipse / arc / rounded rect | yes | Graphics | rect + circle + line only | **kit** |
| Layers / Y-sort helper | user | z-index / ysort | draw-call order | **kit** |
| WebGL sprite batcher | SpriteBatch | default | Canvas2D `drawImage` per call | engine (deferred) |

**Friction evidence:** `malda_platform` still loops `drawImageRect` per 32px of platform. Hundreds of tiles per frame (a 40×22 cave) will hitch on Canvas2D; `maldadash` avoids images and uses `fillRect`.

### 4.2 Tilemaps (largest content gap)

| Capability | Pico-8 | Phaser | Godot | MALDA |
|------------|--------|--------|-------|-------|
| Draw a 2D cell array | `map` | Tilemap | TileMapLayer | user `while` + `fillRect` / `drawImageRect` |
| Query solid / tile id | `mget` / flags | `getTileAt` | `get_cell_atlas_coords` | user arrays |
| Tiled / LDtk import | tools | first-class | importer | **no** |
| Autotile / animated tiles | limited | yes | yes | **no** |
| Tile **collision** vs actor | map flags + user | Arcade + tile | physics layers | `sweepRects` on a **hand-built AABB list**, not cells |

A `game.drawTiles(image, cells, tileW, tileH, …)` plus `game.tileAt` / `game.sweepTiles` would unlock grid games without becoming Tiled. Full TMX/LDtk parsers belong in a pack.

### 4.3 Camera and loop

| Capability | Typical peer | MALDA | Class |
|------------|--------------|-------|-------|
| Pan + zoom + HUD stack | Love2D user / Phaser Camera | landed | landed |
| Clamp follow + pixel snap | Godot limits / Phaser clamp | `followCamera` | landed |
| Lerp / look-ahead / dead-zone | Godot drag margins, Phaser lerp | **no** (roadmap called this out) | **kit** |
| Screen shake / flash / fade | Phaser camera FX | user offset | **kit** |
| Letterbox / integer scale / HiDPI backing store | Phaser Scale Manager, Pico-8 integer | CSS on host HTML; `createCanvas` is 1:1 CSS pixels | **kit** (G10 left this out) |
| `startFixed` **render interpolation** (`alpha` leftover) | Godot physics interpolation, many fixed-step articles | update 0–5× then render at last pose — no leftover | **kit** |
| Pause / time scale without `stop()` | Phaser `time.timeScale`, Godot `Engine.time_scale` | **no**; `stop` tears down input | **kit** |
| Pause-when-hidden (Page Visibility) | engines often freeze | rAF throttles; catch-up capped at 5 (time is dropped, not paused cleanly) | **kit** |

### 4.4 Collision and physics

| Capability | Love2D | Phaser Arcade | MALDA | Class |
|------------|--------|---------------|-------|-------|
| AABB / circle overlap | user / bump.lua | yes | landed | landed |
| Swept AABB | bump.lua | separate | `sweepRect(s)` | landed |
| Swept **circle** | extra | extra | **no** | **kit** |
| Raycast / line vs AABB | extra | yes | **no** | **kit** |
| Slopes / one-way platforms | extra | platforms | **no** | **kit** (helper) |
| Collision layers / masks | physics module | yes | **no** | **kit** or engine |
| Spatial hash / quadtree | extra | yes | O(n) over the obstacle array | **kit** |
| Rigid body / joints / gravity world | `love.physics` (Box2D) | Arcade or Matter | **no** by design | engine |

`malda_platform` still axis-separates X then Y in user code. That is acceptable for a kit; a `moveAndSlide(rect, vx, vy, solids)` would remove the remaining landing boilerplate without a physics engine.

### 4.5 Input

| Capability | Peers | MALDA | Class |
|------------|-------|-------|-------|
| Key/mouse edges, touches, gamepad press/release | yes | landed | landed |
| Analog deadzone (per axis) | yes | landed (no radial rescale) | landed |
| 2D stick helper (`getGamepadStick`) | common | **no** (G14 explicitly left it out) | **kit** |
| Mouse **wheel** | Love2D / Phaser | **no** | **kit** |
| Pointer lock / relative mouse | Love2D relative mode | **no** | **kit** |
| Gamepad **rumble** | Love2D / Gamepad API | **no** | **kit** |
| Virtual on-screen stick | Kaplay / Phaser plugins | **no** | **kit** |
| Pinch / swipe gestures | mobile kits | raw `getTouches` only | **kit** |
| Action **mapping** (jump = Space or A or touch) | Godot InputMap | duplicated `isKeyDown` / gamepad in every game | **kit** |

### 4.6 Audio

| Capability | Love2D | Phaser | Pico-8 | MALDA |
|------------|--------|--------|--------|-------|
| Chip pattern + one track | — | — | yes | Audio Spec v1 | landed (differentiator) |
| Overlapping file samples + pan/rate | Source objects | yes | — | landed |
| **Handle per voice** (stop this jump, not every jump SFX) | yes | yes | — | `audioPlaySample` returns **`null`**; `audioStopSample(url)` stops **all** of that URL | **kit** |
| Music crossfade / ducking | user | yes | — | one track; samples vs track are separate graphs | **kit** |
| Positional 2D from world X | Source:setPosition | yes | — | stereo `pan` only | **kit** |
| Decode progress / `sampleIsReady` | yes | loader | — | silent no-op until decoded (same as images) | **kit** |

### 4.7 Text and HUD

| Capability | Peers | MALDA | Class |
|------------|-------|-------|-------|
| CSS font `drawText` + `measureText` | TTF / Text | landed | landed |
| Align / wrap / line height | `printf`, Phaser wordWrap | **no** | **kit** |
| Bitmap font / BMFont | Love2D, Phaser BitmapText, Pico-8 | **no** | **kit** |
| Canvas UI widgets | engines / Dear ImGui ports | `dom.*` HTML or `drawText` | host (DOM) vs kit (canvas) |

Pixel-art games that want a consistent HUD currently mix CSS fonts with `setPixelated` sprites — they fight.

### 4.8 Animation, juice, FX

| Capability | Phaser / Godot | Love2D | MALDA | Class |
|------------|----------------|--------|-------|-------|
| Tweens (ease to a value) | first-class | libs (`flux`) | **no** | **kit** |
| Particle emitter | yes | ParticleSystem | **no** (deferred) | **kit** (tiny) / engine (full) |
| Sprite flash on hit | tint | setColor | **no tint** | depends on tint |
| After-image / trail | yes | user | user | kit if blend exists |

### 4.9 Assets, scenes, save, ship

| Capability | Peers | MALDA | Class |
|------------|-------|-------|-------|
| Async image handle | similar | landed | landed |
| Loader with **progress** / manifest | Phaser Loader | **no** | **kit** |
| TexturePacker / Aseprite JSON | common | **no** | pack |
| Scene / state stack | Phaser Scene, hump.gamestate | user `state = "splash"` (`maldadash`) | **kit** |
| Save | files / cloud | `localStorage` only | host (browser) |
| Desktop window / Steam | Love2D, Godot, Raylib | JS/PWA/itch folder | engine (out of tree) |
| Visual game editor | Godot / GM | Desktop IDE is **not** a game editor | non-goal |

### 4.10 Host and language constraints (not `game.*`)

These hurt 2D authors even if the canvas API were complete:

| Constraint | Effect on games |
|------------|-----------------|
| `game-canvas` is **JS-only** | No interpret 60 Hz loop; Desktop F5 is WebView2 + source maps, not `malda debug-adapter` |
| Web IDE ≠ Desktop F5 | Playground cannot replace `malda play` |
| No string interpolation | HUD lines are `"Coins: " + string(coins) + …` |
| JS closure rule | Template/examples keep **all** locals in one `#app` block |
| Canvas2D | No 2D `@shader()` on the 2D path (`@shader` is the `three.*` / fullscreen-quad path) |
| Full-stack `malda play` refused | Score games need `compile --mode fullstack` |

MALDA **advantages** the peers lack: `@client` / `@server` + `schema`/`validate` (G9 scores), PWA compile, chip-tune v1, CPU pixel buffer, host prompts as optional NPC commentary.

---

## 5. Where MALDA is already enough

Against Love2D’s *floor* (not its plugin ecosystem) the kit is honest:

- A newcomer can scaffold and play a fixed-tick game without writing host HTML.
- A platformer can load an atlas, follow a clamped camera, land on AABBs without tunneling, play overlapping SFX, and persist a high score.
- Input is complete enough for keyboard, one gamepad, and simple touch (first touch still aliases mouse 0).
- Audio v1 is stronger than Phaser’s out-of-the-box chip story and weaker than Love2D `Source` objects.

Do **not** treat “no Box2D / no scene graph / no Pixi” as unfinished work. Those are product boundaries.

---

## 6. Highest-leverage remaining gaps (if `game.*` is reopened)

Ranked for **finishable games**, still inside “functions beat objects” and “deepen `game.*`”:

1. **Tint + canvas composite mode** on `drawImageEx` / a `setBlend` — unlocks hit-flash, additive sparks, night multiply. Small API; `drawImageEx` already reserved this as out of scope.
2. **Tile helpers** — `drawTiles` + cell query + `sweepTiles` against a 2D id grid. This is the Pico-8 `map`/`mget` hole; it is why `maldadash` never used G1 images.
3. **`moveAndSlide` / one-call axis resolve** — wrap the X-then-Y `sweepRects` pattern `malda_platform` still copies.
4. **Atlas animation helper** — `drawAnim(handle, x, y, { frames, fps, t })` (pure draw, no sprite object).
5. **Camera juice** — `followCamera` options: `lerp`, `shake`, maybe look-ahead. Dead-zone is optional.
6. **Integer scale / letterbox** — one helper so pixel art is not host-CSS (G10 leftover).
7. **Sample voice handle** — return an id from `audioPlaySample` so overlapping clips of the same URL can be stopped independently.
8. **Text wrap + align** (and optionally a baked bitmap font blit).
9. **Mouse wheel, rumble, `getGamepadStick`** — leftover input completeness from G14.
10. **Pause / timeScale** and/or `startFixed` interpolation leftover for high-refresh displays.

**Stay out of core** (unchanged from the games roadmap): Box2D-class physics, Tiled/LDtk as a required format, particle *engine*, scene graph, WebGL 2D batcher, native SDL, Desktop-as-game-editor.

A **tiny** particle helper (`game.burst(x, y, { n, life, color })` as immediate draw) is the one deferred row that could still be kit-sized; a full ParticleSystem is a pack.

---

## 7. Suggested decision rule

| If the next game you want in `Examples/Games` is… | Then |
|---------------------------------------------------|------|
| Another primitive-draw arcade | **Do nothing** — kit is sufficient |
| A tiled cave / RPG overworld / Metroidvania slice | Add **tile helpers** (item 2) before anything else |
| A juicier platformer (flash, shake, dust) | Add **tint/blend + camera shake** (items 1 and 5) |
| A physics toy | **Do not** grow `game.*` — optional pack |
| A Steam desktop action game | Out of tree (native pack), not this repo |

Revisit this file when a new 2D workstream is actually scheduled. Until then, [`docs/roadmap-games.md`](roadmap-games.md) remains the status of G0–G15 and the non-goals list.

---

## Related

- Kit status and non-goals: [`docs/roadmap-games.md`](roadmap-games.md)
- Call contracts: [`docs/javascript-backend.md`](javascript-backend.md)
- Capability tag `game-canvas`: [`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md)
- Examples: [`Examples/Games/README.md`](../Examples/Games/README.md)
- Chapter 26: [`ReferenceManual/26-browser-javascript-backend.html`](../ReferenceManual/26-browser-javascript-backend.html)
