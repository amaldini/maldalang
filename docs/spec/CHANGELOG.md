# Malda language specification — CHANGELOG and versioning policy

**Document:** `docs/spec/CHANGELOG.md`  
**Applies to:** [malda-language-1.0.md](malda-language-1.0.md) and successor spec files under `docs/spec/`  
**Toolchain:** `malda` CLI, interpreter, and transpiler follow the active spec; their **package version** (e.g. assembly `1.x`) is independent but should cite the spec version in release notes.

---

## Versioning model

Malda uses **semantic versioning** for the **language specification** (`MAJOR.MINOR.PATCH`):

| Bump | When | Examples |
|------|------|----------|
| **MAJOR** | Breaking change to Tier 0 semantics, syntax removal, or incompatible builtin contract | Remove `fn` alias; change `dict` missing-key from `null` to error; drop flat `abs()` global |
| **MINOR** | Additive, backward-compatible contract | New keyword/syntax; new Tier 1 builtin; new `typeOf` tag while old tag still accepted during deprecation |
| **PATCH** | Clarification only; no observable behavior change in conformance tests | Spec prose fix; document de-facto parser rule; typo in grammar chapter |

**Spec status labels**

| Label | Meaning |
|-------|---------|
| **Draft X.Y** | Normative intent documented; Tier 0 conformance may still be incomplete |
| **Final X.Y** | Tier 0 conformance suite is the acceptance gate for that spec line |

Current line: **Final 1.0** ([malda-language-1.0.md](malda-language-1.0.md), declared 2026-08-12; Draft since 2026-06-04).

---

## What counts as breaking vs additive

### Tier 0 (language kernel)

| Change type | Tier |
|-------------|------|
| New keyword or statement form that existing programs can ignore | **MINOR** (if purely additive) |
| Stricter parse (previously accepted program becomes syntax error) | **MAJOR** |
| Stricter runtime (previously defined program changes result or errors) | **MAJOR** |
| `match` / `async` / actor semantics change | **MAJOR** |
| `null`, truthiness, or dictionary `d["missing"]` behavior change | **MAJOR** |

### Tier 1 (stdlib shipped with core distribution)

| Change type | Tier |
|-------------|------|
| New namespaced builtin (`math.foo`) | **MINOR** |
| New flat global mirroring namespaced API | **MINOR** during migration; removing flat global later is **MAJOR** after deprecation window |
| Moving builtin from core registry to optional pack | **MAJOR** for scripts that relied on zero-config global (pack migration uses deprecation policy below) |

### Tier 2 (optional packs)

Optional packs and platform hosts are versioned **separately** from Tier 0. Pack API breaks do not bump the Tier 0 spec MAJOR unless the core `loadNativeModule` contract changes.

### Documentation-only

| Change type | Tier |
|-------------|------|
| [35-grammar.html](../../ReferenceManual/35-grammar.html) aligned with parser | **PATCH** (spec 1.0 unchanged) |
| Reference Manual narrative | Not spec versioned; track in manual changelog if needed |

---

## One-release deprecation policy

**Default rule** for language surface, flat stdlib globals, and `typeOf` tag renames:

1. **Release N (deprecation release)**  
   - Old and new forms both work where possible.  
   - **IDE** emits `malda-style` or dedicated diagnostic (once per site).  
   - **Runtime** may log a single warning per process for hot paths.  
   - Spec and Reference Manual state the replacement and target removal version.

2. **Release N+1 (removal release)**  
   - Old form removed or hard-errors.  
   - Conformance tests updated; CHANGELOG records **MAJOR** if observable behavior removed.

**Exceptions** (require explicit CHANGELOG entry and roadmap approval):

- Security or correctness bugs (fix in **PATCH** or **MINOR**; if fix breaks programs, treat as **MAJOR**).  
- Optional-pack-only symbols (no core deprecation; use SDK `include` instead).

**Current deprecations (N = 2026-06 core distribution)**

| Surface | Replacement | Removal target |
|---------|-------------|----------------|
| Flat math builtins (`abs`, `sqrt`, …) | `math.*` | Next **MAJOR** core spec after flat-alias period |
| `Math.*` module alias | `math.*` | Same as flat math |
| Flat string builtins (`split`, `join`, …) | `str.*` | Same |
| Flat I/O builtins (`readFile`, `print`, …) | `io.*` | Same |
| `fn` / `def` function keywords | `function` | **Removed** (Unreleased MAJOR) |

---

## How to propose a spec change

1. Update [malda-language-1.0.md](malda-language-1.0.md) (or fork `malda-language-1.1.md` for large drafts).  
2. Add a **Conformance** row and test in `MaldaLang.Tests/Conformance/Tier0/` when behavior is normative.  
3. Add an entry under `[Unreleased]` below with **MAJOR** / **MINOR** / **PATCH** label.  
4. If syntax changes: update [35-grammar.html](../../ReferenceManual/35-grammar.html) and `ReferenceManualGrammarCoverageTests`.  
5. Phase 2.4: `scripts/verify-spec-parser-drift.ps1` and `bitbucket-pipelines.yml` fail PRs that touch `Parser.cs` or `Lexer.cs` without spec/grammar/CHANGELOG update.

**Implementation precedence for Final 1.0:** interpreter + Tier 0 tests → spec prose → Reference Manual.

---

## Release history (spec line)

### [Unreleased]

#### Added (MINOR — three data textures + Mandelbrot perturbation)

- **`three.createDataTexture` / `three.updateDataTexture` / `three.mandelbrotOrbit` (JavaScript backend only):** `createDataTexture(width, height, pixels, options?)` builds an RGBA `DataTexture` (default float32; `"type": "byte"` for 8-bit), nearest-filtered, no mipmaps, ready immediately. `updateDataTexture(handle, pixels)` rewrites the buffer. `mandelbrotOrbit(real, imag, maxIter)` parses **decimal strings** for C (MALDA IEEE doubles cannot hold a deep center), walks `Z_{n+1} = Z_n² + C` in BigInt fixed-point (256 fraction bits), and packs `(re, im, alive, 1)` into a 1-row float texture. `createShaderMaterial` / `setUniform` unwrap those handles for `sampler2D`. Interpreter / C# transpile: n/a (`three.*`).
- **`Examples/Games/three_shader_mandelbrot.malda`:** Seahorse Valley infinite zoom now uses the BigInt reference orbit plus a scaled-perturbation `@shader()` kernel (not GPU float-float). Host scale wraps after 32 decades. Hold Space to pause; `[` `]` change speed. Host page: `three_shader_mandelbrot_runtime_smoke_test.html`.

#### Added (PATCH — infinite Mandelbrot zoom example)

- **`Examples/Games/three_shader_mandelbrot.malda`:** GPU Mandelbrot infinite zoom on the existing `three.createShaderMaterial` / `@shader()` path. The kernel uses float-float (hi/lo) complex arithmetic around Seahorse Valley; host MALDA advances an exponential scale that wraps after six decades so the dive loops. Hold Space to pause; `[` `]` change speed. Host page: `three_shader_mandelbrot_runtime_smoke_test.html`. No new `game.*` / `three.*` names. JavaScript backend only.

