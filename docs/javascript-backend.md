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
  - `malda compile <input> --mode esm` / `--target esm` (ES module, default `.mjs`; `CompileToJavaScriptModule`)
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
  - file `import` / selective import are inlined before JS emit (`ModuleSymbolResolver.ExpandFileImportsForTranspile`); a selective import keeps the imported file's `import js` statements
  - `import js NAME = "m";` / `import js { a } from "m";` / `import js global g;` -> module-scope `let` bindings (`JsTranspiler.Interop.cs`). Globals are read from `globalThis` when the script loads; modules load in `__maldaJsReady` through `mlRuntime.js.importModule(() => import("m"), "m")` (the `import()` is in the generated file, so relative specifiers resolve against it) and named exports through `mlRuntime.js.requireExport`. `main()` is `async` and awaits `__maldaJsReady` first. Calls on a chain rooted at a binding are emitted plain (no `arrayAppend` / `callArrayMethod` / `callDictMethod` rewrite); a binding named like a runtime module (`math`, `three`, …) shadows it. `new A.B(…)` emits the dotted path. Reference: [Reference Manual 28.11](../ReferenceManual/28-browser-javascript-backend.html#js-interop)
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

## ES Module Output (`--mode esm`)

- `JsTranspiler.ModuleFormat = JsModuleFormat.EsModule` (`JsTranspiler.Modules.cs`). The module starts with
  `import "./malda-js-runtime.js";` (`RuntimeModuleSpecifier`), has no IIFE, ends with `await main();` and
  `export { … }`. Modules that resolve the runtime to the same file share one `globalThis.mlRuntime`.
- Exports follow the MALDA `import` rule: `export`-marked functions / classes / vars / types (their
  variant constructors) if the file has any, else every top-level function, class, actor, var and
  variant constructor. Only declarations whose `SourceFile` is the compiled file count, so files
  inlined by `import "lib.malda"` are not re-exported (`TargetPartitioner` keeps `SourceFile` on the
  functions and classes it clones). Exported vars are module-scope `let`s, assigned when `main()` runs.
- `index.html` imports the module with `<script type="module">` and reports the outcome; there is no
  `MaldaApp.main()` call. `import js` uses the same `__maldaJsReady` loader as the script output.

## Numeric Variables (operator emit)

- `JsTranspiler.NumericInference.cs` finds the names that hold a JavaScript number wherever they are
  visible: every `var` and every assignment of the name anywhere in the program has a value that is a
  number by construction (numeric literal, `- * / %`, `+` of two numbers, `int()` / `float()`, a `math`
  function that always returns a number, another numeric name), and the name is never a parameter,
  loop / catch / pattern variable, field, declaration or import. By name over the whole program, to a
  fixpoint: conservative, no flow analysis.
- Operands that are numbers that way are emitted bare (`i < n`, `a + b`, `a === b`); the others go
  through `__maldaFloat` (the local alias of `mlRuntime.coerceToFloat`). Semantics do not change:
  `coerceToFloat` returns a number unchanged. `BackendDifferentialFuzzTests` covers it.

## Buffers (`buf.*`)

- `mlRuntime.buf` (`bufStdLib`) creates `Float64Array` / `Float32Array` / `Int32Array` / `Uint8Array`; the rules
  and messages match `MaldaLang/BuiltIns/TypedBufferInstance.cs` and `BufStdLib.cs` word for word.
  `getIndex` / `setIndex` keep the array path small (V8 inlines them) and send buffers to
  `getIndexOther` / `setIndexOther`; `formatValue`, `typeOf`, `length`, `toJSON` and the array / dict
  method entry points know buffers.
- `JsTranspiler.NumericInference.cs` also finds buffer variables (every definition is a buffer constructor,
  `buf.from` / `slice` / `fill` / `fromUtf8` / `fromBase64`, or another buffer variable). `b[i]` on them is
  emitted as `__maldaBufGet` / `__maldaBufSet` (`mlRuntime.bufGet` / `bufSet`: a typed array reads
  `undefined` exactly when a numeric index is out of range or not an integer) and counts as a number.

## Worker Actors (`@worker()`)

- `spawn W(…)` of a `@worker()` actor emits `mlRuntime.actors.spawnWorker(__maldaWorkerScope, "W", [args])`.
  `__maldaWorkerScope` (`JsTranspiler.Actors.cs`, `EmitWorkerScope`) is a self-contained function: the runtime
  aliases, a copy of the emitted types, functions, actors and classes, `let` for the hoisted top-level names,
  the top-level const initializers, and `return { "W": W, … }`. It must reach nothing but `globalThis`, since
  the runtime ships its source text to the worker.
- The runtime wrapper is the named function `maldaJsRuntime`; `createWorkerPort` builds the worker source as
  `(maldaJsRuntime source)(globalThis)` + the scope + `mlRuntime.actors.runWorker(__maldaWorkerScope)`, and starts
  it from a blob URL (`new Worker`, works from `file://`) or `worker_threads` with `eval: true` under Node.
- Protocol: main → worker `{ t: "spawn", actor, args }`, `{ t: "send", handler, args, corr }`; worker → main
  `{ t: "reply", corr, value }`, `{ t: "print", text }` (`io.print` goes through `printSink` in a worker),
  `{ t: "error", message }`. The main thread keeps a proxy cell (`cell.port`) for the worker; callbacks and
  timeouts stay on the sender's side exactly as for local actors. `toWire` checks and copies a message
  (dict markers become a visible `__maldaWireDict` field, `buf.transfer` buffers go into the transfer list),
  `fromWire` restores it; the rules and wording match `WorkerBoundary` (interpreter) and
  `ActorsRuntime.CrossWorkerBoundary` (C#). `WorkerActorRules` (`MaldaLang/Runtime/`) is the shared pre-check.
- `stop` / `shutdownAsync` terminate workers, so Node exits after `main`.
- Start-up cost: each worker parses the whole runtime again (about 0.5 s for the full ~500 KB file in Node; the trimmed
  copy beside a program is smaller). Messages queue in the worker's port until `runWorker` listens.

## Runtime Areas (trimmed runtime)

- `malda-js-runtime.js` marks optional members of the runtime object with
  `// @malda-runtime-area NAME` / `// @malda-runtime-area-end NAME` (today `game` and `three`, about 43% of
  the file). `JsRuntimeAreas.WriteBeside` (`MaldaLang.Compiler/JsRuntimeAreas.cs`) writes the copy beside a
  `--mode js` / `esm` / `pwa` output with only the areas the generated code calls (`mlRuntime.game…`), plus
  the areas an existing runtime in that folder already keeps, and a first line
  `// malda-js-runtime areas: …`. A dropped area becomes `missingRuntimeArea("NAME")`, a Proxy whose
  members throw an error naming the area. A runtime without that header line is a full runtime.
- The repository copy (`Examples/Web/wwwroot/malda-js-runtime.js`) stays full; tests and `MALDA_JS_RUNTIME_PATH`
  use it. `nn.*` is interleaved with `math.*` and stays in the core.
- Adding an area: wrap one member of the `runtime` object in the two markers, add its name to
  `JsRuntimeAreas.All`, and check that nothing outside the markers reaches into it.

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
  - Type helpers: `coerceToInt`, `coerceToFloat`, `coerceToString`, `isTruthy`, `equals`, `isObject`, `objectHasKey`. `isTruthy` matches the interpreter (spec §6): only `null`, `undefined`, and `false` are falsy. `0`, `NaN`, `""`, and empty arrays are truthy.
  - Built-ins: `builtins.print`, `builtins.println`, `builtins.sleep`
  - Stdlib: `math.*`, `str.*`, `io.print`, `io.input` (also flat `input`)
  - Collections / conversions: `sortBuiltin`, `reverse`, `rangeBuiltin`, `joinBuiltin`, `toCsv`, `toIntOr`, `toIntOrNull`, `formatNumber`, `isString`, `isArray`, `isObjectBuiltin`, `urlEncode`, `urlDecode`, `assert`, `expect` (interpreter semantics; exceptions in [`spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md))
  - JSON: `parseJSON`, `parseJson`, `toJSON` (System.Text.Json escaping and .NET number text)
  - Schema: `schema.register`, `schema.registerSumType`, `schema.validate`, `schema.asVariant`
  - Date/env: `now`, `formatDate`, `parseDate`, `addDays`, `addHours`, `getEnv`, `getEnvOr`, `hasEnv`
  - HTTP client: `http.get|post|put|delete|patch` (`fetch`; not `HttpServer`)
  - Bounds: `within.run` / `within.check`
  - JavaScript interop: `js.importModule(load, specifier)` (adds the specifier to a load failure), `js.requireExport(namespace, name, specifier)` (falls back to `module.exports` for CommonJS)
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
    - Audio: `game.audioInit()`, `game.audioIsReady()`, `game.audioSetMasterVolume(v)`, `game.audioPlayTone(...)`, `game.audioPlayNoise(...)`, `game.audioPlayPattern(pattern)`, `game.audioStopPattern()`, `game.audioPlaySample(url, volume?, options?)`, `game.audioStopSample(url?)`, `game.audioLoadTrack(...)`, `game.audioPlayTrack()`, `game.audioStopTrack()`, `game.audioCreateVoice(options?)`, `game.audioSetVoice(voice, options)`, `game.audioStopVoice(voice)`, `game.audioSetListener(position, forward?, up?)`, `game.audioStopAll()`
  - Three helpers:
    - Renderer/lifecycle: `three.createRenderer(width, height, mountSelector?)`, `three.setClearColor(renderer, color)`, `three.setRendererSize(renderer, width, height)`, `three.setRenderScale(renderer, scale)`, `three.setAutoRenderScale(renderer, targetFps?, minScale?, maxScale?)`, `three.getRenderScale()`, `three.getStats()`, `three.showStats(visible?)`, `three.start(updateFn, renderFn?)`, `three.startFixed(updateFn, renderFn?, tickMs?)`, `three.getFixedAlpha()`, `three.stop()`
    - Scene graph: `three.createScene()`, `three.createPerspectiveCamera(fovDeg, aspect, near, far)`, `three.createOrthographicCamera(left, right, top, bottom, near, far)`, `three.setCameraAspect(camera, aspect)`, `three.createGroup()`, `three.createMesh(geometry, material)`, `three.createInstancedMesh(geometry, material, count)`, `three.setInstance(mesh, index, x, y, z, options?)`, `three.setInstanceCount(mesh, count)`, `three.add(parent, child)`, `three.loadGLTF(url)`, `three.modelIsReady(handle)`
    - Geometry/material/light: `three.createBoxGeometry(width, height, depth)`, `three.createPlaneGeometry(width, height)`, `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`, `three.createCylinderGeometry(radiusTop, radiusBottom?, height, radialSegments?)`, `three.createTexture(url)`, `three.createDataTexture(width, height, pixels, options?)`, `three.updateDataTexture(handle, pixels)`, `three.mandelbrotOrbit(real, imag, maxIter)`, `three.createStandardMaterial(options)`, `three.createShaderMaterial(options)`, `three.setUniform(material, name, value)`, `three.createDirectionalLight(color, intensity)`, `three.createAmbientLight(color?, intensity?)`
    - Transforms/input: `three.setPosition(object, x, y, z)`, `three.setRotation(object, x, y, z)`, `three.setScale(object, x, y, z)`, `three.lookAt(object, x, y, z, up?)`, `three.render(renderer, scene, camera, options?)`, `three.isKeyDown(key)`, `three.wasKeyPressed(key)`, `three.wasKeyReleased(key)`, `three.getMouseX()`, `three.getMouseY()`, `three.isMouseDown(button?)`, `three.wasMousePressed(button?)`, `three.wasMouseReleased(button?)`, `three.isGamepadConnected(index?)`, `three.getGamepadAxis(index, axis, deadzone?)`, `three.isGamepadButtonDown(index, button)`, `three.wasGamepadButtonPressed(index, button)`, `three.wasGamepadButtonReleased(index, button)`
    - Buffers, picking, controls, render targets: `three.createBufferGeometry(attributes, options?)`, `three.updateGeometry(geometry, name, values?)`, `three.getAttribute(geometry, name)`, `three.setDrawRange(geometry, start, count)`, `three.createLines(geometry, options?)`, `three.createPoints(geometry, options?)`, `three.pick(camera, target, x, y, options?)`, `three.raycast(target, origin, direction, options?)`, `three.pickId(renderer, camera, scene, x, y)`, `three.createOrbitControls(camera, options?)`, `three.orbitControls(controls, azimuth, polar)`, `three.panControls(controls, dx, dy)`, `three.zoomControls(controls, factor)`, `three.setControlsTarget(controls, x, y, z)`, `three.setControlsEnabled(controls, enabled)`, `three.controlsMoved(controls)`, `three.getControlsCamera(controls)`, `three.disposeControls(controls)`, `three.fitToBounds(controlsOrCamera, object, margin?)`, `three.setProjection(controls, kind)`, `three.createRenderTarget(width, height, options?)`, `three.setTargetSize(target, width, height)`, `three.renderToTarget(renderer, target, scene, camera, options?)`, `three.getTargetTexture(target)`, `three.readPixels(renderer, target, x?, y?, width?, height?)`, `three.screenshot(renderer, scene, camera, type?)` — [Reference Manual 28.10.2](../ReferenceManual/28-browser-javascript-backend.html#three-graphics)
    - Shader kernels: `@shader()` plus `glsl.compile`, `glsl.source(names...)` and `glsl.functions()` (JS transpile only). User-facing contract — types, subset, `glsl.compile` keys, IDE rename vs string keys — is [Reference Manual 28.10.1](../ReferenceManual/28-browser-javascript-backend.html#shader-kernels). This is not a fourth execution backend.
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
  - `game.audioCreateVoice(options?)` / `game.audioSetVoice(voice, options)` / `game.audioStopVoice(voice)` — continuous voice (oscillator or looping noise → low-pass → gain → stereo or 3D panner). Options `{ wave?, freq?, volume?, cutoff?, pan?, glide?, position?, refDistance?, rolloff? }`
  - `game.audioSetListener(position, forward?, up?)` — listener for 3D voices and placed samples

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

`malda play` is the inner loop: it compiles `--mode js` into `.malda-play/` next to the source, copies `malda-js-runtime.js` and `assets/` if present, writes a host page, and prints a local URL. It watches the source, sibling `index.html`, and `assets/`, rebuilds in place, and reloads the browser (`--no-watch` serves the first compile only). Ctrl+C stops the server. `--open` may launch a browser. PWA packaging is still `malda compile --mode pwa`. `malda play` serves the preview folder, not the source tree. Fullstack score apps (`malda new game --fullstack`) are not a `play` preview — compile `--mode fullstack` and run the server with `MALDA_WEB_DIRECTORY` (see `Templates/game-fullstack/README.md`).

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
- Voices: `wave` ∈ `sine` / `square` / `triangle` / `sawtooth` (default; unknown names fall back to it) / `noise` (looping 2 s white-noise buffer; `freq` ignored). `freq` 20–20000 Hz (default 110), `volume` 0–1 (default 0; fades in over ~20 ms), `cutoff` 20–20000 Hz low-pass (default 20000), `pan` −1..1, `glide` 0–2 s time constant (default 0.03; `setTargetAtTime`, so values settle exponentially). `audioSetVoice` only touches the keys you pass; `pan` is ignored on a 3D voice and `position` on a stereo one. Whether a voice is 3D is fixed when it is created. `audioStopVoice` fades over ~30 ms, stops 0.2 s later, and disconnects the nodes; later calls on that handle are no-ops. Voices stay outside the 32-source cap; `audioStopAll` stops them; a new `AudioContext` drops them.
- 3D: `position` (on `audioCreateVoice`, `audioSetVoice`, or `audioPlaySample` options) routes through a `PannerNode` with `panningModel: "HRTF"`, `distanceModel: "inverse"`, `refDistance` (default 5), `rolloffFactor` = `rolloff` (default 1), `maxDistance` 10000. `audioSetListener` writes the listener's `positionX`… / `forwardX`… / `upX`… params (or `setPosition` / `setOrientation` on older engines). Use the same world units as the scene; there is no Doppler.
- Use `game.setPixel` + `game.blitPixels()` for full-frame CPU rendering. Do not call `game.fillRect` once per pixel.
- `game.blitPixels(pixels)` requires `pixels.length` to be `bufferWidth * bufferHeight * 3` (RGB) or `* 4` (RGBA). The buffer matches the canvas unless `createPixelBuffer(width, height)` requested another size.
- Do not call `game.createCanvas(...)` while the loop is running; call `game.stop()` first.
- Do not call `game.start(...)` or `game.startFixed(...)` twice without stopping in between. They share one running flag.
- `game.startFixed` always passes `tickMs` into `update` (not the wall-clock frame delta). After a long pause it runs at most 5 catch-up updates and drops the rest.
- `game.save` / `game.load` are origin-scoped browser `localStorage` (`malda.game.` prefix), not files. Quota / missing storage / corrupt JSON: save no-ops, load returns `null`.
- Call `game.stop()` only when a loop is active.
- `malda play` refuses fullstack sources (`@client` plus `@server` or a route). Use `malda new game --fullstack` then `malda compile --mode fullstack`.

See `Examples/Games/game_bounce.malda` and `Examples/Games/game_runtime_smoke_test.html` for a complete playable reference. See `Examples/Games/game_sprite_smoke.malda` for PNG atlas blit, a scrolling camera, `setCameraZoom`, `drawImageEx` flip/rotate/tint/`tintFill`, `setBlend` add/multiply, `pushCamera` HUD, `measureText` / `imageWidth` / `setPixelated` / `strokeCircle`, and a click marker via `getMouseWorldX`. See `Examples/Games/game_input_smoke.malda` for key edges, `wasMousePressed`, touches, and gamepad (including `wasGamepadButtonReleased` and analog deadzone). See `Examples/Games/game_collision_smoke.malda` for AABB overlap plus `sweepRects`. See `Examples/Games/game_tiles_smoke.malda` for `drawTiles` / `tileAt` / `sweepTiles` on a small cave. See `Examples/Games/game_audio_sample_smoke.malda` for overlapping WAV one-shots with pan / `playbackRate` next to a looping pattern. See `Examples/Games/game_fixed_save_smoke.malda` for `startFixed` plus a high score that survives reload. See `Examples/Games/malda_platform.malda` for a short side-scroller that uses the kit together (atlas tiles, `followCamera`, `sweepRects`, key edges, sample SFX, `startFixed`) plus spinning coins, a facing player via `drawImageEx`, coin-hit `tintFill` flash, and an additive spark. See `malda new game --fullstack` (`Templates/game-fullstack/`) for a canvas client plus `schema Score` / `validate` / `httpPost` scores. See `Examples/Games/maldadash.malda` for a Boulder Dash-style tile cave that uses the kit together (`drawTiles` / `tileAt` / `sweepTiles` sparks, `followCamera`, `drawImageEx` flip/tint, `setBlend` add/multiply, key/gamepad/touch, sample SFX, `startFixed`, `save` high score). See `Examples/Games/chess.malda` for a turn-based chess board (click/touch moves, sliding pieces with a knight hop, legal highlights, castling / en passant / queen promotion, checkmate, optional 1–3 ply CPU, `game.save`). See `Examples/Games/ray_tracer.malda` for a CPU ray tracer that fills the pixel buffer and blits it once per frame.

## three.js Quick Start (`three.*`)

Use `three.*` when you want a browser-hosted 3D scene in MALDA JavaScript mode without exposing raw `THREE.*` calls directly in MALDA source.

### API groups

- Renderer/lifecycle:
  - `three.createRenderer(width, height, mountSelector?)`
  - `three.setClearColor(renderer, color)`
  - `three.setRendererSize(renderer, width, height)`
  - `three.setRenderScale(renderer, scale)`, `three.setAutoRenderScale(renderer, targetFps?, minScale?, maxScale?)`, `three.getRenderScale()`
  - `three.getStats()`, `three.showStats(visible?)`
  - `three.start(updateFn, renderFn?)`
  - `three.startFixed(updateFn, renderFn?, tickMs?)`, `three.getFixedAlpha()`
  - `three.stop()`
- Scene graph:
  - `three.createScene()`
  - `three.createPerspectiveCamera(fovDeg, aspect, near, far)`
  - `three.createOrthographicCamera(left, right, top, bottom, near, far)`
  - `three.setCameraAspect(camera, aspect)`
  - `three.createGroup()`
  - `three.createMesh(geometry, material)`
  - `three.createInstancedMesh(geometry, material, count)`, `three.setInstance(mesh, index, x, y, z, options?)`, `three.setInstanceCount(mesh, count)`
  - `three.add(parent, child)`
  - `three.loadGLTF(url)`
  - `three.modelIsReady(handle)`
- Geometry/material/light:
  - `three.createBoxGeometry(width, height, depth)`
  - `three.createPlaneGeometry(width, height)`
  - `three.createSphereGeometry(radius, widthSegments?, heightSegments?)`, `three.createCylinderGeometry(radiusTop, radiusBottom?, height, radialSegments?)`
  - `three.createTexture(url)`
  - `three.createDataTexture(width, height, pixels, options?)`
  - `three.updateDataTexture(handle, pixels)`
  - `three.mandelbrotOrbit(real, imag, maxIter)`
  - `three.createStandardMaterial(options)`
  - `three.createShaderMaterial(options)`
  - `three.setUniform(material, name, value)`
  - `three.createDirectionalLight(color, intensity)`
  - `three.createAmbientLight(color?, intensity?)`
  - `three.setShadows(renderer, enabled)`, `three.setShadow(object, cast, receive?)`, `three.setLightShadow(light, options?)`
  - `three.setFog(scene, color, near, far)`, `three.setFogExp(scene, color, density)`
- Transform/render/input:
  - `three.setPosition(object, x, y, z)`
  - `three.setRotation(object, x, y, z)`
  - `three.setScale(object, x, y, z)`
  - `three.lookAt(object, x, y, z, up?)` — `up` `[x, y, z]` is set on the object before aiming
  - `three.render(renderer, scene, camera, options?)` — `{ clear: false }` draws over the current frame (color and depth kept; `autoClear` is restored after)
  - `three.isKeyDown(key)`, `three.wasKeyPressed(key)`, `three.wasKeyReleased(key)`
  - `three.getMouseX()`, `three.getMouseY()`
  - `three.isMouseDown(button?)`, `three.wasMousePressed(button?)`, `three.wasMouseReleased(button?)`
  - `three.isGamepadConnected(index?)`, `three.getGamepadAxis(index, axis, deadzone?)`, `three.isGamepadButtonDown(index, button)`, `three.wasGamepadButtonPressed(index, button)`, `three.wasGamepadButtonReleased(index, button)`

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
- `three.startFixed` matches `game.startFixed`: fixed `update(dtMs)` ticks (default 60 Hz, clamp 1–1000 ms, max 5 catch-up ticks, then the backlog is dropped) and one `render()` per frame. `three.getFixedAlpha()` is the 0–1 fraction toward the next tick (0 under `three.start`). Input edges (`wasKeyPressed` / `wasMousePressed` / `wasGamepadButtonPressed` and the `Released` forms) follow the `game.*` contract: visible in one `update`, false in `render`. Both loops share one gamepad snapshot helper with `game.*`. `three.stop()` and window blur clear all input.
- Render scale: drawing buffer = renderer size × `min(devicePixelRatio, 2)` × scale (0.25–2); CSS size never changes. `setAutoRenderScale` (defaults 60 fps, 0.5–1) samples fps over 0.5 s windows: under 90% of target → ×0.85 and hold 4 s; at ≥97% for 3 s → +0.05. The first second after a loop start or a `visibilitychange` is skipped. A target above the display rate only ever lowers the scale. `setRenderScale` turns auto off; `createRenderer` resets scale to 1. `getStats()` → `{ fps, frameMs, renderScale, autoScale, width, height, drawCalls, triangles }`. `showStats(true)` / `#malda-stats` in the URL show a fixed overlay at the canvas corner.
- Instancing: `createInstancedMesh` caps `count` at 100000, uses a dynamic instance buffer, and turns frustum culling off (instances spread past the base geometry's bounds). `setInstance` options: `rotation` (Y angle or `[rx, ry, rz]`), `scale` (number or `[sx, sy, sz]`), `color` (`#rrggbb` or `[r, g, b]` 0–1). The first color fills the other instances with white, because three.js starts the color buffer at black. Index outside `0..count-1` throws. `setInstanceCount` clamps to `0..count`.
- Shadows: `setShadows(renderer, on)` (PCF soft), `setLightShadow(light, { extent = 10, near = 0.5, far = extent * 6, mapSize = 1024 rounded to a power of two in 256–4096, bias = -0.0005, normalBias = 0.02 })`, and `setShadow(object, cast, receive = cast)` recursing through children. `lookAt` on a directional or spot light moves `light.target` and updates its matrix, since a light shines at its target, not along its rotation.
- Fog: `setFog(scene, color, near, far)` / `setFogExp(scene, color, density)`; a `null` color clears. Turning the shadow map, a mesh's `receiveShadow`, a light's `castShadow`, or fog on or off flags every material in the scene for a rebuild on the next `three.render`. Changing fog values or shadow settings that are already on does not rebuild. Shader materials do not get fog or shadows.
- Shader + mesh passes: a `createShaderMaterial` fullscreen pass can write `gl_FragDepth` (WebGL2 / GLSL ES 3.00) with the perspective projection `((f + n) / (f - n) - 2fn / ((f - n) d)) * 0.5 + 0.5`, `d` = view-axis distance, 1.0 for misses; drop `"depthWrite": false` on that material. Then `three.render(renderer, meshScene, perspectiveCamera, { clear: false })` depth-tests meshes against it. Copy the shader camera into the mesh camera every frame: position, `lookAt(cam, pos + forward, up)`, the same vertical FOV and aspect. Physical lights (three r155+): a directional light of intensity π and an ambient of intensity π give `albedo * cos` and `albedo`, the usual shader convention.
- Mouse/touch coordinates are renderer units (the `createRenderer` / `setRendererSize` size), not drawing-buffer pixels. Before this change they were backing-store pixels, so on a devicePixelRatio 2 screen `getMouseX()` ran to twice the width.
- Buffer geometry: a `Float32Array` (`buf.float32`) attribute is the geometry's own storage (no copy); float64 buffers and arrays are copied with `Float32Array.from`. The index is copied into a `Uint32Array` and range-checked. `updateGeometry` recomputes bounds after a position or normal change (raycasts and frustum culling read them) and normals when they were computed, not given. `getAttribute` refuses non-float32 storage.
- Picking: `pick` maps renderer units to NDC with the renderer size (or `width` / `height`), calls `updateMatrixWorld(true)` on the camera and targets, and drops hits on hidden objects or under hidden parents. One `THREE.Raycaster` is reused. Line / point thresholds default to 0.1 world units (three.js uses 1). `pickId` swaps every visible mesh / line / points material for a flat `toneMapped: false`, `fog: false` id color (instanced meshes get a temporary `instanceColor` with one id per instance), sets a 1x1 `setViewOffset` on the camera, clears to black, renders into a reused 1x1 linear target, reads the pixel, and restores materials, instance colors, view, clear color, background and override material in a `finally`.
- Orbit controls are written in the runtime (spherical coordinates, no `OrbitControls` add-on): pointer events with capture on the renderer canvas, `wheel` non-passive, `contextmenu` suppressed. They move the camera immediately (no damping), so no per-frame update call is needed. Orthographic zoom changes `camera.zoom`. `setProjection` keeps one camera of each kind per controls.
- Render targets default to `SRGBColorSpace` textures so `readPixels` matches the canvas and a `map` decodes correctly; `"linear": true` keeps linear. `readPixels` flips WebGL's bottom-up rows. `screenshot` renders and calls `toDataURL` in the same task, so it works without `preserveDrawingBuffer`.
- No raw `THREE.*` in MALDA source: use `import js` for classes the wrapper does not cover. `three.createTexture(url)` returns a handle immediately (async decode; missing files stay unready). `createStandardMaterial({ "map": handle })` leaves `map` unset until the handle is ready, then assigns it. `three.createDataTexture(width, height, pixels, options?)` is ready immediately (RGBA; default float32, or `"type": "byte"`). `three.updateDataTexture(handle, pixels)` rewrites that buffer. `three.mandelbrotOrbit(real, imag, maxIter)` is JavaScript-only BigInt: pass **decimal strings** for C (MALDA numbers are IEEE double), walk `Z_{n+1} = Z_n² + C` at 256 fraction bits, and pack `(re, im, alive, 1)` into a 1-row float texture. `createShaderMaterial` / `setUniform` unwrap texture handles for `sampler2D`. `three.loadGLTF(url)` returns a group you can `add` immediately; children appear when `three.modelIsReady(handle)` is true (JSON `.gltf` or `.glb`; failures stay unready). `three.lookAt(object, x, y, z)` requires `lookAt` on the three.js object. Custom GLSL stays `three.createShaderMaterial` / `three.setUniform`. See `Examples/Games/three_textured.malda`.

### Shader materials (`three.createShaderMaterial`)

Use a fullscreen quad plus GLSL when CPU `game.setPixel` is too slow. MALDA owns the loop and uniforms; the fragment shader owns the rays.

- `three.createOrthographicCamera(left, right, top, bottom, near, far)` — typically `(-1, 1, 1, -1, 0, 1)` for an NDC fullscreen pass
- `three.createShaderMaterial({ "vertexShader": vert, "fragmentShader": frag, "uniforms": { ... } })`
  - `vertexShader` and `fragmentShader` are GLSL strings
  - Prefer writing kernels as typed MALDA `@shader()` functions and compiling them with `glsl.compile({ ... })` (JavaScript mode only). Triple-quoted GLSL strings (`"""`, not `$"""`) still work when you need raw GLSL.
  - Plain uniform values are wrapped as `{ value }`. Arrays of length 2/3/4 become `Vector2` / `Vector3` / `Vector4`. `#rrggbb` strings become `Color`. Texture handles from `createTexture` / `createDataTexture` / `mandelbrotOrbit` unwrap to `THREE.Texture` for `sampler2D`.
  - Optional flags: `"depthWrite": false`, `"depthTest": false`, `"transparent": true`
- `three.setUniform(material, name, value)` — update a uniform after creation. Vector uniforms accept arrays.
  - Uniform arrays: declare `"vec3 uCars[8]"` (or `[CAR_COUNT]` with `consts: ["int CAR_COUNT = 8"]`; consts are emitted before uniforms). `createShaderMaterial` reads `uniform T name[N]` from the shader strings, so `setUniform` fills the array instead of building a `Vector4`. Element types: `float` / `int` / `bool` / `vec2-4` / `ivec2-4`. Vector arrays take nested `[[x, y, z], ...]` or flat lists. Missing entries are zero. Updates are in place.

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

The user-facing contract (types, subset, `glsl.compile` keys, IDE rename) is in [Reference Manual 28.10.1](../ReferenceManual/28-browser-javascript-backend.html#shader-kernels). The three.js scene API itself is [28.10](../ReferenceManual/28-browser-javascript-backend.html).

`@shader()` functions are not emitted as JavaScript. They are a typed subset (C-like control flow, GLSL type hints such as `vec3` / `out float`, `math.sqrt` → `sqrt`). Host MALDA still owns the `three.*` loop and uniforms.

See `Examples/Games/three_shader_raytracer.malda` for a realtime GPU ray tracer (spheres, cube, prism, cylinder, cone). Compile:

```bash
malda compile Examples/Games/three_shader_raytracer.malda --mode js -o Examples/Games/three_shader_raytracer.js
```

`Examples/Games/three_shader_billiards.malda` is a playable pool-table showcase on the same path. Host MALDA steps 2D circle physics and pushes `uBall0`–`uBall15` plus cue uniforms; the kernel traces felt, cushions, pockets, numbered balls, and the stick. `[` `]` zoom the orbit camera, `C` (or Stop camera) zeros auto-orbit, and sliders set cushion/ball restitution and felt friction. The compiled program is playable from [Reference Manual 39](../ReferenceManual/39-appendix-gpu-billiards.html). Compile:

```bash
malda compile Examples/Games/three_shader_billiards.malda --mode js -o Examples/Games/three_shader_billiards.js
```

`Examples/Games/three_shader_path_tunnel.malda` is a ShaderToy-style path-marching tunnel (Frostbyte, CC-BY-NC-SA-4.0 — that file is not under the repo dual licence). Compile:

```bash
malda compile Examples/Games/three_shader_path_tunnel.malda --mode js -o Examples/Games/three_shader_path_tunnel.js
```

`Examples/Games/three_shader_mandelbrot.malda` is an infinite Mandelbrot zoom: the host builds a BigInt reference orbit with `three.mandelbrotOrbit` (decimal strings for C), and the `@shader()` kernel applies scaled perturbation (`w_{n+1} = 2 Z_n w_n + s w_n² + p`) so the GPU never adds the pixel offset to the high-precision center. Page sliders set iterate budget (64–2048) and wrap decades (2–32); defaults are 1024 / 8. Hold Space to pause; `[` `]` change speed. Compile:

```bash
malda compile Examples/Games/three_shader_mandelbrot.malda --mode js -o Examples/Games/three_shader_mandelbrot.js
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
  - Stops currently active one-shot/pattern voices, continuous voices from `audioCreateVoice`, and clears scheduler state.
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
- `import js` against real ES / CommonJS / WASM-glue modules in Node: `MaldaLang.Tests/JsInteropTests.cs`
- `--mode esm` modules imported by a JavaScript program in Node: `MaldaLang.Tests/JsEsModuleOutputTests.cs`
- `malda test --target js` (`MaldaLang/Testing/JavaScriptTestRunner.cs`): runs each test file compiled to
  JavaScript in Node, with `.malda` locations through the source map. Tests:
  `MaldaLang.Tests/TestCommandJavaScriptTargetTests.cs`
- Generated-code speed against hand-written JavaScript: `js` / `js-hand` columns of
  `scripts/run-runtime-benchmarks.ps1` ([`docs/benchmarks.md`](benchmarks.md))
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
- Games kit G0–G22 landed (sprites, input edges, overlap, sample SFX, `malda play`, 3D assets, fullstack scores, size queries / pixelated / `strokeCircle`, `sweepRects`, `followCamera`, sample pan/rate, gamepad release + deadzone, `malda new game` on `startFixed`, image tint / `setBlend`, tile helpers, `three.*` uniform arrays, `three.startFixed` + input edges, render scale + stats overlay, instancing / shadows / fog, audio voices + 3D sound). See [`docs/roadmap-games.md`](roadmap-games.md). Post-kit 2D gaps vs Love2D / Pico-8 / Phaser: [`docs/games-2d-gap-analysis.md`](games-2d-gap-analysis.md).

### Current limitations

- Actor support in JS backend is **process-local only** (browser or Node actor refs). Seamless communication with server-side actors is not part of this phase.
- File I/O, agents/prompts, durable workflows, `HttpServer` / UIHost, and .NET interop stay host-only.
  Compile to JavaScript checks for them before emit and lists **every** use with its line,
  column and code (`JS0001`–`JS0013`, e.g. `Line 4:12 - JS0001: readFile(…) is host-only; …`)
  instead of failing at the first unsupported construct or emitting a call that throws in the
  browser. Codes: [`spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md#javascript-pre-check-diagnostics).

## Desktop IDE debug

Programs that call `dom.*`, `game.*`, or `three.*` cannot pause in the interpreter. The Desktop IDE treats them as JavaScript debug targets:

1. Glyph breakpoints in the `.malda` editor (1-based lines, same as interpret mode).
2. F5 transpiles with a VLQ source map, writes `.malda-preview/*.js` + `.map`, and loads Web Preview. Relative `assets/...` URLs are resolved from the open `.malda` file (not the host page). Preview is served from `https://malda.preview/` so `fetch` (glTF) works.
3. WebView2 Chromium debugger (`Debugger.setBreakpointByUrl`) stops on the generated line; the IDE maps back to the MALDA line for highlight, call stack, and locals.
4. Ctrl+F5 / Run opens the same preview without attaching the debugger.

Full-stack sources (`@client()` / `@javascript()` plus `@server()` / `@csharp()` or a route decorator) launch both debuggees: interpret on the host partition and this WebView2 session on the client. Continue / step follow the last pause; Pause stops both. Output uses `[server]` / `[client]` labels. One inspect panel is shown at a time. `@shared()` code can hit either runtime.

Watch expressions and breakpoint conditions on the client are JavaScript. VS Code F5 remains interpret-only (`malda debug-adapter`).
