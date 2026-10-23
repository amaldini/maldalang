# MALDA JavaScript platform roadmap

**Status:** J0 and J1 landed 2026-10-24; J2–J8 open  
**Created:** 2026-10-23  
**Audience:** maintainers working on `JsTranspiler`, `malda-js-runtime.js`, `JsCapabilityChecker`
and the browser-facing built-ins

The JavaScript backend today runs games and small pages well: the core language, the pure
stdlib, `dom.*`, `game.*`, `three.*`, `@shader()` and process-local actors. A large browser
application — an editor, a CAD or modelling tool, a data or design workbench — needs more than
that: existing JS/WASM libraries, numeric code that runs at native JS speed, background threads,
low-level graphics, a real client-side UI, local storage and files. This plan lists what is
missing and in what order to add it. Each item is useful on its own; none is tied to one
application.

Current backend notes: [`docs/javascript-backend.md`](javascript-backend.md). What runs where:
[`docs/spec/backend-capability-matrix.md`](spec/backend-capability-matrix.md).

**Principles:**

- **Interop first.** No built-in surface covers every browser API or npm package. A safe way to
  call JavaScript (J1) unblocks everything else; built-ins come after, and only where a MALDA
  API is clearly better than calling the library.
- **One program, honest backends.** JS-only features are rejected on the interpreter / C# by a
  pre-check, the way `JsCapabilityChecker` rejects host-only features today. Where a feature has
  a natural host meaning (typed buffers, workers ↔ threads, `ui.*` local mode), all three
  backends implement it and the differential tests cover it.
- **Respect the built-in soft freeze.** Extend `io` / `math` / `str` / `dom` / `three` / `ui`
  before adding a namespace. Every new built-in follows the checklist in `AGENTS.md`.