#### Added (PATCH — GPU billiards example)

- **`Examples/Games/three_shader_billiards.malda`:** playable GLSL pool table on the existing `three.createShaderMaterial` / `@shader()` path. Host MALDA aims the cue and steps 2D circle physics (elastic ball–ball, cushion restitution, pockets, friction); the kernel traces `uBall0`–`uBall15`, a cue stick, an aim line, and 7-segment decals. The cue stays locked on the white ball; mouse-on-table aims (A/D fine-tunes). Hold click or Space to charge power, release to shoot; R reracks; `[` `]` / `+` `−` zoom; `C` or **Stop camera** zeros orbit speed. Sliders set cushion e, ball e, and felt friction. Host page: `three_shader_billiards_runtime_smoke_test.html`. No new `game.*` / `three.*` names. JavaScript backend only.

#### Added (PATCH — billiards appendix in the Reference Manual)

- **`ReferenceManual/37-appendix-gpu-billiards.html`:** playable compiled GPU billiards as a manual appendix (`billiards-play.html` + committed `three_shader_billiards.js`). Change the `.malda` source and recompile; a guard fails if the committed JS drifts. GitHub Pages copies `three.min.js` and `malda-js-runtime.js` beside the chapter.

#### Added (MINOR — @MCPTool / @Tool host-validate args)

- **Host-validate `@MCPTool` / `@Tool` arguments:** when the third argument attaches a schema (name or JSON object string), incoming MCP `tools/call`, `MCPServer.callTool(name, args)`, agent `@Tool` invoke, and `tool.execute(args)` run the same `validate()` check **before** the function body. Failure does not call the body: `callTool` returns `{ ok: false, error }`; MCP `tools/call` returns `isError: true`; agents / `execute` return `Error: tool arguments failed schema: …`. Omitted third argument stays advertise-only (auto-generated all-string properties, no host check). Direct `add(1, 2)` calls are unchanged. Interpreter and C# transpile agree. JavaScript: n/a (MCP is host-only). Example: `Examples/MCP/mcp_schema_tool.malda`. Few-shot: `docs/llm/few-shot/32_mcptool_validate.malda`.

#### Added (MINOR — @MCPTool / @Tool schema names)

- **`@MCPTool` / `@Tool` third argument:** a registered `schema` or sum-type name (`"AddArgs"` or the identifier `AddArgs`) is resolved to JSON Schema the same way as `validate("AddArgs", …)`. A JSON schema object string still works (enums, defaults). Omitted still auto-generates all-string properties. Unknown names error (they used to be ignored when the string was not valid JSON). `MCPServer.getTools()` returns `{ name, description, inputSchema }` without starting STDIO. Interpreter and C# transpile agree. JavaScript: n/a (MCP is host-only). Example: `Examples/MCP/mcp_schema_tool.malda`. Few-shot: `docs/llm/few-shot/31_mcptool_schema.malda`.

#### Added (MINOR — prompt attachments)

- **Prompt `attachments:` (image/pdf):** additive prompt-body field. `user` / `system` stay strings. Each item is `{ kind: "image"|"pdf", path }` or `{ kind: "image", url }` (`http`/`https` pass-through, no in-process fetch). Kind may be inferred from `.png`/`.jpg`/`.jpeg`/`.webp`/`.gif`/`.pdf`. `path` may be a string or `cap.fileRead` token. Building a `PromptInstance` stores metadata only; `await` / `runPrompt` / `agent.think` encode OpenAI `image_url` / `file` content parts on HTTP clients. In-process GGUF throws (text-only). Max 8 items, 10 MB each. JavaScript: n/a (prompts are host-only). Example: `Examples/Prompts/multimodal_attachments.malda`.

#### Changed (PATCH — drop former informal overview from precedence)

- **Normative precedence** in [`malda-language-1.0.md`](malda-language-1.0.md) no longer names the retired informal overview. Final 1.0 order is unchanged: interpreter + Tier 0 tests → spec prose → Reference Manual. `README.md` is explicitly non-normative.

#### Added (PATCH — program(Api) few-shots / runProgram in step)

- **Closed `api` / `program(Api)` few-shots:** `docs/llm/few-shot/28_api_program_prompt.malda` is `prompt … -> program(Api)` plus `evalPrompt` fixture plus `runProgram` (offline stand-in for Mode A `response_format` / GBNF). `docs/llm/few-shot/29_runprogram_in_step.malda` is `step result = runProgram(prog)` with the plan JSON as workflow input. Example: `Examples/Workflows/runprogram_in_step.malda`. Produce the plan before `startWorkflow`; do not `await` in the workflow body (replay would re-prompt; not WF1001). JavaScript: n/a (prompts / `api` are host-only).

#### Added (MINOR — GGUF GBNF constrained decode)

- **In-process llama.cpp / LLamaSharp typed prompts:** Mode A and Mode C extract compile the resolved `-> Type` JSON Schema (schema, sum type, or `program(Api)`) to a GBNF grammar and set `DefaultSamplingPipeline.Grammar`. Local GGUF sampling cannot emit tokens outside that contract. Mode B tool rounds stay unconstrained so the model can emit tool calls. OpenAI-compatible HTTP backends still use `response_format`; the schema appendix and validate/repair loop are unchanged. JavaScript: n/a (prompts are host-only). Converter: `MaldaLang/BuiltIns/JsonSchemaGbnf.cs`.

#### Added (MINOR — JS game tile helpers)

- **G17 `game.drawTiles` / `game.tileAt` / `game.sweepTiles` (JavaScript backend only):** `game.tileAt(cells, col, row, options?)` returns the id at cell coordinates (nested rows or a flat array plus `columns`; floors `col`/`row`; out of range is `out`, default `empty`/`0`). `game.drawTiles(handle, cells, tileW, tileH, options?)` blits non-empty ids from an atlas (`id - firstId`, default `firstId` 1; unready no-op; camera/alpha/blend apply; culls to the view). Options `{ x?, y?, columns?, rows?, empty?, srcW?, srcH?, atlasColumns?, firstId? }`. `game.sweepTiles(x, y, w, h, dx, dy, cells, tileW, tileH, options?)` returns the same `{ hit, t, nx, ny, x, y }` as `sweepRect` against solid cells (default: any id other than `empty`; optional `solids` array; `out` cells can be a solid border). Pure query/sweep (no canvas). Not Tiled/LDtk. Smoke: `Examples/Games/game_tiles_smoke.malda`. Showcase `maldadash` draws with `drawTiles`, queries with `tileAt`, and uses `sweepTiles` for spark bounces. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game tint / setBlend)

