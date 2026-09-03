# MALDA JavaScript Backend: Architecture and Testing

This document captures the current JavaScript backend design in code and the near-term testing strategy.

## Scope and API Surface

- Backend entry point: `MaldaLang.Compiler/Compiler.cs`
  - `CompilationMode.JavaScript`
  - `CompilationMode.PWA`
  - `CompileToJavaScript(sourcePath, outputPath)`
  - `CompileToPwa(sourcePath, outputDir)`
  - `TranspileToJavaScript(sourcePath)`
  - `TranspileToJavaScriptFromSource(source, sourceFilePath?)`
  - `TranspileToJavaScriptWithSourceMapFromSource(source, sourceFilePath?, generatedFileName?)`
- CLI integration: `MaldaLang/Program.cs`
  - `malda compile <input> --mode js`
  - `malda compile <input> --target js` (alias)
  - `malda compile <input> --mode pwa`
  - `malda compile <input> --target pwa` (alias)
  - `malda new game [directory]`
  - `malda new game [directory] --fullstack` (canvas client + `@GET` / `@POST` scores; compile `--mode fullstack`)
  - `malda play <file.malda>` (JS preview server; not a second packaging format; refuses fullstack sources)
- Template preprocessing: `MaldaLang.Compiler/TemplatePreprocessor.cs`
  - Triggered when input path ends with `.malda.html`
  - Supports `{{ expression }}` and `{% statements %}`
  - Produces MALDA code containing `renderRoot(rootSelector)` and `bootstrap(rootSelector)`

## Generated JavaScript Contract (`JsTranspiler`)

- Transpiler implementation: `MaldaLang.Compiler/JsTranspiler.cs`
- Output contract:
  - Generates a `MaldaApp` module object from an IIFE.
  - Requires `globalThis.mlRuntime` to be available at runtime.
  - Exports CommonJS when `module.exports` exists.
  - Exposes `globalThis.MaldaApp` for direct browser script usage.
  - Always exports `main`; conditionally exports `renderRoot` and `bootstrap` if present in source.
- Built-in mapping (current subset):
  - `print(...)` -> `mlRuntime.builtins.print(...)`
  - `println(...)` -> `mlRuntime.builtins.println(...)`
  - `sleep(...)` -> `mlRuntime.builtins.sleep(...)`
  - `string(...)` / `int(...)` / `float(...)` -> `mlRuntime.coerceToString` / `coerceToInt` / `coerceToFloat`
  - `parseJSON(...)` / `parseJson(...)` / `toJSON(...)` -> `mlRuntime.parseJSON` / `parseJson` / `toJSON`
  - `validate(...)` -> `mlRuntime.schema.validate(...)`
  - `asVariant(...)` -> `mlRuntime.schema.asVariant(...)`
  - `now` / `formatDate` / `parseDate` / `addDays` / `addHours` -> `mlRuntime.*`
  - `getEnv` / `getEnvOr` / `hasEnv` -> `mlRuntime.getEnv*`
  - `httpGet` / `httpPost` / `httpPut` / `httpDelete` / `httpPatch` -> `mlRuntime.http.get|post|put|delete|patch` (`fetch`)
- Stdlib namespaces:
  - `math.*` -> `mlRuntime.math.*`
  - `str.*` -> `mlRuntime.str.*`
  - `io.print` / `io.input` -> `mlRuntime.io.*` (file I/O is not available)
- Language mapping:
  - `$"..."` interpolated strings -> concatenation with `mlRuntime.coerceToString(...)`
  - array/object destructuring -> `mlRuntime.getArray` / `isObject` / `objectHasKey` (mismatch throws)
  - `class Dog extends Animal` / `super(...)` -> JS `class … extends` / `super`
  - `schema Name { … }` -> `mlRuntime.schema.register(...)`
  - `@within(ms)` on functions -> `mlRuntime.within.run(ms, name, …)`
  - file `import` / selective import are inlined before JS emit (`ModuleSymbolResolver.ExpandFileImportsForTranspile`)
- Actor mapping (local JS runtime):
  - `spawn ActorName(...)` -> `mlRuntime.actors.spawn(new ActorName(...))`
  - `send target.handler(args...)` -> `mlRuntime.actors.send(...)`
  - `send ... then (...) { ... } timeout ... catch (...) { ... }` -> `mlRuntime.actors.sendWithCallback(...)`
  - `reply(value)` -> `mlRuntime.actors.reply(value)`
  - `receive()` in actor handlers -> `await mlRuntime.actors.receiveAsync()`
  - `self` -> `mlRuntime.actors.getSelf()`
- DOM mapping (API mode):
  - `dom.*` calls map to `mlRuntime.dom.*`
- Game mapping (canvas API mode):
  - `game.*` calls map to `mlRuntime.game.*`
- Three mapping (3D scene API mode):
  - `three.*` calls map to `mlRuntime.three.*`

## Target Decorators and Partitioning

Single-source fullstack files can use compile-time target decorators so the compiler emits backend-specific artifacts from one `.malda` source:

- `@client()` / `@javascript()`:
  - included in JavaScript output
  - excluded from C# output
- `@server()` / `@csharp()`:
  - included in C# output
  - excluded from JavaScript output
- `@shared()`:
  - included in both C# and JavaScript outputs

Route decorators remain server-side runtime decorators. Declarations marked with `@GET`, `@POST`, `@PUT`, `@DELETE`, `@PATCH`, `@OPTIONS`, `@PAGE`, `@AIPAGE`, `@ACTION`, `@COMPONENT`, or `@LIVE` are treated as server-oriented and do not appear in JavaScript output.

Validation rules currently enforced:

- `@client()` combined with route decorators is invalid.
- `@shared()` should only contain cross-target-safe logic. Keep target-specific APIs (for example direct server/file/database operations or browser-only DOM/storage helpers) in explicitly targeted declarations.

## Runtime Contract (`mlRuntime`)