- **Runtime material.** New files emitted next to JS output (worker entries, runtime modules)
  are covered by `LICENSE-RUNTIME-EXCEPTION` ("any other runtime asset written next to generated
  JavaScript"); a new kind of emitted material updates that list.

---

## Gaps found (2026-10-23)

Found by reading the JS backend and transpiling a small numeric loop.

1. **No JavaScript interop.** A program cannot import an ES module, an npm package or a WASM
   module, call a JS object or pass a MALDA function as a callback. The only browser surface is
   what `mlRuntime` exposes.
2. **No library output.** The output is one IIFE with `main` (plus `renderRoot` / `bootstrap`).
   JS code cannot import a MALDA module and call its functions; there is no ES module output,
   no multiple entry points, and the ~11k-line runtime ships whole.
3. **Numeric code goes through helpers.** `s = s + xs[i] * 2.0` in a loop becomes
   `mlRuntime.coerceToFloat(__maldaGetIndex(xs, i))` and the loop test
   `coerceToFloat(i) < coerceToFloat(mlRuntime.str.length(xs))`, every iteration. There is no typed JS emit
   (the C# backend has `--typed-transpile-level`) and no typed buffers (`Float64Array`,
   `Float32Array`, `Int32Array`, `Uint8Array`) visible to programs.
4. **No binary data.** No bytes value on JS, no little-endian read/write helpers to build or parse
   binary formats.
5. **No background threads.** Actors are process-local on JS: everything runs on the UI thread.
   There are no Web Workers and no way to move a buffer to another thread without copying.
6. **Graphics stop at the scene-graph level.** `three.*` covers boxes, spheres, materials,
   instancing and shader materials. Missing: geometry from buffers (custom meshes, lines,
   points), picking (raycast / ID buffer), camera controls, render targets and pixel readback.
   `@shader()` is compile-time only; shaders assembled at runtime need plain GLSL strings.
7. **No client-side UI framework.** `ui.*` is server-driven and rejected on JS (`JS0008`).
   `dom.*` has seven helpers (`query`, `create`, `append`, `clear`, `setText`, `html`, `on`): no
   attributes, classes or styles API, no pointer capture, wheel, modifiers, resize or focus events.
8. **No local storage or files.** `game.save` / `game.load` (localStorage under `malda.game.`)
   is the only persistence. No IndexedDB, no file picker, no download, no dropped files.
9. **Tooling.** No `@test` run on the JS backend from the CLI, no minified production build, no
   size budget for runtime + program.

---

## Workstreams

| ID | Workstream | Depends on | Size |
|----|------------|-----------|------|
| J0 | Baselines: JS benchmarks, Node test runner | — | S |
| J1 | JavaScript interop (`import js`) | J0 | L |
| J2 | ES module output and libraries | J1 | M |
| J3 | Typed JS emit, typed buffers and bytes | J0 | L |
| J4 | Workers: actors off the main thread | J2, J3 | L |
| J5 | Graphics: buffers, picking, controls, render targets | J1, J3 | M |
| J6 | Client-side UI: `ui.*` on JS, richer `dom.*` | J2 | XL |
| J7 | Browser storage and files | J3 (bytes) | M |
| J8 | Production build and tooling | J2 | M |

Suggested order: J0 → J1 → J3 → J2 → J4 → J7 → J5 → J6, with J8 alongside from J2 on.
J1 and J3 are independent and can run in parallel.

---

## J0 — Baselines

**Landed 2026-10-24.** `malda test --target js` (`MaldaLang/Testing/JavaScriptTestRunner.cs`);
the `js-hand` column, hand-written twins in `benchmarks/runtime/js-baseline/` and the `arrays`
benchmark in `scripts/run-runtime-benchmarks.ps1`. First numbers in
[`docs/benchmarks.md`](benchmarks.md): numeric loops run 6–8× slower than hand-written
JavaScript once Node startup is removed (`floats`, `arrays`), calls and objects 1.2–2×. Source
maps are now written without a BOM (Node could not read them).

Measure before optimizing and test before extending.

- **Benchmarks** in `docs/benchmarks.md` for the JS backend: numeric loops over arrays, nested
  object access, string building, a DOM churn test. Run in Node; record the ratio against
  hand-written JS.
- **Node test runner:** run `@test` functions of a program compiled to JS (`malda test --target js`
  or an equivalent), reusing the Node harness `InterpretJsPairTests` already uses.

**Done when:** the numbers are in `docs/benchmarks.md` and a filtered test runs a `@test` file on
JS.

## J1 — JavaScript interop

**Landed 2026-10-24.** Syntax as below, with `import js NAME = "module";` for the namespace (the
`=` form of existing aliases) and `import js global a, b;` for globals; `js` / `global` are
contextual words. Code: `JsImportStatement`, `Parser.Statements.cs`, `JsTranspiler.Interop.cs`,
`mlRuntime.js`; tests in `JsInteropTests`. Reference: chapter 28.11. Not done yet: MALDA stubs
for the LSP (completion on imported members) and `.d.ts` generation.

The foundation:

```malda
import js THREE = "three";                  // ES module / npm specifier
import js geom = "./geom.wasm.js";          // local module, e.g. a WASM wrapper
import js global window;                    // an existing global

var v = new THREE.Vector3(1, 2, 3);
v.normalize();
var mod = await geom.init();                 // Promise <-> await
el.addEventListener("click", function (e) { io.print(e.clientX); });
```

- **Values.** JS values are opaque `js` handles. Member access, calls, `new` and assignment emit
  plain JS (no `getIndex` helpers). MALDA arrays and dicts are already JS arrays and objects and
  pass through; `null` and `undefined` both read as `null`.
- **Callbacks.** A MALDA function passed to JS is a plain JS function; an `async` one returns a
  Promise.
- **Promises.** `await` on a JS Promise works; a rejected Promise becomes a MALDA error that
  `try` / `catch` sees, with the JS message and stack.
- **WASM.** Loaded through its JS glue module (Emscripten / wasm-bindgen): no special case,
  plus a documented pattern for passing typed buffers (J3) into WASM memory.
- **Backends.** `import js` is JavaScript-only. The interpreter and C# reject it with a
  pre-check diagnostic, and it is allowed in `@client()` declarations of fullstack files.
- **Typing (later).** Optional MALDA stubs for an imported JavaScript module so the LSP can complete members;
  generating them from `.d.ts` is a stretch goal.

**Done when:** a program imports an npm ES module and a WASM module, calls them, passes
callbacks and awaits Promises; `JsInteropTests` covers each case in Node, and the reference
manual chapter 28 documents it.

## J2 — ES module output and libraries

- `--module esm`: emit an ES module with `import` lines for the `import js` modules and the
  runtime, and `export` for the declarations marked to export (keyword or decorator, to decide).
  Keep the IIFE as the default.
- **MALDA as a library:** JS code can `import { fn } from "./lib.js"` and call MALDA functions
  with plain JS arguments.
- **Several entry points** sharing one runtime (a page, a worker in J4, a test entry).
- **Runtime split:** split `malda-js-runtime.js` by area (core, schema, actors, dom, game, three,
  …) and emit only what the program uses; the full file stays for script-tag use.

**Done when:** a page imports two MALDA modules that share one runtime instance, and the runtime
emitted for a `core`-only program is a fraction of today's file.

## J3 — Typed JS emit, typed buffers and bytes

- **Typed emit.** When the type is provable (number literals, typed locals, `for` counters,
  `length` of an array), emit plain JS operators and indexing: no `coerceToFloat`, `getIndex` or `str.length` call per iteration.
  Reuse the levels and the type environment of the C# native numeric work
  ([`docs/native-numeric-rollout.md`](native-numeric-rollout.md)); keep the dynamic path for
  anything mixed. Error behavior (index out of range, string index) stays as specified,
  checked by the differential fuzz.
- **Typed buffers.** Fixed-size numeric arrays: float64, float32, int32, uint8. On JS they are
  `Float64Array` / … ; on the interpreter and C# `double[]` / `float[]` / `int[]` / `byte[]`.
  Same indexing, `length`, slicing and iteration as arrays; out-of-range writes throw on every
  backend. Name and namespace to decide (extend `math` or a small `buf` namespace).
- **Bytes and binary I/O.** A bytes value (`Uint8Array` / `byte[]`) plus little-endian
  read/write of ints and floats at an offset, base64 and UTF-8 encode/decode. This is enough to
  write and parse binary file formats in MALDA.
- Extend `BackendDifferentialFuzzTests` to generate typed-buffer and typed-arithmetic code.

**Done when:** the J0 numeric benchmark runs within 2× of hand-written JS, the fuzz passes
`MALDA_FUZZ_SEEDS=1-200`, and a test writes and reads back a binary format on all three
backends.

## J4 — Workers: actors off the main thread

- `@worker` on an actor (or `spawn worker Actor(...)`, to decide) runs it in a Web Worker on JS
  and on a thread on the host backends, where actors already exist. Code that does not use it
  is unchanged.
- **Messages** cross by structured clone; typed buffers and bytes can be **transferred** (moved,
  not copied) with an explicit marker. A message that cannot cross (a function, a DOM node, a
  `js` handle) is a clear error at send time.
- `send … then` / `reply` / timeouts behave as they do with local actors.
- **Bundling:** the compiler emits a worker entry (J2 entry points) with the actor's code and the
  runtime parts it needs. It works from `file://` (blob URL) and from a server.
- **A pool helper** for data-parallel work (map a function over chunks across N workers), if the
  plain actor API proves too verbose in examples.

**Done when:** an example moves a heavy computation into a worker, the page stays responsive,
and the same program runs on the interpreter with a thread; tests cover transfer, errors and
shutdown.

## J5 — Graphics

Extend `three.*` where a MALDA API is simpler than calling three.js through J1:

- **Geometry from buffers:** positions / normals / indices / colors from typed buffers (J3),
  updated in place; line segments and point clouds.
- **Picking:** raycast against meshes (hit object, point, face index) and an ID-buffer pick for
  large scenes.
- **Camera controls:** orbit / pan / zoom with sensible defaults, fit-to-bounds, orthographic ↔
  perspective switch.
- **Render targets and readback:** render to a texture, read pixels (thumbnails, picking,
  screenshots).
- **Runtime shaders:** `@shader()` functions available as GLSL strings at runtime so a program
  can compose them with generated code into a `three.createShaderMaterial`.
- Raw WebGL2 goes through J1 interop, not a `gl.*` built-in namespace.

**Done when:** an example builds a mesh from computed buffers, picks faces with the mouse and
orbits the view, with Node tests on the fake renderer pattern used by the games kit.

## J6 — Client-side UI

Bring the `ui.*` model into the browser so the same declarative UI runs with no host:

- **`ui.*` local mode on JS.** The tree, diff and patch logic run in the browser: port
  `UiDiffEngine` to the JS runtime (or emit it from the C# source) and reuse `MaldaUiCore` /
  `MaldaUiDom` from `malda-ui-client.js`, which already apply patches to the DOM. Events go
  straight to the program's `ui.run` loop. `UiPatchFuzzTests` gains a JS-diff ↔ C#-diff parity
  check. Builds on [`docs/roadmap-ui-runtime.md`](roadmap-ui-runtime.md).
- **Controls desktop-class apps need:** split panes with resizable separators, menus and context
  menus, a command palette, keyboard shortcuts, property grids, a host slot that embeds a
  canvas or `three` renderer inside the UI tree, modal dialogs.
- **Richer `dom.*`** for code that works below `ui.*`: attributes, classes, styles, pointer events
  with capture, wheel, keyboard modifiers, focus, resize observation, drag and drop, clipboard.
- **i18n hook:** a message-catalog lookup that `ui.*` text can use, with plural and number
  formatting through `Intl`.

**Done when:** a multi-panel example (tree + properties + canvas) runs from `--mode js` with no
host, with the same source as its UIHost version; `JS0008` is lifted for local mode.

## J7 — Browser storage and files

Extend `io` (soft freeze) with browser-safe, async operations:

- **Key-value storage:** a small persistent store (localStorage for small values, IndexedDB for
  large or binary ones) with `get` / `set` / `remove` / `keys`. `game.save` keeps working.
- **Files:** open a file picker and read the result as text or bytes (J3); save or download a
  text / bytes file; receive files dropped on an element. Optional File System Access
  (open-then-save to the same file) where the browser supports it.
- Host backends: the same calls map to the local file system / a store under `./.malda/`, or are
  rejected by the pre-check where there is no sensible meaning (the picker).

**Done when:** an example opens a file, edits it, saves it back and keeps an autosave across
reloads; tests cover the store on Node with a fake IndexedDB.

## J8 — Production build and tooling

- `--minify` for JS / PWA output (via esbuild when available), with source maps kept.
- **Size budget:** report runtime + program size per build; a guard test fails when the runtime
  emitted for a `core`-only program grows past a set size.
- **Capability matrix and pre-check:** every new JS-only feature gets a row and a `JS0xxx` or host
  diagnostic, with a fixture (`BackendCapabilityMatrixGuardTests`).
- **Docs:** chapter 28 of the reference manual, `docs/javascript-backend.md`, and
  `docs/llm/` gotchas for interop pitfalls (opaque `js` values, `null` vs `undefined`,
  what crosses a worker boundary).

---

## Open decisions

1. ~~Syntax for interop~~ (decided in J1: `import js …`); syntax for exports (keyword or decorator) stays open for J2.
2. Name and namespace for typed buffers and bytes (`math`, a new `buf`, or literal types).
3. Worker marker: decorator on the actor or a `spawn` variant.
4. Whether `ui.*` local mode ports the diff to JS by hand or generates it from the C# engine.
5. Whether host backends implement `io` storage / files (J7) or reject them.

## Out of scope

- A new execution backend (WASM output of MALDA itself).
- Native desktop wrappers (Electron, WebView2) — they can host a J-roadmap app unchanged.
- Server-side features: they belong to the host backends.