- **G16 `game.setBlend` / `drawImageEx` tint (JavaScript backend only):** `game.setBlend(mode)` / `game.getBlend()` set the canvas composite for subsequent world draws. Names: `"alpha"` (default, `source-over`), `"add"` (`lighter`), `"multiply"`, `"screen"`; aliases `"source-over"` / `"lighter"` map to `"alpha"` / `"add"`; unknown / empty → `"alpha"`. `createCanvas` resets to `"alpha"`. `clear()` always composites as `"alpha"` and does not change the current mode. Blend does not affect `setPixel` / `blitPixels`. `game.drawImageEx` options add `{ tint?, tintFill? }`: `tint` is a CSS color on an offscreen copy (omit / empty → no tint); default is multiply (white is identity); `tintFill: true` replaces RGB and keeps alpha (white fill is a hit-flash). Smoke: `Examples/Games/game_sprite_smoke.malda`. Showcase `malda_platform` flashes the player on coin collect and draws an additive spark. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — evalPrompt)

- **`evalPrompt(prompt, fixture, typeName?)`:** offline fixture in/out on a `PromptInstance`. Same JSON extract / `TryValidateReturnType` coerce path as `await prompt … -> Type`, with no LLM, no repair loop, and no gather tool round. Returns `{ ok, data }` or `{ ok: false, error }` (`validate` shape). Success `data` is a **variant** for sum types (unlike `validate`, which leaves a dict). Fixture is a dict/object or an LLM-shaped JSON string (markdown fences allowed). Optional `typeName` overrides `instance.returnType`. `instance.eval(fixture)` is the same helper. Mode C `gather:` instances still carry `returnType` so extract contracts can be fixture-tested. Interpreter and C# transpile agree. JavaScript: n/a (prompts are host-only). Example: `Examples/Prompts/eval_prompt.malda`. Few-shot: `docs/llm/few-shot/27_eval_prompt.malda`.

#### Added (MINOR — `malda new agent`)

- **`malda new agent [directory]`:** fourth template beside `webapi` / `fullstack` / `game`. Emits `tools.malda` (`schema NoteArgs` + `validate` + `cap.confine` / `cap.read`), `app.malda` (host-mints `notes/` with `getProgramDirectory()`, `@Tool("read_note")`, offline demo), `notes/welcome.txt`, optional `tests/cap_tools.test.malda`, and `README.md`. Next step: `malda app.malda` (and `malda test`). No `config/environments`; `--local-first` is ignored. Tokens stay on the host — tool args are a relative path only. Few-shot: [`docs/llm/few-shot/26_tool_cap_read.malda`](../llm/few-shot/26_tool_cap_read.malda). Docs: [`docs/start-here.md`](../start-here.md) path “Build An AI App”. Template: `Templates/agent/`. Interpreter / C# transpile: n/a (host-only scaffold).

#### Changed (MINOR — Mode B structured-if-supported)

- **Mode B (`tools:` + `-> Type`):** send OpenAI `response_format` and the `MALDA_OUTPUT_SCHEMA` appendix together with tools (one LLM call, not a silent Mode C rewrite). If the backend rejects tools+`json_schema`, the host retries once without format (keeps tools) and remembers that backend so later rounds skip the failed request. Mode C `gather:` is unchanged (tool round unconstrained, then a typed extract). Example: `Examples/Prompts/prompt_tools_mode.malda`.

#### Added (PATCH — malda check --json)

- **`malda check`:** parse + IDE/`LanguageService` diagnostics without executing (parser, type hints, schema/sum-type names, interpolation, UI loop, workflow determinism). `--json` emits `{ ok, executed: false, file, errorCount, warningCount, infoCount, diagnostics[] }` with 1-based `line`/`column`. Default type-mismatch severity matches the IDE (Errors); `--strict-types` is the full CLI suite; `--lenient-types` keeps mismatches as warnings. Exit 0 with warnings-only, 1 if any error, 2 on usage/I/O. `malda --check "<code>"` remains syntax-only compiler Validate.

#### Added (MINOR — asVariant)

- **`asVariant(typeName, value)`:** coerce a tagged dict (JSON wire `{ "tag": "Buy", … }`) into a sum-type variant for `match`. `validate("Intent", dict)` still returns the original dict. An already-built variant is returned as-is when its tag belongs to the type. Throws on a non-sum schema, unknown tag, or payload that fails the sum-type JSON Schema (same rules as typed-prompt coerce). Interpreter and C# transpile agree. JavaScript: `mlRuntime.schema.asVariant` (tag + constructor param order; payload-type checks stay as weak as JS `validate`). Example: `Examples/Basics/as_variant.malda`. Few-shot: `docs/llm/few-shot/25_as_variant.malda`.

#### Added (MINOR — JS game.sweepRects)

- **G11 `game.sweepRects` (JavaScript backend only):** `game.sweepRects(x, y, w, h, dx, dy, obstacles)` returns the same `{ hit, t, nx, ny, x, y }` as `sweepRect` against the earliest hit in an array of `{ x, y, w, h }` (ties keep the first in array order). Empty / missing / non-array: miss (`t` 1, end pose). Skip non-objects and `w`/`h` ≤ 0. Pure function: no canvas, no camera. Smoke: `Examples/Games/game_collision_smoke.malda`. Showcase `malda_platform` uses one `plats` array. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game.followCamera)

- **G12 `game.followCamera` (JavaScript backend only):** `game.followCamera(targetX, targetY, viewW, viewH, worldW, worldH, options?)` pans so the target sits at `(screenX, screenY)` then clamps the view inside `(0, 0)–(worldW, worldH)`. Options `{ screenX?, screenY?, snap? }`; default screen is view center; `snap: true` floors pan after clamp; `worldW ≤ viewW` → `camX = 0` (same for Y). Does not change zoom. Requires canvas. Showcase `malda_platform`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS audioPlaySample pan / playbackRate)

- **G13 sample pan / playbackRate (JavaScript backend only):** `game.audioPlaySample(url, volume?, options?)` options are additive `{ loop?, pan?, playbackRate? }`. `pan` clamp `[-1, 1]` (default `0`; ignored if `createStereoPanner` is missing). `playbackRate` default `1`; non-positive / non-finite → `1`; clamp `[0.25, 4]`. Pending plays store pan and rate. Smoke: `Examples/Games/game_audio_sample_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS gamepad release / deadzone)

- **G14 gamepad completeness (JavaScript backend only):** `game.wasGamepadButtonReleased(index, button)` is the release twin of `wasGamepadButtonPressed` (same update-only clock; false in `render`). `game.getGamepadAxis(index, axis, deadzone?)` keeps the two-arg form raw; optional `deadzone` clamp `[0, 1]` returns `0` when `|v| ≤ deadzone` with no radial rescale. Smoke: `Examples/Games/game_input_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Changed (MINOR — JS game starter loop)

- **G15 `malda new game` uses `startFixed`:** `Templates/game/app.malda` calls `game.startFixed(updateGame, renderGame)` (default 60 Hz, max 5 catch-up). Bounce stays on `game.start`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game query / pixel / strokeCircle)