- Runtime file: `Examples/Web/wwwroot/malda-js-runtime.js`
- Required global symbol: `globalThis.mlRuntime`
- Current helper surface:
  - Type helpers: `coerceToInt`, `coerceToFloat`, `coerceToString`, `isTruthy`, `equals`, `isObject`, `objectHasKey`
  - Built-ins: `builtins.print`, `builtins.println`, `builtins.sleep`
  - Stdlib: `math.*`, `str.*`, `io.print`, `io.input`
  - JSON: `parseJSON`, `parseJson`, `toJSON`
  - Schema: `schema.register`, `schema.registerSumType`, `schema.validate`, `schema.asVariant`
  - Date/env: `now`, `formatDate`, `parseDate`, `addDays`, `addHours`, `getEnv`, `getEnvOr`, `hasEnv`
  - HTTP client: `http.get|post|put|delete|patch` (`fetch`; not `HttpServer`)
  - Bounds: `within.run` / `within.check`
  - Actor runtime:
    - `actors.spawn(actorInstanceOrFactory, ...args)`
    - `actors.send(targetRef, handlerNameOrNull, ...args)`
    - `actors.sendWithCallback(senderRef, targetRef, handlerNameOrNull, callbackFn, timeoutMsOrNull, timeoutErrFnOrNull, ...args)`
    - `actors.reply(value)`, `actors.receiveAsync()`, `actors.getSelf()`
    - `actors.stop(actorRef)`, `actors.shutdownAsync()`, `actors.callActorOrVoidStop(target)`
  - DOM helpers: `dom.query`, `dom.create`, `dom.append`, `dom.clear`, `dom.setText`, `dom.html`, `dom.on`
  - Game helpers:
    - Canvas/lifecycle: `game.createCanvas(width, height, mountSelector?)`, `game.setBackground(color)`, `game.start(updateFn, renderFn?)`, `game.startFixed(updateFn, renderFn?, tickMs?)`, `game.stop()`, `game.save(key, value)`, `game.load(key)`, `game.removeSave(key)`
    - Drawing: `game.clear()`, `game.fillRect(x, y, width, height, color?)`, `game.fillCircle(x, y, radius, color?)`, `game.strokeCircle(x, y, radius, color?, width?)`, `game.drawText(text, x, y, color?, font?)`, `game.measureText(text, font?)`, `game.drawLine(x1, y1, x2, y2, color?, width?)`, `game.strokeRect(x, y, w, h, color?, width?)`, `game.setAlpha(a)`, `game.setBlend(mode)`, `game.getBlend()`, `game.setPixelated(enabled)`, `game.getCanvasWidth()`, `game.getCanvasHeight()`
    - Images / camera: `game.loadImage(url)`, `game.imageIsReady(handle)`, `game.imageWidth(handle)`, `game.imageHeight(handle)`, `game.drawImage(handle, x, y, w?, h?)`, `game.drawImageRect(handle, sx, sy, sw, sh, dx, dy, dw?, dh?)`, `game.drawImageEx(handle, x, y, options?)`, `game.drawTiles(handle, cells, tileW, tileH, options?)`, `game.setCamera(x, y)`, `game.followCamera(targetX, targetY, viewW, viewH, worldW, worldH, options?)`, `game.getCameraX()`, `game.getCameraY()`, `game.setCameraZoom(z)`, `game.getCameraZoom()`, `game.pushCamera()`, `game.popCamera()`, `game.screenToWorld(x, y)`, `game.worldToScreen(x, y)`
    - Pixel buffer / blit: `game.createPixelBuffer(width?, height?)`, `game.setPixel(x, y, r, g, b, a?)`, `game.blitPixels(pixels?, destX?, destY?)`
    - Collision: `game.overlapRect(x1, y1, w1, h1, x2, y2, w2, h2)`, `game.overlapCircle(x1, y1, r1, x2, y2, r2)`, `game.pointInRect(px, py, x, y, w, h)`, `game.pointInCircle(px, py, x, y, r)`, `game.sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh)`, `game.sweepRects(x, y, w, h, dx, dy, obstacles)`, `game.tileAt(cells, col, row, options?)`, `game.sweepTiles(x, y, w, h, dx, dy, cells, tileW, tileH, options?)`
    - Input: `game.isKeyDown(key)`, `game.wasKeyPressed(key)`, `game.wasKeyReleased(key)`, `game.getMouseX()`, `game.getMouseY()`, `game.getMouseWorldX()`, `game.getMouseWorldY()`, `game.isMouseDown(button?)`, `game.wasMousePressed(button?)`, `game.wasMouseReleased(button?)`, `game.getTouches()`, `game.isGamepadConnected(index?)`, `game.getGamepadAxis(index, axis, deadzone?)`, `game.isGamepadButtonDown(index, button)`, `game.wasGamepadButtonPressed(index, button)`, `game.wasGamepadButtonReleased(index, button)`
    - Audio: `game.audioInit()`, `game.audioIsReady()`, `game.audioSetMasterVolume(v)`, `game.audioPlayTone(...)`, `game.audioPlayNoise(...)`, `game.audioPlayPattern(pattern)`, `game.audioStopPattern()`, `game.audioPlaySample(url, volume?, options?)`, `game.audioStopSample(url?)`, `game.audioLoadTrack(...)`, `game.audioPlayTrack()`, `game.audioStopTrack()`, `game.audioStopAll()`
  - Three helpers:
    - Renderer/lifecycle: `three.createRenderer(width, height, mountSelector?)`, `three.setClearColor(renderer, color)`, `three.setRendererSize(renderer, width, height)`, `three.start(updateFn, renderFn?)`, `three.stop()`
    - Scene graph: `three.createScene()`, `three.createPerspectiveCamera(fovDeg, aspect, near, far)`, `three.createOrthographicCamera(left, right, top, bottom, near, far)`, `three.setCameraAspect(camera, aspect)`, `three.createGroup()`, `three.createMesh(geometry, material)`, `three.add(parent, child)`, `three.loadGLTF(url)`, `three.modelIsReady(handle)`
    - Geometry/material/light: `three.createBoxGeometry(width, height, depth)`, `three.createPlaneGeometry(width, height)`, `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`, `three.createTexture(url)`, `three.createStandardMaterial(options)`, `three.createShaderMaterial(options)`, `three.setUniform(material, name, value)`, `three.createDirectionalLight(color, intensity)`, `three.createAmbientLight(color?, intensity?)`
    - Transforms/input: `three.setPosition(object, x, y, z)`, `three.setRotation(object, x, y, z)`, `three.setScale(object, x, y, z)`, `three.lookAt(object, x, y, z)`, `three.render(renderer, scene, camera)`, `three.isKeyDown(key)`, `three.getMouseX()`, `three.getMouseY()`, `three.isMouseDown(button?)`
    - Shader kernels: `@shader()` plus `glsl.compile` (JS transpile only). User-facing contract — types, subset, `glsl.compile` keys, IDE rename vs string keys — is [Reference Manual 26.10.1](../ReferenceManual/26-browser-javascript-backend.html#shader-kernels). This is not a fourth execution backend.
- Browser loading model:
  1. Load `malda-js-runtime.js`
  2. Load compiled MALDA script
  3. Call `MaldaApp.main()` (API mode) and/or `MaldaApp.bootstrap("#app")` (template mode)
- PWA output model:
  - `--mode pwa` / `--target pwa` writes a directory instead of a single file
  - Generated artifacts include `index.html`, `manifest.webmanifest`, `sw.js`, `malda-js-runtime.js`, `<app>.js`, and `<app>.js.map`

## HTML + MALDA Integration

- API mode (`.malda`):
  - Regular MALDA source manipulates DOM through `dom.*` helpers.
- Template mode (`.malda.html`):
  - HTML host with inline MALDA expressions/statements.
  - Compiles through preprocessor + normal MALDA parser + JS transpiler.
  - Result can be mounted via `bootstrap`/`renderRoot`.

## Game Canvas Quick Start (`game.*`)

Use `game.*` when you want a browser-hosted interactive canvas loop in MALDA JavaScript mode.

### API groups

- Canvas/lifecycle:
  - `game.createCanvas(width, height, mountSelector?)`
  - `game.setBackground(color)`
  - `game.start(updateFn, renderFn?)`
  - `game.startFixed(updateFn, renderFn?, tickMs?)` — default `tickMs` = `1000 / 60`; accrue wall `dtMs`, call `update(tickMs)` zero or more times (max 5), then `render` once. Mutually exclusive with `game.start`
  - `game.stop()`
  - `game.save(key, value)` — JSON in origin `localStorage` under `malda.game.`
  - `game.load(key)` — parsed JSON, or `null` if missing/corrupt
  - `game.removeSave(key)`
- Drawing:
  - `game.clear()`
  - `game.fillRect(...)`
  - `game.fillCircle(...)`
  - `game.strokeCircle(x, y, radius, color?, width?)` — stroke; default color `#ffffff`, width `1`. Camera pan and zoom apply (same as `fillCircle` / `strokeRect`)
  - `game.drawText(...)`
  - `game.measureText(text, font?)` — `{ width, height }` in **unscaled** font pixels (ignores camera pan/zoom). Height uses the canvas bounding box when present, else the `px` size in `font` (default `"16px sans-serif"`)
  - `game.drawLine(x1, y1, x2, y2, color?, width?)`
  - `game.strokeRect(x, y, w, h, color?, width?)`
  - `game.setAlpha(a)` — clamp `[0, 1]`; applies to subsequent world draws
  - `game.setBlend(mode)` / `game.getBlend()` — canvas composite for subsequent world draws. Names: `"alpha"` (default, `source-over`), `"add"` (`lighter`), `"multiply"`, `"screen"`. Canvas aliases `"source-over"` / `"lighter"` map to `"alpha"` / `"add"`. Unknown / empty → `"alpha"` (no throw). `createCanvas` resets to `"alpha"`. `clear()` always composites as `"alpha"` and does not change the current mode. Does **not** affect `setPixel` / `blitPixels`.
  - `game.setPixelated(enabled)` — `true` turns off canvas smoothing and sets CSS `image-rendering: pixelated`. Default after `createCanvas` is off (browser smoothing)
  - `game.getCanvasWidth()` / `game.getCanvasHeight()` — backing-store pixels
- Images / camera:
  - `game.loadImage(url)` — returns a handle immediately; decode is async
  - `game.imageIsReady(handle)`
  - `game.imageWidth(handle)` / `game.imageHeight(handle)` — bitmap size, or `0` until ready / on a missing file
  - `game.drawImage(handle, x, y, w?, h?)` — destination size defaults to the bitmap size
  - `game.drawImageRect(handle, sx, sy, sw, sh, dx, dy, dw?, dh?)` — atlas source rect; `dw`/`dh` default to `sw`/`sh`
  - `game.drawImageEx(handle, x, y, options?)` — draw with optional atlas rect, dest size, origin, rotation, flip, and tint. `x`/`y` are the origin in world space (default origin is dest top-left). Options: `{ sx?, sy?, sw?, sh?, w?, h?, ox?, oy?, angle?, flipX?, flipY?, tint?, tintFill? }`. `angle` is radians; canvas Y+ down so positive is clockwise. `flipX`/`flipY` scale around `(ox, oy)` (default `0, 0` — flipX draws to the left of `x`). For in-place facing, set `ox` to `w / 2`. `tint` is a CSS color applied on an offscreen copy (omit / empty → no tint). Default is **multiply** (Love2D `setColor`; white is identity). `tintFill: true` replaces RGB and keeps alpha (`source-in`) — white fill is a hit-flash. Camera, `setAlpha`, and `setBlend` apply to the blit.
  - `game.drawTiles(handle, cells, tileW, tileH, options?)` — blit a 2D id grid from an atlas. `cells` is nested rows (`cells[row][col]`) or a flat array with `columns`. Id `0` is empty (skip) unless `empty` says otherwise. Atlas index is `id - firstId` (default `firstId` 1). Options: `{ x?, y?, columns?, rows?, empty?, srcW?, srcH?, atlasColumns?, firstId? }`. `x`/`y` are the world origin of cell `(0, 0)` (default `0, 0`). `srcW`/`srcH` default to `tileW`/`tileH`. Unready handles no-op. Camera, `setAlpha`, and `setBlend` apply. Culls to the current camera view.
  - `game.setCamera(x, y)` — world origin in screen space; subsequent world draws subtract the camera, then multiply by zoom. Default `(0, 0)`.
  - `game.followCamera(targetX, targetY, viewW, viewH, worldW, worldH, options?)` — pan so `target` sits at `(screenX, screenY)` then clamp the view inside `(0, 0)–(worldW, worldH)`. Options: `{ screenX?, screenY?, snap? }`. Defaults: `screenX = viewW / 2`, `screenY = viewH / 2`. `snap: true` floors pan after clamp. If `worldW ≤ viewW`, `camX = 0` (same for Y). Does not change zoom. Requires canvas.
  - `game.getCameraX()`, `game.getCameraY()`
  - `game.setCameraZoom(z)` / `game.getCameraZoom()` — default `1`. Non-positive / non-finite → `1`. Clamp `[0.05, 100]`. Scales world draw sizes, radii, and stroke widths. `pushCamera` / `popCamera` stack pan **and** zoom.
  - `game.pushCamera()` / `game.popCamera()` — stack the current camera. HUD: `pushCamera`, `setCamera(0, 0)`, `setCameraZoom(1)`, draw, `popCamera`. Empty `popCamera` is a no-op (camera stays). `createCanvas` clears the stack, resets zoom to `1`, and turns pixelated off.
  - `game.screenToWorld(x, y)` / `game.worldToScreen(x, y)` — `{ x, y }`. Screen is canvas pixels; world divides/multiplies the current zoom then adds/subtracts the camera. Overlap helpers still take the numbers you pass (they do not subtract camera).
- Pixel buffer / blit:
  - `game.createPixelBuffer(width?, height?)` — allocate an `ImageData` (defaults to canvas size; filled opaque black)
  - `game.setPixel(x, y, r, g, b, a?)` — write one RGBA pixel (0–255; alpha defaults to 255). Out-of-bounds writes are ignored. Auto-creates the buffer.
  - `game.blitPixels()` — `putImageData` the buffer at `(0, 0)`
  - `game.blitPixels(pixels, destX?, destY?)` — upload a packed RGB (`w*h*3`) or RGBA (`w*h*4`) array, then blit. Destination defaults to `(0, 0)`.
- Collision:
  - `game.overlapRect(x1, y1, w1, h1, x2, y2, w2, h2)` — inclusive AABB; touching edges count. `w`/`h` ≤ 0 → `false`
  - `game.overlapCircle(x1, y1, r1, x2, y2, r2)` — inclusive (distance ≤ r1+r2). `r` ≤ 0 → `false`
  - `game.pointInRect(px, py, x, y, w, h)`, `game.pointInCircle(px, py, x, y, r)`
  - `game.sweepRect(x, y, w, h, dx, dy, ox, oy, ow, oh)` — first contact along the delta. Returns `{ hit, t, nx, ny, x, y }`. Miss: `hit` false, `t` 1, `x`/`y` at the end pose. Hit: `t` in `[0, 1]`, `x`/`y` at impact, `nx`/`ny` the outward normal (canvas Y+ down: floor is `ny = -1`). Zero/negative sizes miss. Already penetrating (positive-area overlap): `t` 0 and a minimum-translation normal. Not a physics engine.
  - `game.sweepRects(x, y, w, h, dx, dy, obstacles)` — same return as `sweepRect` against the earliest hit in `obstacles` (`[{ x, y, w, h }, …]`; ties keep the first). Empty / missing / non-array: miss. Skip non-objects and `w`/`h` ≤ 0. Pure function (no canvas, no camera).
  - `game.tileAt(cells, col, row, options?)` — id at cell coordinates (col = X, row = Y, row 0 at the top). Floors `col`/`row`. Out of range returns `out` (default `empty`, default `0`). Nested rows, or a flat array with `columns`. Options: `{ columns?, rows?, empty?, out? }`. Pure function.
  - `game.sweepTiles(x, y, w, h, dx, dy, cells, tileW, tileH, options?)` — same return as `sweepRect` against solid cells in the grid. Default: any id other than `empty` is solid. Optional `solids` is an array of ids that are solid. Options also take `x`/`y` origin, `columns`/`rows`/`empty`/`out` (same as `tileAt` / `drawTiles`). Out-of-bounds cells use `out`; if that id is solid, the map has a solid border. Pure function (no canvas, no camera). Not a tileset engine or Tiled/LDtk importer.
- Input:
  - `game.isKeyDown("arrowleft")`, `game.isKeyDown("arrowright")`, etc.
  - `game.wasKeyPressed(key)`, `game.wasKeyReleased(key)` — true on the first **update** after key-down / key-up; same names as `isKeyDown`
  - `game.getMouseX()`, `game.getMouseY()` — canvas pixels (not camera-offset)
  - `game.getMouseWorldX()`, `game.getMouseWorldY()` — canvas mouse converted through the current camera pan and zoom
  - `game.isMouseDown(button?)` (defaults to left button when omitted)
  - `game.wasMousePressed(button?)`, `game.wasMouseReleased(button?)` — true on the first **update** after mouse-down / mouse-up; default button `0`. Same clock as `wasKeyPressed`. First touch still aliases button 0, so a new touch also edges `wasMousePressed(0)`.
  - `game.getTouches()` — `[{ id, x, y }]` in canvas pixels; empty if none. First active touch still aliases mouse button 0. Convert with `screenToWorld` when you need world points.
  - `game.isGamepadConnected(index?)` — default index `0`
  - `game.getGamepadAxis(index, axis, deadzone?)` — missing device → `0`; axes clamped `[-1, 1]`. Optional `deadzone` clamp `[0, 1]`: if `|v| ≤ deadzone` return `0` (no radial rescale). Two-arg form is unchanged.
  - `game.isGamepadButtonDown(index, button)`, `game.wasGamepadButtonPressed(index, button)`, `game.wasGamepadButtonReleased(index, button)` — edges use the same clock as keys (false in `render`)
- Audio:
  - `game.audioInit()` — resume the shared `AudioContext` after a user gesture
  - `game.audioPlaySample(url, volume?, options?)` — decode a WAV/OGG once per URL and play a one-shot (or `{ loop: true, pan?, playbackRate? }`). `volume` default `1`, clamp `[0, 1]`. `pan` clamp `[-1, 1]` (default `0`; ignored if `createStereoPanner` is missing). `playbackRate` default `1`; non-positive / non-finite → `1`; clamp `[0.25, 4]`. Returns `null`
  - `game.audioStopSample(url?)` — omit URL to stop every sample. Does **not** stop the v1 track or pattern
  - `game.audioPlayTone` / `game.audioPlayNoise` / `game.audioPlayPattern` / `game.audioLoadTrack` — Audio Spec v1 (unchanged signatures)

### Minimal starter template

```malda
var x = 120;
var y = 140;
var speed = 220; // pixels per second

function update(dtMs) {
    var step = (speed * dtMs) / 1000;
    if (game.isKeyDown("arrowleft")) {
        x = x - step;
    }
    if (game.isKeyDown("arrowright")) {
        x = x + step;
    }
}

function render() {
    game.clear();
    game.fillRect(x, y, 40, 40, "#33cc66");
    game.drawText("Use left/right arrows", 12, 24, "#ffffff", "14px monospace");
}

game.createCanvas(640, 360, "#app");
game.setBackground("#202830");
game.start(update, render);
```

Compile:

```bash
malda play Examples/Games/game_bounce.malda
# or the explicit compile (play wraps this and serves a host page):
malda compile Examples/Games/game_bounce.malda --mode js -o Examples/Games/game_bounce.js
```

`malda play` is the inner loop: it compiles `--mode js` into `.malda-play/` next to the source, copies `malda-js-runtime.js` and `assets/` if present, writes a host page, and prints a local URL. Ctrl+C stops the server. `--open` may launch a browser. PWA packaging is still `malda compile --mode pwa`. `malda play` serves the preview folder, not the source tree. Fullstack score apps (`malda new game --fullstack`) are not a `play` preview — compile `--mode fullstack` and run the server with `MALDA_WEB_DIRECTORY` (see `Templates/game-fullstack/README.md`).

Host page loading order (required):

1. `malda-js-runtime.js`
2. compiled MALDA game script
3. `MaldaApp.main()`

### Event loop best practices (`dt`, scaling)

- Treat `dtMs` as delta-time in milliseconds from `requestAnimationFrame`.
- Convert velocity values expressed in pixels/second using `(speed * dtMs) / 1000`.
- Keep update and render separate: update state in `update(dtMs)`, draw in `render()`.
- Use a conservative clamp on very large frame times when needed to avoid huge jumps after tab/background stalls.

Example clamp:

```malda
function update(dtMs) {
    var dt = dtMs;
    if (dt > 50) {
        dt = 50;
    }
    // physics using dt
}
```

### Guardrails and error conditions

- Call `game.createCanvas(...)` before `game.clear(...)`, draw calls, pixel-buffer calls, camera/alpha/blend/pixelated/measureText/canvas-size/`followCamera`/`drawTiles` calls, or `game.start(...)` / `game.startFixed(...)`. `game.loadImage` / `game.imageIsReady` / `game.imageWidth` / `game.imageHeight`, overlap helpers (`overlapRect`, `overlapCircle`, `pointInRect`, `pointInCircle`), `game.sweepRect` / `game.sweepRects` / `game.tileAt` / `game.sweepTiles`, `game.audio*`, and `game.save` / `game.load` / `game.removeSave` may run without a canvas.
- Unready image handles (still decoding, missing URL, or decode failure) make `drawImage` / `drawImageRect` / `drawImageEx` / `drawTiles` no-op. They do not throw. `imageWidth` / `imageHeight` return `0`.
- `game.setCamera` offsets `fillRect`, `fillCircle`, `strokeCircle`, `drawText`, `drawLine`, `strokeRect`, and image draws (`drawImage`, `drawImageRect`, `drawImageEx`, `drawTiles`). `setCameraZoom` scales those world sizes (default `1`; clamp `[0.05, 100]`; non-positive → `1`). `followCamera` sets pan (and optional integer snap) without changing zoom. Camera does **not** offset `setPixel` / `blitPixels`. `game.measureText` ignores pan and zoom (font pixels at zoom 1 — use it for HUD after `pushCamera` + zoom 1). `getMouseX` / `getMouseY` / `getTouches` stay in canvas pixels; use `getMouseWorldX` / `getMouseWorldY` or `screenToWorld` for world points (they honor zoom). HUD: `pushCamera` + `setCamera(0, 0)` + `setCameraZoom(1)` + draw + `popCamera` (do not leave the camera at origin).
- `game.setPixelated(true)` disables `imageSmoothingEnabled` and sets the canvas CSS `image-rendering` to `pixelated`. `createCanvas` resets it to the browser default (smoothing on). Host HTML may still set `image-rendering` on `#app canvas`; the API is for `malda play` / games that do not inject that CSS.
- `game.setBlend(mode)` sticks until you change it (same as `setAlpha`). Additive sparks: `setBlend("add")`, draw, `setBlend("alpha")`. Night overlay: `setBlend("multiply")` then a dark `fillRect`. Unknown names become `"alpha"`. `clear()` always uses `"alpha"` internally and does not reset the current mode — `createCanvas` does. Blend does **not** affect `setPixel` / `blitPixels`.
- `drawImageEx` `tint` is per-draw (not a global `setColor`). Multiply white is a no-op on RGB; use `tintFill: true` with `"#ffffff"` for a silhouette flash. `tintFill` without `tint` is ignored. Tint is not a sprite object or a second renderer.
- Overlap helpers are inclusive AABB / circle tests in the numbers you pass. They do **not** subtract `setCamera`. Zero or negative width, height, or radius is `false`. They do not stop a fast mover that ends the tick past a thin wall — use `game.sweepRect` / `game.sweepRects` / `game.sweepTiles` for motion. Those are pure functions (no canvas, no camera). Surface contact with parallel motion is not a hit, so walking on a floor while sweeping X works. Not a physics engine. `tileAt` / `sweepTiles` take a 2D id grid (nested rows or a flat array plus `columns`); they are not Tiled/LDtk. A flat array without `columns` is an empty map (`tileAt` returns `out`). Out-of-range cells use `out` (default `0`); pass `"out": WALL` when the cave border should be solid.
- Read `wasKeyPressed` / `wasKeyReleased` / `wasGamepadButtonPressed` / `wasGamepadButtonReleased` / `wasMousePressed` / `wasMouseReleased` in `update` only. Edges are snapshotted at the start of `update` and are **false in `render`**. Holding a key, button, or mouse button does not retrigger.
- First active touch still aliases mouse button 0 so existing `isMouseDown` games keep working. `getTouches()` is canvas pixels.
- Missing Gamepad API or a disconnected pad: `isGamepadConnected` is false and axes are `0`.
- `game.stop()` clears keys, mouse edges, and button edges (no leftover press on the next `start`).
- `game.audioPlaySample` decodes once per URL and caches the buffer. Missing files / decode failures no-op (no throw). Overlapping plays of the same or different URLs are allowed. Options `{ loop?, pan?, playbackRate? }` are additive (`pan` needs `AudioContext.createStereoPanner`; otherwise pan is ignored and the sample still plays). `game.audioStopSample` never stops `audioPlayTrack` / `audioPlayPattern` / tones. `game.stop()` still does **not** implicit-stop audio. Samples share the existing 32-node cap with tones.
- Call `game.audioInit()` after a click or key before expecting audible output (browser autoplay).
- Use `game.setPixel` + `game.blitPixels()` for full-frame CPU rendering. Do not call `game.fillRect` once per pixel.
- `game.blitPixels(pixels)` requires `pixels.length` to be `bufferWidth * bufferHeight * 3` (RGB) or `* 4` (RGBA). The buffer matches the canvas unless `createPixelBuffer(width, height)` requested another size.
- Do not call `game.createCanvas(...)` while the loop is running; call `game.stop()` first.
- Do not call `game.start(...)` or `game.startFixed(...)` twice without stopping in between. They share one running flag.
- `game.startFixed` always passes `tickMs` into `update` (not the wall-clock frame delta). After a long pause it runs at most 5 catch-up updates and drops the rest.
- `game.save` / `game.load` are origin-scoped browser `localStorage` (`malda.game.` prefix), not files. Quota / missing storage / corrupt JSON: save no-ops, load returns `null`.
- Call `game.stop()` only when a loop is active.
- `malda play` refuses fullstack sources (`@client` plus `@server` or a route). Use `malda new game --fullstack` then `malda compile --mode fullstack`.

See `Examples/Games/game_bounce.malda` and `Examples/Games/game_runtime_smoke_test.html` for a complete playable reference. See `Examples/Games/game_sprite_smoke.malda` for PNG atlas blit, a scrolling camera, `setCameraZoom`, `drawImageEx` flip/rotate/tint/`tintFill`, `setBlend` add/multiply, `pushCamera` HUD, `measureText` / `imageWidth` / `setPixelated` / `strokeCircle`, and a click marker via `getMouseWorldX`. See `Examples/Games/game_input_smoke.malda` for key edges, `wasMousePressed`, touches, and gamepad (including `wasGamepadButtonReleased` and analog deadzone). See `Examples/Games/game_collision_smoke.malda` for AABB overlap plus `sweepRects`. See `Examples/Games/game_tiles_smoke.malda` for `drawTiles` / `tileAt` / `sweepTiles` on a small cave. See `Examples/Games/game_audio_sample_smoke.malda` for overlapping WAV one-shots with pan / `playbackRate` next to a looping pattern. See `Examples/Games/game_fixed_save_smoke.malda` for `startFixed` plus a high score that survives reload. See `Examples/Games/malda_platform.malda` for a short side-scroller that uses the kit together (atlas tiles, `followCamera`, `sweepRects`, key edges, sample SFX, `startFixed`) plus spinning coins, a facing player via `drawImageEx`, coin-hit `tintFill` flash, and an additive spark. See `malda new game --fullstack` (`Templates/game-fullstack/`) for a canvas client plus `schema Score` / `validate` / `httpPost` scores. See `Examples/Games/maldadash.malda` for a Boulder Dash-style tile cave that uses the kit together (`drawTiles` / `tileAt` / `sweepTiles` sparks, `followCamera`, `drawImageEx` flip/tint, `setBlend` add/multiply, key/gamepad/touch, sample SFX, `startFixed`, `save` high score). See `Examples/Games/ray_tracer.malda` for a CPU ray tracer that fills the pixel buffer and blits it once per frame.

## three.js Quick Start (`three.*`)

Use `three.*` when you want a browser-hosted 3D scene in MALDA JavaScript mode without exposing raw `THREE.*` calls directly in MALDA source.

### API groups

- Renderer/lifecycle:
  - `three.createRenderer(width, height, mountSelector?)`
  - `three.setClearColor(renderer, color)`
  - `three.setRendererSize(renderer, width, height)`
  - `three.start(updateFn, renderFn?)`
  - `three.stop()`
- Scene graph:
  - `three.createScene()`
  - `three.createPerspectiveCamera(fovDeg, aspect, near, far)`
  - `three.createOrthographicCamera(left, right, top, bottom, near, far)`
  - `three.setCameraAspect(camera, aspect)`
  - `three.createGroup()`
  - `three.createMesh(geometry, material)`
  - `three.add(parent, child)`
  - `three.loadGLTF(url)`
  - `three.modelIsReady(handle)`
- Geometry/material/light:
  - `three.createBoxGeometry(width, height, depth)`
  - `three.createPlaneGeometry(width, height)`
  - `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`
  - `three.createTexture(url)`
  - `three.createStandardMaterial(options)`
  - `three.createShaderMaterial(options)`
  - `three.setUniform(material, name, value)`
  - `three.createDirectionalLight(color, intensity)`
  - `three.createAmbientLight(color?, intensity?)`
- Transform/render/input:
  - `three.setPosition(object, x, y, z)`
  - `three.setRotation(object, x, y, z)`
  - `three.setScale(object, x, y, z)`
  - `three.lookAt(object, x, y, z)`
  - `three.render(renderer, scene, camera)`
  - `three.isKeyDown(key)`
  - `three.getMouseX()`, `three.getMouseY()`
  - `three.isMouseDown(button?)`

### Minimal starter template

```malda
var width = 800;
var height = 500;
var aspect = width / height;

var renderer = three.createRenderer(width, height, "#app");
three.setClearColor(renderer, "#101722");
three.setRendererSize(renderer, width, height);

var scene = three.createScene();
var camera = three.createPerspectiveCamera(70, aspect, 0.1, 100.0);
three.setCameraAspect(camera, aspect);
three.setPosition(camera, 0, 0, 5);

var world = three.createGroup();
three.add(scene, world);

var floorGeometry = three.createPlaneGeometry(8, 8);
var floorMaterial = three.createStandardMaterial({ "color": "#1f2937", "roughness": 0.95, "metalness": 0.0 });
var floor = three.createMesh(floorGeometry, floorMaterial);
three.setRotation(floor, -1.5708, 0.0, 0.0);
three.setPosition(floor, 0.0, -1.1, 0.0);
three.add(world, floor);

var geometry = three.createBoxGeometry(1, 1, 1);
var material = three.createStandardMaterial({ "color": "#44aaff" });
var cube = three.createMesh(geometry, material);
three.add(world, cube);

var sphereGeometry = three.createSphereGeometry(0.35, 24, 16);
var sphereMaterial = three.createStandardMaterial({ "color": "#f59e0b", "roughness": 0.4, "metalness": 0.1 });
var sphere = three.createMesh(sphereGeometry, sphereMaterial);
three.setPosition(sphere, 1.45, 0.15, 0.0);
three.setScale(sphere, 1.0, 1.2, 1.0);
three.add(world, sphere);

var light = three.createDirectionalLight("#ffffff", 1.1);
three.setPosition(light, 2, 3, 4);
three.add(scene, light);

var ambient = three.createAmbientLight("#9bbcff", 0.35);
three.add(scene, ambient);

var angle = 0.0;

function update(dtMs) {
    angle = angle + ((dtMs * 1.2) / 1000);
    three.setRotation(cube, angle, angle * 0.8, 0.0);
}

function render() {
    three.render(renderer, scene, camera);
}

three.start(update, render);
```

Compile:

```bash
malda compile Examples/Games/three_cube.malda --mode js -o Examples/Games/three_cube.js
malda compile Examples/Games/three_textured.malda --mode js -o Examples/Games/three_textured.js
```

Host page loading order (required):

1. `Examples/Web/wwwroot/vendor/three.min.js` (or another compatible browser bundle that defines `globalThis.THREE`)
2. `malda-js-runtime.js`
3. compiled MALDA script
4. `MaldaApp.main()`

See `Examples/Games/three_cube.malda` and `Examples/Games/three_runtime_smoke_test.html` for the MVP reference implementation. See `Examples/Games/three_textured.malda` for a PNG `map` plus a glTF cube and `lookAt`.

### Guardrails and error conditions

- `three.*` is available only in JavaScript mode with a browser host.
- Load `Examples/Web/wwwroot/vendor/three.min.js` before `malda-js-runtime.js` and before the compiled MALDA script.
- Call `three.createRenderer(...)` before `three.render(...)` or `three.start(...)`.
- When you change viewport dimensions after setup, call both `three.setRendererSize(renderer, width, height)` and `three.setCameraAspect(camera, width / height)`.
- Do not call `three.createRenderer(...)` while the loop is running; call `three.stop()` first.
- The curated wrapper still has no orbit controls and no raw `THREE.*` in MALDA source. `three.createTexture(url)` returns a handle immediately (async decode; missing files stay unready). `createStandardMaterial({ "map": handle })` leaves `map` unset until the handle is ready, then assigns it. `three.loadGLTF(url)` returns a group you can `add` immediately; children appear when `three.modelIsReady(handle)` is true (JSON `.gltf` or `.glb`; failures stay unready). `three.lookAt(object, x, y, z)` requires `lookAt` on the three.js object. Custom GLSL stays `three.createShaderMaterial` / `three.setUniform`. See `Examples/Games/three_textured.malda`.

### Shader materials (`three.createShaderMaterial`)

Use a fullscreen quad plus GLSL when CPU `game.setPixel` is too slow. MALDA owns the loop and uniforms; the fragment shader owns the rays.

- `three.createOrthographicCamera(left, right, top, bottom, near, far)` — typically `(-1, 1, 1, -1, 0, 1)` for an NDC fullscreen pass
- `three.createShaderMaterial({ "vertexShader": vert, "fragmentShader": frag, "uniforms": { ... } })`
  - `vertexShader` and `fragmentShader` are GLSL strings
  - Prefer writing kernels as typed MALDA `@shader()` functions and compiling them with `glsl.compile({ ... })` (JavaScript mode only). Triple-quoted GLSL strings (`"""`, not `$"""`) still work when you need raw GLSL.
  - Plain uniform values are wrapped as `{ value }`. Arrays of length 2/3/4 become `Vector2` / `Vector3` / `Vector4`. `#rrggbb` strings become `Color`.
  - Optional flags: `"depthWrite": false`, `"depthTest": false`, `"transparent": true`
- `three.setUniform(material, name, value)` — update a uniform after creation. Vector uniforms accept arrays.

Fullscreen vertex shader (clip-space quad from `PlaneGeometry(2, 2)`), written in MALDA and compiled to GLSL:

```malda
@shader()
function vertexMain() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}

var vert = glsl.compile({
    varyings: ["vec2 vUv"],
    functions: ["vertexMain"],
    main: "vertexMain"
});
```

The user-facing contract (types, subset, `glsl.compile` keys, IDE rename) is in [Reference Manual 26.10.1](../ReferenceManual/26-browser-javascript-backend.html#shader-kernels). The three.js scene API itself is [26.10](../ReferenceManual/26-browser-javascript-backend.html).

`@shader()` functions are not emitted as JavaScript. They are a typed subset (C-like control flow, GLSL type hints such as `vec3` / `out float`, `math.sqrt` → `sqrt`). Host MALDA still owns the `three.*` loop and uniforms.

See `Examples/Games/three_shader_raytracer.malda` for a realtime GPU ray tracer (spheres, cube, prism, cylinder, cone). Compile:

```bash
malda compile Examples/Games/three_shader_raytracer.malda --mode js -o Examples/Games/three_shader_raytracer.js
```

`Examples/Games/three_shader_billiards.malda` is a playable pool-table showcase on the same path. Host MALDA steps 2D circle physics and pushes `uBall0`–`uBall15` plus cue uniforms; the kernel traces felt, cushions, pockets, numbered balls, and the stick. `[` `]` zoom the orbit camera, `C` (or Stop camera) zeros auto-orbit, and sliders set cushion/ball restitution and felt friction. The compiled program is playable from [Reference Manual 37](../ReferenceManual/37-appendix-gpu-billiards.html). Compile:

```bash
malda compile Examples/Games/three_shader_billiards.malda --mode js -o Examples/Games/three_shader_billiards.js
```

`Examples/Games/three_shader_path_tunnel.malda` is a ShaderToy-style path-marching tunnel (Frostbyte, CC-BY-NC-SA-4.0 — that file is not under the repo dual licence). Compile:

```bash
malda compile Examples/Games/three_shader_path_tunnel.malda --mode js -o Examples/Games/three_shader_path_tunnel.js
```

Host page loading order is the same as other `three.*` demos (`three.min.js` first).

## Audio Spec v1 (`game.audio*`)

Audio Spec v1 adds a browser-hosted game audio API in JavaScript mode. The goal is immediate SFX/music value with stable MALDA-facing semantics for MALDA game scripts.

### API methods

- `game.audioInit()`
  - Initializes or resumes a shared `AudioContext`.
  - Idempotent: safe to call multiple times.
  - Returns `null`.
- `game.audioIsReady()`
  - Returns `true` when audio context is initialized and not closed.
  - Returns `false` otherwise.
- `game.audioSetMasterVolume(volume)`
  - Sets shared master gain.
  - `volume` is clamped to `[0.0, 1.0]`.
  - Returns `null`.
- `game.audioPlayTone(freqHz, durationMs, waveType?, volume?)`
  - One-shot oscillator tone with click-free envelope.
  - `freqHz` clamped to `[20, 20000]`.
  - `durationMs` clamped to `[1, 10000]`.
  - `waveType` default: `"square"`. Allowed: `"sine"`, `"square"`, `"triangle"`, `"sawtooth"`.
  - `volume` default: `0.25`, clamped to `[0.0, 1.0]`.
  - Returns `null`.
- `game.audioPlayNoise(durationMs, volume?)`
  - One-shot white-noise source with envelope.
  - `durationMs` clamped to `[1, 10000]`.
  - `volume` default: `0.2`, clamped to `[0.0, 1.0]`.
  - Returns `null`.
- `game.audioStopAll()`
  - Stops currently active one-shot/pattern voices and clears scheduler state.
  - Keeps `AudioContext` alive for reuse.
  - Returns `null`.
- `game.audioPlayPattern(pattern)`
  - Starts simple event-based pattern playback with bounded lookahead scheduling.
  - Pattern fields:
    - `tempoBpm` (default `120`)
    - `loop` (default `false`)
    - `tracks` (array; each track is event list)
    - each event: `{ atBeats, noteHz, durBeats, waveType?, volume? }`
  - Returns `null`.
- `game.audioStopPattern()`
  - Stops the active pattern scheduler.
  - Returns `null`.
- `game.audioLoadTrack(source, options?)`
  - Loads a pre-rendered music track from URL (for example `.ogg`/`.wav`).
  - Options: `{ autoplay?, loop?, volume? }`.
  - Returns `null`.
- `game.audioPlayTrack()`
  - Starts/resumes pre-rendered track playback.
  - Returns `null`.
- `game.audioStopTrack()`
  - Stops pre-rendered track playback and rewinds to start.
  - Returns `null`.
- `game.audioSetTrackOptions(options)`
  - Applies track options (currently `loop`, `volume`).
  - Returns `null`.
- `game.audioTrackIsReady()`
  - Returns `true` when the loaded track can play.
  - Returns `false` otherwise.
- `game.audioGetTrackInfo()`
  - Returns `{ ready, source, playing, loop, volume, backendError }`.
- `game.audioPlaySample(url, volume?, options?)` (additive sample SFX; v1 method signatures above are unchanged)
  - Plays a decoded WAV/OGG (or other `decodeAudioData` format) through the shared Web Audio graph.
  - `volume` default `1`, clamped to `[0.0, 1.0]`. If the second argument is an object, it is treated as `options`.
  - Options: `{ loop?: bool, volume?: number, pan?: number, playbackRate?: number }`. `loop` default `false`. `pan` clamp `[-1, 1]`, default `0` (ignored if `createStereoPanner` is missing). `playbackRate` default `1`; non-positive / non-finite → `1`; clamp `[0.25, 4]`.
  - Returns `null`. Decode once per URL and cache. Overlapping plays are allowed.
  - Empty URL, failed fetch, or failed decode: no-op, no throw.
  - Autoplay-blocked: no-op until `game.audioInit()` succeeds (same as v1).
- `game.audioStopSample(url?)`
  - Stops active sample voices. Omit URL (or pass `""`) to stop every sample.
  - Does **not** stop the v1 HTML-audio track, pattern scheduler, or oscillator/noise tones.
  - Returns `null`.

### Runtime guardrails

- API methods avoid hard throws on normal gameplay misuse; invalid values are clamped or ignored when possible.
- If browser autoplay policy blocks audio (missing user gesture), methods no-op gracefully until `game.audioInit()` succeeds.
- `game.stop()` does not implicitly stop audio; audio lifecycle is explicit via audio API methods.
- A shared node cap limits runaway resource growth during bursty SFX playback. Sample voices count toward the same cap as tones (`maxConcurrentAudioSources` = 32).

### Determinism and scheduling

- Pattern playback uses a short lookahead window with fixed scheduling interval to reduce jitter.
- One-shot nodes are disconnected on end to avoid leaks.
- The API is designed for predictable parameter behavior (explicit defaults and ranges).

### Forward compatibility

- Audio Spec v1 API signatures and parameter semantics are stable and must remain backward compatible.
- Runtime internals may switch audio implementation details without changing v1 MALDA call sites.

## Testing Strategy (Current and Next)

### Current automated coverage

- Test file: `MaldaLang.Tests/JavaScriptBackendTests.cs`
- Interpret/JS stdout pairs: `MaldaLang.Tests/InterpretJsPairTests.cs` (skip if Node or the runtime is missing)
- Covered today:
  - Module shape and runtime dependency assumptions.
  - Built-in function mappings (`print`, `println`, `sleep`, `math.*`, `str.*`, JSON, dates, env, HTTP client).
  - Interpolated strings, destructuring, `class extends` / `super`, `schema` / `validate()`, file `import`.
  - DOM and truthiness/equality helper mappings.
  - `game.*` namespace mapping to `mlRuntime.game.*` (member access + calls).
  - `three.*` namespace mapping to `mlRuntime.three.*` (member access + calls).
  - JavaScript compile mode output path behavior.
  - Template preprocessing happy path and malformed template errors.

### Near-term expansion (planned)

- Run DOM-centric cases under `jsdom` to validate runtime behavior (`dom.query`, event wiring, HTML/text updates).
- Keep C# tests as codegen/contract checks, and add Node/jsdom tests for runtime semantics.
- Games kit G0–G17 landed (sprites, input edges, overlap, sample SFX, `malda play`, 3D assets, fullstack scores, size queries / pixelated / `strokeCircle`, `sweepRects`, `followCamera`, sample pan/rate, gamepad release + deadzone, `malda new game` on `startFixed`, image tint / `setBlend`, tile helpers). See [`docs/roadmap-games.md`](roadmap-games.md). Post-kit 2D gaps vs Love2D / Pico-8 / Phaser: [`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md).

### Current limitations

- Actor support in JS backend is **process-local only** (browser or Node actor refs). Seamless communication with server-side actors is not part of this phase.
- File I/O, agents/prompts, durable workflows, `HttpServer` / UIHost, and .NET interop stay host-only.

## Desktop IDE debug

Programs that call `dom.*`, `game.*`, or `three.*` cannot pause in the interpreter. The Desktop IDE treats them as JavaScript debug targets:

1. Glyph breakpoints in the `.malda` editor (1-based lines, same as interpret mode).
2. F5 transpiles with a VLQ source map, writes `.malda-preview/*.js` + `.map`, and loads Web Preview. Relative `assets/...` URLs are resolved from the open `.malda` file (not the host page). Preview is served from `https://malda.preview/` so `fetch` (glTF) works.
3. WebView2 Chromium debugger (`Debugger.setBreakpointByUrl`) stops on the generated line; the IDE maps back to the MALDA line for highlight, call stack, and locals.
4. Ctrl+F5 / Run opens the same preview without attaching the debugger.

Full-stack sources (`@client()` / `@javascript()` plus `@server()` / `@csharp()` or a route decorator) launch both debuggees: interpret on the host partition and this WebView2 session on the client. Continue / step follow the last pause; Pause stops both. Output uses `[server]` / `[client]` labels. One inspect panel is shown at a time. `@shared()` code can hit either runtime.

Watch expressions and breakpoint conditions on the client are JavaScript. VS Code F5 remains interpret-only (`malda debug-adapter`).
