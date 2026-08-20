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
- CLI integration: `MaldaLang/Program.cs`
  - `malda compile <input> --mode js`
  - `malda compile <input> --target js` (alias)
  - `malda compile <input> --mode pwa`
  - `malda compile <input> --target pwa` (alias)
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
  - Schema: `schema.register`, `schema.registerSumType`, `schema.validate`
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
    - Canvas/lifecycle: `game.createCanvas(width, height, mountSelector?)`, `game.setBackground(color)`, `game.start(updateFn, renderFn?)`, `game.stop()`
    - Drawing: `game.clear()`, `game.fillRect(x, y, width, height, color?)`, `game.fillCircle(x, y, radius, color?)`, `game.drawText(text, x, y, color?, font?)`
    - Pixel buffer / blit: `game.createPixelBuffer(width?, height?)`, `game.setPixel(x, y, r, g, b, a?)`, `game.blitPixels(pixels?, destX?, destY?)`
    - Input: `game.isKeyDown(key)`, `game.getMouseX()`, `game.getMouseY()`, `game.isMouseDown(button?)`
  - Three helpers:
    - Renderer/lifecycle: `three.createRenderer(width, height, mountSelector?)`, `three.setClearColor(renderer, color)`, `three.setRendererSize(renderer, width, height)`, `three.start(updateFn, renderFn?)`, `three.stop()`
    - Scene graph: `three.createScene()`, `three.createPerspectiveCamera(fovDeg, aspect, near, far)`, `three.createOrthographicCamera(left, right, top, bottom, near, far)`, `three.setCameraAspect(camera, aspect)`, `three.createGroup()`, `three.createMesh(geometry, material)`, `three.add(parent, child)`
    - Geometry/material/light: `three.createBoxGeometry(width, height, depth)`, `three.createPlaneGeometry(width, height)`, `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`, `three.createStandardMaterial(options)`, `three.createShaderMaterial(options)`, `three.setUniform(material, name, value)`, `three.createDirectionalLight(color, intensity)`, `three.createAmbientLight(color?, intensity?)`
    - Transforms/input: `three.setPosition(object, x, y, z)`, `three.setRotation(object, x, y, z)`, `three.setScale(object, x, y, z)`, `three.render(renderer, scene, camera)`, `three.isKeyDown(key)`, `three.getMouseX()`, `three.getMouseY()`, `three.isMouseDown(button?)`
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
  - `game.stop()`
- Drawing:
  - `game.clear()`
  - `game.fillRect(...)`
  - `game.fillCircle(...)`
  - `game.drawText(...)`
- Pixel buffer / blit:
  - `game.createPixelBuffer(width?, height?)` — allocate an `ImageData` (defaults to canvas size; filled opaque black)
  - `game.setPixel(x, y, r, g, b, a?)` — write one RGBA pixel (0–255; alpha defaults to 255). Out-of-bounds writes are ignored. Auto-creates the buffer.
  - `game.blitPixels()` — `putImageData` the buffer at `(0, 0)`
  - `game.blitPixels(pixels, destX?, destY?)` — upload a packed RGB (`w*h*3`) or RGBA (`w*h*4`) array, then blit. Destination defaults to `(0, 0)`.
- Input:
  - `game.isKeyDown("arrowleft")`, `game.isKeyDown("arrowright")`, etc.
  - `game.getMouseX()`, `game.getMouseY()`
  - `game.isMouseDown(button?)` (defaults to left button when omitted)

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
malda compile Examples/Web/js/my_game.malda --mode js -o Examples/Web/js/my_game.js
```

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

- Call `game.createCanvas(...)` before `game.clear(...)`, draw calls, pixel-buffer calls, or `game.start(...)`.
- Use `game.setPixel` + `game.blitPixels()` for full-frame CPU rendering. Do not call `game.fillRect` once per pixel.
- `game.blitPixels(pixels)` requires `pixels.length` to be `bufferWidth * bufferHeight * 3` (RGB) or `* 4` (RGBA). The buffer matches the canvas unless `createPixelBuffer(width, height)` requested another size.
- Do not call `game.createCanvas(...)` while the loop is running; call `game.stop()` first.
- Do not call `game.start(...)` twice without stopping in between.
- Call `game.stop()` only when a loop is active.

See `Examples/Web/js/game_bounce.malda` and `Examples/Web/js/game_runtime_smoke_test.html` for a complete playable reference. See `Examples/Web/js/ray_tracer.malda` for a CPU ray tracer that fills the pixel buffer and blits it once per frame.

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
- Geometry/material/light:
  - `three.createBoxGeometry(width, height, depth)`
  - `three.createPlaneGeometry(width, height)`
  - `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`
  - `three.createStandardMaterial(options)`
  - `three.createShaderMaterial(options)`
  - `three.setUniform(material, name, value)`
  - `three.createDirectionalLight(color, intensity)`
  - `three.createAmbientLight(color?, intensity?)`
- Transform/render/input:
  - `three.setPosition(object, x, y, z)`
  - `three.setRotation(object, x, y, z)`
  - `three.setScale(object, x, y, z)`
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
malda compile Examples/Web/js/three_cube.malda --mode js -o Examples/Web/js/three_cube.js
```

Host page loading order (required):

1. `Examples/Web/wwwroot/vendor/three.min.js` (or another compatible browser bundle that defines `globalThis.THREE`)
2. `malda-js-runtime.js`
3. compiled MALDA script
4. `MaldaApp.main()`

See `Examples/Web/js/three_cube.malda` and `Examples/Web/js/three_runtime_smoke_test.html` for the MVP reference implementation.

### Guardrails and error conditions

- `three.*` is available only in JavaScript mode with a browser host.
- Load `Examples/Web/wwwroot/vendor/three.min.js` before `malda-js-runtime.js` and before the compiled MALDA script.
- Call `three.createRenderer(...)` before `three.render(...)` or `three.start(...)`.
- When you change viewport dimensions after setup, call both `three.setRendererSize(renderer, width, height)` and `three.setCameraAspect(camera, width / height)`.
- Do not call `three.createRenderer(...)` while the loop is running; call `three.stop()` first.
- The MVP is intentionally curated: no textures, model loading, orbit controls, or generic raw JS interop are included. Custom GLSL is available only through `three.createShaderMaterial` / `three.setUniform`.

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

See `Examples/Web/js/three_shader_raytracer.malda` for a realtime GPU sphere tracer. Compile:

```bash
malda compile Examples/Web/js/three_shader_raytracer.malda --mode js -o Examples/Web/js/three_shader_raytracer.js
```

`Examples/Web/js/three_shader_path_tunnel.malda` is a ShaderToy-style path-marching tunnel (Frostbyte, CC-BY-NC-SA-4.0 — that file is not under the repo dual licence). Compile:

```bash
malda compile Examples/Web/js/three_shader_path_tunnel.malda --mode js -o Examples/Web/js/three_shader_path_tunnel.js
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

### Runtime guardrails

- API methods avoid hard throws on normal gameplay misuse; invalid values are clamped or ignored when possible.
- If browser autoplay policy blocks audio (missing user gesture), methods no-op gracefully until `game.audioInit()` succeeds.
- `game.stop()` does not implicitly stop audio; audio lifecycle is explicit via audio API methods.
- A shared node cap limits runaway resource growth during bursty SFX playback.

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

### Current limitations

- Actor support in JS backend is **process-local only** (browser or Node actor refs). Seamless communication with server-side actors is not part of this phase.
- File I/O, agents/prompts, durable workflows, `HttpServer` / UIHost, and .NET interop stay host-only.