- **G10 2D query helpers (JavaScript backend only):** `game.imageWidth(handle)` / `game.imageHeight(handle)` return the bitmap size, or `0` until ready (no canvas required). `game.getCanvasWidth()` / `game.getCanvasHeight()` are backing-store pixels. `game.measureText(text, font?)` returns `{ width, height }` in unscaled font pixels (ignores camera pan/zoom; height uses canvas bounding boxes when present, else the `px` size in `font`). `game.setPixelated(enabled)` disables smoothing and sets CSS `image-rendering: pixelated` (`createCanvas` resets to browser smoothing). `game.strokeCircle(x, y, radius, color?, width?)` strokes a circle; camera pan and zoom apply (default color `#ffffff`, width `1`). Smoke: `Examples/Games/game_sprite_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game.setCameraZoom)

- **`game.setCameraZoom` (JavaScript backend only):** `game.setCameraZoom(z)` / `game.getCameraZoom()` scale world draws after the camera pan (default `1`; non-positive / non-finite → `1`; clamp `[0.05, 100]`). Sizes, radii, and stroke widths scale. `pushCamera` / `popCamera` stack pan and zoom. `screenToWorld` / `worldToScreen` / `getMouseWorldX` honor zoom (`getMouseX` stays canvas pixels). HUD: push, `setCamera(0, 0)`, `setCameraZoom(1)`, draw, pop. `createCanvas` resets zoom to `1`. Smoke: `Examples/Games/game_sprite_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game camera space / mouse edges)

- **Camera space and mouse edges (JavaScript backend only):** `game.pushCamera()` / `game.popCamera()` stack the current camera (HUD: push, `setCamera(0, 0)`, draw, pop; empty pop is a no-op; `createCanvas` clears the stack). `game.screenToWorld(x, y)` / `game.worldToScreen(x, y)` return `{ x, y }` in canvas pixels vs world. `game.getMouseWorldX()` / `game.getMouseWorldY()` are canvas mouse plus the current camera (`getMouseX` / `getTouches` stay canvas pixels). `game.wasMousePressed(button?)` / `game.wasMouseReleased(button?)` use the same update-only clock as `wasKeyPressed` (default button `0`; first touch still aliases button 0). Overlap / `sweepRect` stay pure numbers. Smoke: `Examples/Games/game_sprite_smoke.malda`, `Examples/Games/game_input_smoke.malda`. Showcase `malda_platform` HUD uses the camera stack; the player faces with `drawImageEx` `flipX`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game.drawImageEx)

- **`game.drawImageEx` (JavaScript backend only):** `game.drawImageEx(handle, x, y, options?)` draws an image (optional atlas `sx`/`sy`/`sw`/`sh`, dest `w`/`h`) with origin `ox`/`oy`, rotation `angle` (radians; canvas Y+ down so positive is clockwise), and `flipX`/`flipY` around that origin. Default origin is dest top-left, so `flipX` without `ox` draws to the left of `x`; in-place facing uses `ox = w / 2`. Unready handles no-op. Camera and `setAlpha` apply. `drawImage` / `drawImageRect` signatures unchanged. Not a sprite object. Smoke: `Examples/Games/game_sprite_smoke.malda`. Showcase `malda_platform` spins coins with it. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game.sweepRect)

- **`game.sweepRect` (JavaScript backend only):** `game.sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh)` returns `{ hit, t, nx, ny, x, y }` at the first contact along the motion delta. Miss: `hit` false, `t` 1, `x`/`y` at the end pose. Hit: `t` in `[0, 1]`, position at impact, `nx`/`ny` pointing out of the obstacle (canvas Y+ down: floor `ny = -1`). Zero/negative sizes miss. Positive-area overlap at the start is `t` 0 plus a minimum-translation normal. Surface contact with parallel motion is not a hit (axis-separated platformer walks work). Pure function: no canvas, no camera. Not a physics engine / tileset / swept circle. Smoke: `Examples/Games/game_collision_smoke.malda`. Showcase `malda_platform` uses axis-separated sweep instead of the 18px landing slop. Interpreter / C# transpile: n/a (`game-canvas`).

#### Clarified (PATCH — docs / tracking only)

- **G11–G15 2D plan specified:** [`docs/roadmap-games.md`](../roadmap-games.md) expands the ranked post-G10 slices from draft one-liners to full workstream contracts (calls, guardrails, smoke, files, ship order). G11–G15 later landed (see Unreleased MINOR rows). No Tier 0 semantic change.

- **Maldanoid rally polish:** `Examples/Games/maldanoid.malda` prefers the incoming velocity axis and ignores the last brick for a few ticks, serves near-vertical and builds speed on the paddle, pauses from serve, draws HP bars and shaped power-ups, and saves `{ high, bestCombo }`. No Tier 0 semantic change.

- **Maldanoid feel pass:** `Examples/Games/maldanoid.malda` now depenetrates after brick hits, caps the ball trail, punches `setCamera` with sparks, aims the serve from paddle/touch, follows the first touch, and draws pause/result panels. BGM decode errors stay off the playfield. Still primitive-draw. No Tier 0 semantic change.

- **Maldanoid uses the post-kit 2D loop:** `Examples/Games/maldanoid.malda` stays on the primitive-draw track (no sprites) but now calls `game.startFixed`, `overlapRect` / `overlapCircle`, `wasKeyPressed`, `getTouches` / gamepad helpers, G1 `drawLine` / `strokeRect` / `setAlpha`, and `game.save` / `load` for the high score. Bounce remains the minimal `game.start` loop. No Tier 0 semantic change.

- **Games kit G0–G9 landed:** [`docs/roadmap-games.md`](../roadmap-games.md) is no longer a forward plan. Status, architecture docs layout, `llms.txt`, and the games examples README now say G0–G9 shipped (2026-08-21). Remaining ideas sit in After G9 / non-goals. No Tier 0 semantic change.

#### Added (MINOR — fullstack game scores template)

- **G9 `malda new game --fullstack`:** flag on the G6 `game` template (alias `malda new game-fullstack`) emits one `.malda` with `@client()` canvas (`startFixed`, key edges, AABB, `game.save`/`load`) plus `@GET` / `@POST` `/api/scores` and `schema Score` / `validate("Score", …)`. Server list is in-memory (top 10). Next step is `malda compile app.malda --mode fullstack -o dist` then run the server with `MALDA_WEB_DIRECTORY` pointing at `dist/web`. `malda play` refuses fullstack sources. Host prompts / LLM NPCs stay README commentary. Docs: [`docs/start-here.md`](../start-here.md) path “Build a Browser Game”. Template: `Templates/game-fullstack/`.

#### Fixed (PATCH — RestServer JSON object returns)

- **Transpiled RestServer handlers** that return MALDA object literals now serialize as JSON objects. C# emit uses `Dictionary<string, object?>`; RestServer previously dropped that to HTTP `null`. HttpServer already converted dictionaries; both now share `WebRuntimeHelpers.ConvertTranspiledResultToRuntimeValue`. Needed for G9 `/api/health` and `/api/scores`.

#### Added (MINOR — JS three.* textures / glTF / look-at)

- **G8 `three.createTexture` / `three.loadGLTF` / `three.lookAt` (JavaScript backend only):** `createTexture(url)` returns a handle immediately (async decode; missing files stay unready). `createStandardMaterial` accepts `"map"` (texture handle) and leaves the map unset until ready. `loadGLTF(url)` returns a group you can `add` immediately; children appear when `modelIsReady(handle)` is true (JSON `.gltf` or `.glb`; failures stay unready). The runtime owns the loader (no extra host `<script>`). `lookAt(object, x, y, z)` requires `lookAt` on the three.js object. Orbit controls stay out. Example: `Examples/Games/three_textured.malda`. Interpreter / C# transpile: n/a (`three.*`).

#### Added (MINOR — JS game kit showcase)

- **G7 showcase `malda_platform`:** featured side-scroller that uses G1–G5 together (`loadImage` / `drawImageRect`, `setCamera`, `overlapRect`, `wasKeyPressed`, `audioPlaySample`, `startFixed`, `save`/`load`) and compiles `--mode js`. Bounce / Maldanoid / Maldadash stay. Example: `Examples/Games/malda_platform.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — `malda new game` / `malda play`)

- **G6 CLI scaffolding + preview:** `malda new game [directory]` is a third template beside `webapi` / `fullstack`. It emits `app.malda`, `index.html` (runtime then compiled script), `README.md`, and optional `assets/`. Next step: `malda play app.malda`. No `config/environments` and `--local-first` is ignored. `malda play <file.malda>` compiles `--mode js` into a sibling `.malda-play/` folder, copies `malda-js-runtime.js` and `assets/` when present, serves a host page, and prints a local URL (`--open` may launch a browser; Ctrl+C stops). PWA packaging remains `malda compile --mode pwa`. Interpreter / C# transpile: n/a (`game-canvas`). Docs: [`docs/start-here.md`](../start-here.md) path “Build a Browser Game”.

#### Added (MINOR — JS game fixed timestep + save)

- **G5 `game.startFixed` / `game.save` / `game.load` / `game.removeSave` (JavaScript backend only):** `startFixed(updateFn, renderFn?, tickMs?)` defaults `tickMs` to `1000 / 60`, accrues wall time, calls `update(tickMs)` zero or more times per rAF (max 5 catch-up, then drop remainder), then `render` once. Mutually exclusive with `game.start`. `save`/`load`/`removeSave` store JSON in origin-scoped `localStorage` under `malda.game.` (not files). Quota / missing storage / corrupt JSON: save no-ops, load returns `null`. Example: `Examples/Games/game_fixed_save_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game sample SFX)

- **G4 `game.audioPlaySample` / `game.audioStopSample` (JavaScript backend only):** overlapping WAV/OGG one-shots through the shared Web Audio graph. `audioPlaySample(url, volume?, options?)` decodes once per URL (`volume` default `1`, clamp `[0, 1]`; `{ loop: true }` repeats) and returns `null`. `audioStopSample(url?)` stops samples only — not the v1 HTML-audio track, pattern, or tones. Failed fetch/decode and empty URL no-op. Samples share the 32-node cap. Audio Spec v1 signatures unchanged. Example: `Examples/Games/game_audio_sample_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game collision helpers)

- **G3 `game.*` overlap queries (JavaScript backend only):** `game.overlapRect(x1, y1, w1, h1, x2, y2, w2, h2)` is inclusive AABB (touching edges count). `game.overlapCircle(x1, y1, r1, x2, y2, r2)`. `game.pointInRect` / `game.pointInCircle`. Width, height, or radius ≤ 0 → `false`. Pure functions: no canvas, no camera offset. Not swept AABB or physics. Example: `Examples/Games/game_collision_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — JS game input edges)

- **G2 `game.*` key edges, touches, and gamepad (JavaScript backend only):** `game.wasKeyPressed(key)` / `game.wasKeyReleased(key)` are true on the first `update` after key-down / key-up (same names as `isKeyDown`). `game.getTouches()` returns `[{ id, x, y }]` in canvas pixels. `game.isGamepadConnected(index?)`, `game.getGamepadAxis(index, axis)`, `game.isGamepadButtonDown(index, button)`, `game.wasGamepadButtonPressed(index, button)`. Edges are snapshotted at the start of `update` and are **false in `render`**. First touch still aliases mouse button 0. Missing Gamepad API → disconnected / axis `0`. `game.stop()` clears keys and button edges. Example: `Examples/Games/game_input_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`). `three.*` input is unchanged.

#### Added (MINOR — JS game sprites / camera)

- **G1 `game.*` images and camera (JavaScript backend only):** `game.loadImage(url)` returns a handle immediately (async decode; missing files stay unready and do not throw). `game.imageIsReady`, `game.drawImage`, `game.drawImageRect` blit bitmaps / atlas frames. `game.drawLine` / `game.strokeRect` / `game.setAlpha`. `game.setCamera` offsets world draws (`fillRect`, images, text, lines) and does **not** move `setPixel` / `blitPixels`. Example: `Examples/Games/game_sprite_smoke.malda`. Interpreter / C# transpile: n/a (`game-canvas`).

#### Added (MINOR — result/option bind)

- **`result.andThen` / `option.andThen`:** sequencing helpers beside `map`. `result.andThen(r, fn)` calls `fn(payload)` when `r` is `Ok` and returns that Result unchanged; `Err` skips `fn`. `fn` must return `Ok`/`Err` (a bare payload is an error — use `result.map`). Same contract on `option` with `Some`/`None`. Pipe-friendly: `parse(raw) |> result.andThen(validate)`. Interpreter, C# transpile, and JS agree. Conformance: `result-andthen-chain.malda`. Narrative: [`13-built-in-functions.html`](../../ReferenceManual/13-built-in-functions.html) §13.19.1.

#### Added (MINOR — optional api parameter types)

- **`api` method params:** optional `SchemaType` hints, same form as sum-type constructor payloads (`function add(a: number, b: number)`). Name-only remains valid and permissive. Declared types feed program JSON Schema (narrow the `args` union; always keep `string` for `"$alias"`) and coercion (`"2"` becomes a number only when the hint is `number`/`int`; a `string` hint keeps `"2"`). Prompt parameters stay name-only. Implementing `function` bodies stay untyped. Grammar: [`35-grammar.html`](../../ReferenceManual/35-grammar.html) `ApiMethodSig`; narrative: [`10-prompts.html`](../../ReferenceManual/10-prompts.html) §10.8. Example: `Examples/Prompts/api_program_calc.malda`.

#### Fixed (PATCH — interpreter task isolation)

- **Overlapping `async` + `sleep`:** hot-started user functions no longer share the interpreter environment, `this`, or execution/call/defer stacks. `var tA = async computeA(); var tB = async computeB();` when both callees `sleep` binds on the caller and keeps callee locals. `WrapCallAsTask` now wraps user-function `async` (not only builtins). Spec §11.1; example `Examples/Basics/async_all_example.malda`; tests `AsyncTaskIsolationTests`.

#### Fixed (PATCH — C# destructure temps)

- **Two destructuring bindings in one C# method:** the C# transpiler now suffixes `__destructureValue` / `__destructureArr` (and rest temps) so `var [a, b] = …;` followed by `var { name } = …;` compiles. JS already uniquified. Pair: `InterpretTranspilePairTests.Destructuring_SameStdout`.

#### Fixed (PATCH — program JSON argument types)

- **`program(Api)` / `runProgram`:** LLM program JSON no longer passes leftover objects or numeric strings through as api operands. The host flattens nested `{call,args}` (and TypeChat `@func`/`@args`/`@ref`/`@steps`), coerces `"2"` to a number, unwraps `{type,value}` wrappers, fills missing `@api` / `as` / `return`, then validates. Structured-output schema for `args` no longer includes `object` (that union made models emit wrappers). Leftover objects fail validation/repair instead of reaching `add`/`mul`. Example: `Examples/Prompts/api_program_calc.malda`. Narrative: [`ReferenceManual/10-prompts.html`](../../ReferenceManual/10-prompts.html) §10.8.

- **`program(Api)` call names / structured output:** unique aliases map onto declared methods (`add`/`+` → `_add` when that is the only add-like method), bare `t0` becomes `"$t0"`, and `response_format` rewrites `@api` → `api`, strips `x-malda-*`, and turns `type: [string, number, …]` into `anyOf` so OpenAI-compatible providers keep structured output instead of falling back to free-form JSON. The schema appendix example uses the real method names (including a leading underscore).

#### Added (MINOR — match case guards)

- **`case Pattern if expr:`** optional guard on `match` arms, same `if` word as `catch (e if …)`. Pattern binds first; a falsy guard skips the arm and tries the next case. Interpreter, C# transpile, and JS agree on boolean predicates. Under `--strict-types`, a guarded arm does not cover a variant or count as a catch-all. Conformance: `match-guard.malda`. Grammar: [`35-grammar.html`](../../ReferenceManual/35-grammar.html); narrative: [`08-control-structures.html`](../../ReferenceManual/08-control-structures.html).

#### Removed (MAJOR — function keyword aliases)

- **`fn` / `def`:** no longer tokenize as `function`. The parser rejects them with *`fn`/`def` is not a function keyword. Use `function`.* Previously accepted programs that used the aliases are now syntax errors. IDE no longer offers `fn`/`def` completions. Spec §3 / grammar `FunctionDecl` / reserved-word lists updated.

#### Added (MINOR — capability tokens)

- **L6:** namespaced helper `cap.fileRead(path)` / `cap.fileWrite(path)` / `cap.dirList(path)` mints an unforgeable capability token (`kind`, `path`). `cap.read` / `cap.write` / `cap.list` consume matching tokens only — strings and object literals are rejected, so a tool cannot invent a path. `cap.is(value, kind?)` and `cap.confine(token, relativePath)` inspect and attenuate. `io.readFile` / `io.writeFile` / `io.listDirectory` also accept a matching token. No flat `cap()` alias and no new keyword. Interpreter and C# transpile agree. JS: mint / `is` / `confine` only (file consume is host-only). Example: `Examples/Tools/capability_tokens.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Added (MINOR — grounded values)

- **L5:** namespaced helper `grounded.wrap(value, citations?)` returns `{ value, citations, sourced }` with citations `{ source, id?, span? }`. No flat `grounded()` alias and no new keyword (v2 `grounded<T>` / match-visible kind stays gated). Opt-in GraphMemory ASK: `memory.ask(query, maxResults?, options?)` (or `query(..., { grounded: true })`) wraps hits with citations from `filePath` / `source` / `nodeId`. Interpreter and C# transpile agree. JS: `grounded.wrap` only (GraphMemory is host-only). Example: `Examples/Memory/grounded_ask.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Added (MINOR — workflow call-graph determinism)

- **L4:** WF1001/WF1002 apply when a deny-listed built-in runs in a deterministic workflow section **even if nested in a helper**. IDE/LSP walks same-file `function` callees (bounded depth) outside `step` / `onReject`; imported or unknown callees are **WF1005 Info**, not a hard error. Interpreter and C# transpile agree (reuse in-workflow / in-step flags). JS: n/a (workflows are host-only). Not Temporal-style history comparison; durability remains single-writer SQLite ([`docs/workflows-ha.md`](../workflows-ha.md)). Example: `Examples/Workflows/determinism_helpers.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Added (MINOR — resource budget decorator)

- **L3:** `@budget(tokens: N, tools: N, cost: N?)` beside `@within(ms)` on functions and prompts. Named keys only; unknown keys are errors under `--strict-types` (`malda-bounds`). Runtime aborts with a dedicated message when a bound trips. `tokens` is prompt+completion when the backend reports usage, otherwise a documented chars/4 best-effort count. `tools` is invocation count in that prompt/agent turn, not allow-list length. Optional `cost` when the backend exposes it. Interpreter and C# transpile agree. JS: n/a (prompts are host-only). `MALDA_AGENT_CONTEXT_BUDGET_TOKENS` remains a context-trim fallback for undeclared agents, not a second abort API. Example: `Examples/Prompts/prompt_budget.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Added (MINOR — gather-then-extract prompts)

- **L2:** `gather: ["tool", …]` on a `prompt` with `-> Type` is Mode C in one declaration: a tool round, then a **fresh** typed prompt without tools (Mode A validate/repair). Plain `tools:` + `-> Type` stays Mode B (no silent two-call reinterpret). `gather:` requires `-> Type` (schema, sum type, or `program(Api)`) and cannot combine with `tools:`. Offline `prompt(...)` without `await` does not call the model. Example: `Examples/Prompts/prompt_tools_then_structured.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

#### Added (MINOR — primary constructors)

- **`class Name(params)`:** parameter list after the class name desugars to public fields plus a synthesized constructor. Body optional (`class Point(x, y);` or `{ methods }`). Cannot combine with `extends` or an explicit `function Name(...)`. Grammar: [`35-grammar.html`](../../ReferenceManual/35-grammar.html); narrative: [`11-classes-objects.html`](../../ReferenceManual/11-classes-objects.html) §10.11.

#### Added (MINOR — additive module syntax)

- **Selective imports:** `import { a, b } from "path.malda"` / `from package` — merge only named export-surface bindings; missing names error. Design: [`docs/selective-imports.md`](../selective-imports.md); example `Examples/Modules/selective_import.malda`.
- **`export type` / `export schema`:** same export surface as values; `export type T` includes constructors; selective import expands type↔ctors; IDE/transpile gate types/schemas when the module uses any `export`. Example: `Examples/Modules/export_type_schema.malda`.

#### Added (MINOR — schema / sum-type validate)

- **L1a:** `validate("Intent", value)` resolves sum-type names against the existing tagged `oneOf` schema. Schema fields may name a sum type (`intent: Intent` / `Intent[]`). Success still returns the original dict (no variant coercion). Exclusive names unchanged. Example: `Examples/Basics/schema_sumtype_validate.malda`. Plan: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).
- **L1b:** optional types on variant constructor payloads: `type Intent = Search(query: string) | Buy(sku: string, qty: int)`. Name-only constructors remain valid. Generated JSON Schema uses those field types (primitives, `[]`, schema/sum names). Prompt parameters stay name-only. Example: `Examples/Basics/sumtype_typed_payloads.malda`.

#### Clarified (PATCH — docs / tracking only)

- **Closed `api` / `program(Api)` / `runProgram`:** already shipped (v0.1.50). `api Name { function m(params); }` plus `prompt … -> program(Name)` validates TypeChat-style JSON (`@api`, `steps[{call,args,as}]`, `return`); `runProgram` executes those steps with no further LLM calls. Interpreter and C# transpile agree. JS: n/a (prompts are host-only; JS transpile rejects `api`). Example: `Examples/Prompts/api_program_calc.malda`. Narrative: [`ReferenceManual/10-prompts.html`](../../ReferenceManual/10-prompts.html) §10.8.
- **`typeOf(variant)` / `typeOf(task)`:** already return `"variant"` / `"task"` (Tier 0 T0-096/T0-097); removed stale post-Final gap bullet. Overlapping `async` + `sleep` between `var` bindings is isolated (see Unreleased PATCH — interpreter task isolation).
- **Post-Final language constructs plan:** ranked workstreams L1–L6 (schema/sum-type unification, gather-then-extract prompts, `@budget`, workflow call-graph determinism, grounded values, capability tokens). Tracking only — no Tier 0 semantic change. See [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).
- **Trust plan:** ranked workstreams DT0–DT6 (strict compile as the ship boundary, transpile smoke, loud gotchas). DT6 landed: toolchain **1.0.0** ([`docs/releases/v1.0.0.md`](../releases/v1.0.0.md)). Tracking only — no Tier 0 semantic change. See [`docs/roadmap-trust.md`](../roadmap-trust.md).
- **Games platform plan:** ranked workstreams G0–G9 (JS-only `game.*` / `three.*` kit: sprites, input edges, overlap helpers, sample SFX, `malda play`, 3D assets, fullstack scores). **G0–G9 landed** (2026-08-21). Tracking only — no Tier 0 semantic change. See [`docs/roadmap-games.md`](../roadmap-games.md).

#### Clarified (PATCH — product / Tier-2 docs only; no Tier 0 semantic change)

- **A1 tools vs `response_format`:** exclusivity = no OpenAI `response_format` and no `MALDA_OUTPUT_SCHEMA` appendix when the prompt lists `tools:` (Mode B); `await` + `-> Type` still validates/repairs. Mode C is `gather:` + `-> Type` (one declaration). Supported modes A/B/C documented in [`docs/llm/malda-gotchas.md`](../llm/malda-gotchas.md), [`ReferenceManual/10-prompts.html`](../../ReferenceManual/10-prompts.html) §10.5, and `Examples/Prompts/prompt_tools_*.malda`.
- **DT2 compile gate / DT4 loud gotchas:** `malda compile --mode transpile` and `publish` run `StrictTypesAnalysis` (`Enabled`) and refuse emit on Errors; `--lenient-types` skips. `malda run` stays opt-in `--strict-types`. IDE/LSP `malda-interp` warns on plain `{ident}` strings outside prompt bodies. `parseJson` / `parseJSON` arity errors name the other builtin. Plan: [`docs/roadmap-trust.md`](../roadmap-trust.md). No Tier 0 semantic change.

### [1.0.0] — 2026-08-12 (Final)

**Status:** Final 1.0 declared 2026-08-12. Tier 0 conformance green on interpreter + C# (`scripts/run-tier0-conformance.ps1`: 316 passed, 0 failed). JavaScript Tier 0 remains a separate matrix subset and is **not** Final-gated.

#### Added (shipped under Draft; absorbed into Final without Tier 0 semantic change)

- **CI:** `scripts/verify-spec-parser-drift.ps1`, `scripts/verify-spec-guards.ps1`, `SpecParserDriftGuardTests`, `bitbucket-pipelines.yml` (Phase 2.4).
- **Phase 4.2:** Canonical `typeOf` tags (`int`, `bool`, `dict`, `variant`, `task`, …); `isTag()` with legacy alias matching; `Tier0TypeTags` — see [phase-4.2-type-tags.md](../planning/phase-4.2-type-tags.md).
- **Phase 4.3:** `malda run` / script execution flag `--strict-types`; unknown type-hint errors; non-exhaustive sum-type `match` errors (`malda-match`) — see [phase-4.3-strict-types.md](../planning/phase-4.3-strict-types.md).
- **Phase 4.4:** `result.*` and `option.*` stdlib (`map`, `unwrapOr`, tag tests); null-conditional `?.` / `?[]` — see [phase-4.4-result-option.md](../planning/phase-4.4-result-option.md).
- **Phase 4.5:** Tagged catch `catch (e if condition)` with ordered clause matching — see [phase-4.5-tagged-catch.md](../planning/phase-4.5-tagged-catch.md).

#### Deprecated (Release N = 2026-06 core distribution)

| Surface | Replacement | Removal target |
|---------|-------------|----------------|
| `typeOf` comparison to `"integer"` / `"boolean"` | `"int"` / `"bool"` or `isTag(x, "integer")` during window | Next **MAJOR** after deprecation release |
| Expecting `typeOf(dict)` → `"object"` | `"dict"` or `isTag(x, "dict")` | Same |

#### Final checklist (completed 2026-08-12) — Draft 1.0 → Final

- [x] Tier 0 conformance green on interpreter + C# (`scripts/run-tier0-conformance.ps1` / matrix thresholds) — verified 2026-08-12 (316 passed)
- [x] T1 operator + selected Tier-1 builtin return inference shipped (IDE analysis)
- [x] T2 IDE/LSP type Errors by default + opt-out (`malda.types.strict` / Desktop menu)
- [x] T3 nested schema resolve + IDE field diagnostics (`malda-schema`)
- [x] Remaining draft gaps below marked defer-post-Final with owner/version

Implementation plan: [`docs/roadmap-p0-types-impl.md`](../roadmap-p0-types-impl.md).

#### Known gaps (defer post-Final — do not block Final 1.0)

- Multi-backend product parity (agents/HTTP/workflows on JS) — **not Final-gated**; owner **maintainers**; Tier 0 JS tracked separately via the backend matrix.

#### Closed post-Final (already shipped at Final)

- `typeOf(variant)` / `typeOf(task)` return canonical kind tags `"variant"` / `"task"` (not `"unknown"`); Tier 0 T0-096 / T0-097. Constructor tags stay in `match`, not `typeOf`.
- Concurrent `async` + `sleep` between consecutive `var` bindings: interpreter per-task `InterpreterActivation` (Unreleased PATCH). See spec §11.1.

### [1.0.0-draft] — 2026-06-04 (Phase 3 modules)

#### Added (MINOR — additive)

- Keywords `import` and `export`; file and package import with isolated module environments.  
- Spec §14 and [phase-3-modules-design.md](../planning/phase-3-modules-design.md).  
- Grammar: `ImportStmt`, `ExportableDecl` in [35-grammar.html](../../ReferenceManual/35-grammar.html).

#### Implementation (Phase 3.2)

- `ModuleLoader.LoadFileModuleAsync`, `ModuleExports` filtering, `ImportStatement` in parser/interpreter.

### [1.0.0-draft] — 2026-06-04

**Status:** Historical Draft entry. Superseded by **[1.0.0] Final** (2026-08-12).

#### Added (normative documentation)

- Initial [malda-language-1.0.md](malda-language-1.0.md): value model, null, truthiness, `match`, sum types, `async`/`await`/`all`, actors, `typeOf`/`isNumber`, dictionary missing-key → `null`.  
- Expanded [35-grammar.html](../../ReferenceManual/35-grammar.html) (Phase 2.2).  
- This CHANGELOG and semver policy (Phase 2.3).

#### Implementation alignment (already shipped in toolchain)

- **Pack isolation (2026-06-03):** optional vertical-pack symbols removed from `BuiltInRegistry`. Spec: out of Tier 0.  
- **Phase 1.1:** optional-pack bootstrap auto-globals removed from core.  
- **Phase 1.2:** `math`, `str`, `io` namespaces; flat globals deprecated (one-release policy).  
- **Interpreter:** `WrapCallAsTask` for `async userFn()` environment binding (2026-06-04).

#### P0 readiness notes (2026-08-12) — types / schema (landed before Final)

- **Call-site checking:** IDE/LSP default elevates type mismatches to **Error** (`StrictTypesOptions.Default` / `malda.types.strict`); covers literals, hinted ids, `new`, call `-> T` (same unit + imports), operators (when both sides inferable), and selected Tier-1 builtin returns (`math` / `str` / `io`). CLI `--strict-types` remains explicit and also enables match/`@pure`/bounds/const.  
- **Nested schemas:** field types may name other schemas (`Other` / `Other[]`); unknown field types and cycles error on resolve; IDE `malda-schema` diagnostics on unknown field types; import + `validate` covered by tests.  
- **Workflow (minimal):** WF1001 denies `now` / `random*` / `randn` / `sleep`; WF1002 denies filesystem/process/HTTP built-ins outside `step`; IDE/LSP static WF1001/WF1002 on direct calls; durability remains single-box SQLite + fixed deny-list (not Temporal replay detection).

### Pre-spec baseline (reference only)

| Date | Event | Spec impact |
|------|--------|-------------|
| 2026-06-04 | Phase 0 inventory + Tier 0 test skeleton | Informed Draft 1.0 |
| 2026-06-04 | Phase 1 clean core DoD | Reinforced Tier 0 / Tier 1 boundary |
| 2026-06-03 | Optional vertical packs moved out of core registry | Tier 2 split |

---

## Revision history (this file)

| Date | Change |
|------|--------|
| 2026-06-04 | Initial CHANGELOG and semver policy (Phase 2.3) |
| 2026-06-04 | Phase 2.4: parser/spec drift CI script and Bitbucket pipeline |
| 2026-08-12 | P0 readiness notes: call-site return hints, nested schemas, WF1001 aliases |
| 2026-08-12 | Final checklist + T1/T2/T3 type maturity updates; link `roadmap-p0-types-impl.md` |
| 2026-08-12 | Declared **Final 1.0**; Tier 0 green (316); post-Final gaps owned (maintainers) |
| 2026-08-12 | A1: tools vs `response_format` Modes A/B/C clarified (PATCH docs; Unreleased) |
| 2026-08-14 | Link post-Final language constructs plan (`docs/roadmap-language-constructs.md`; PATCH docs) |
| 2026-08-15 | Trust plan (`docs/roadmap-trust.md`; PATCH docs). Toolchain 1.0 gated on DT2-B + DT3. |
| 2026-08-15 | MAJOR: `fn` / `def` removed; only `function` remains |
| 2026-08-14 | L1a: `validate` + nested schema fields resolve sum-type names (MINOR) |
| 2026-08-14 | L1b: optional constructor payload types in JSON Schema emit (MINOR) |
| 2026-08-21 | PATCH: overlapping `async` + `sleep` interpreter task isolation |
| 2026-08-21 | Games platform plan (`docs/roadmap-games.md`; PATCH docs). G0 only. |
| 2026-08-22 | Games kit G0–G9 marked landed (`docs/roadmap-games.md`; PATCH docs). |
| 2026-08-22 | PATCH: Maldanoid example adopts G1/G2/G3/G5 helpers (`startFixed`, overlap, input edges, save/load) |
| 2026-08-22 | PATCH: Maldanoid feel pass (depenetration, sparks, serve aim, result panels) |
| 2026-08-25 | MINOR: `asVariant(typeName, value)` coerces tagged dicts to sum-type variants |
| 2026-08-26 | MINOR: Mode B sends `response_format` with tools (retry/remember if rejected) |
| 2026-08-27 | MINOR: `malda new agent` cap-confined tool scaffold |
| 2026-08-27 | MINOR: `evalPrompt(prompt, fixture)` offline PromptInstance harness |
| 2026-09-01 | MINOR: `@MCPTool` / `@Tool` host-validates attached-schema args; `MCPServer.callTool` |
| 2026-08-31 | MINOR: `@MCPTool` / `@Tool` third argument accepts a MALDA schema name |
| 2026-08-28 | PATCH: `program(Api)` few-shots + `runProgram` in a durable `step` |
| 2026-08-21 | MINOR: JS `game.loadImage` / camera / draw extras (G1) |
| 2026-08-21 | MINOR: JS `game.wasKeyPressed` / touches / gamepad (G2) |
| 2026-08-21 | MINOR: JS `game.overlapRect` / circle / point queries (G3) |
| 2026-08-21 | MINOR: JS `game.audioPlaySample` / `audioStopSample` (G4) |
| 2026-08-21 | MINOR: JS `game.startFixed` / `save` / `load` / `removeSave` (G5) |
| 2026-08-21 | MINOR: `malda new game` / `malda play` JS preview (G6) |
| 2026-08-21 | MINOR: JS game kit showcase `malda_platform` (G7) |
| 2026-08-21 | MINOR: JS `three.createTexture` / `loadGLTF` / `lookAt` (G8) |
| 2026-08-25 | MINOR: JS `game.sweepRect` swept AABB helper |
| 2026-08-25 | MINOR: JS `game.drawImageEx` flip / rotate / origin blit |
| 2026-08-25 | MINOR: JS camera space / `wasMousePressed` / `pushCamera` |
| 2026-08-25 | MINOR: JS `game.setCameraZoom` world-draw scale |
| 2026-08-25 | MINOR: JS `game.sweepRects` multi-obstacle sweep (G11) |
| 2026-08-25 | MINOR: JS `game.followCamera` follow clamp (G12) |
| 2026-08-25 | MINOR: JS `audioPlaySample` pan / playbackRate (G13) |
| 2026-08-25 | MINOR: JS `wasGamepadButtonReleased` / axis deadzone (G14) |
| 2026-08-25 | MINOR: `malda new game` uses `startFixed` (G15) |
| 2026-08-27 | MINOR: JS `game.setBlend` / `drawImageEx` tint / `tintFill` (G16) |
| 2026-08-27 | MINOR: JS `game.drawTiles` / `tileAt` / `sweepTiles` (G17) |
