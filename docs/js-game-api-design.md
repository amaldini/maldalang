# JavaScript Game API Routing Design

This note confirms the current JavaScript call-routing behavior and defines the routing strategy for MALDA browser namespaces such as `game.*` and `three.*`.

## Current Routing Behavior (Confirmed)

### Entry/runtime binding

- `JsTranspiler` emits:
  - runtime availability guard for `globalThis.mlRuntime`
  - local binding: `const mlRuntime = globalThis.mlRuntime;`
- Routing decisions happen inside `TranspileExpression(...)` and `TranspileFunctionCall(...)` in `MaldaLang.Compiler/JsTranspiler.cs`.

### Built-in function routing (identifier callee)

- Identifier calls are special-cased in `TranspileFunctionCall(...)`:
  - `print(...)` -> `mlRuntime.builtins.print(...)`
  - `println(...)` -> `mlRuntime.builtins.println(...)`
  - `sleep(...)` -> `mlRuntime.builtins.sleep(...)`
  - `string(...)` -> `mlRuntime.coerceToString(...)` (special handling for zero args)
- Any other identifier call falls through to normal JS call emission:
  - `<callee>(<args>)`

### Namespaced `dom.*`, `game.*`, and `three.*` routing

These browser-facing namespaces are explicitly routed, and each is handled in two places:

1. `TranspileExpression(MemberAccessExpression)`:
   - `dom.someMember` -> `mlRuntime.dom.someMember`
   - `game.someMember` -> `mlRuntime.game.someMember`
   - `three.someMember` -> `mlRuntime.three.someMember`
2. `TranspileFunctionCall(...)` when callee is member access with object identifier `dom`:
   - `dom.someFn(a, b)` -> `mlRuntime.dom.someFn(a, b)`
   - `game.someFn(a, b)` -> `mlRuntime.game.someFn(a, b)`
   - `three.someFn(a, b)` -> `mlRuntime.three.someFn(a, b)`

This dual handling keeps both property access and direct call syntax routed to runtime browser helpers.

### Runtime contract shape today

In `Examples/Web/wwwroot/malda-js-runtime.js`, `mlRuntime` currently provides:

- top-level helpers: `coerceToInt`, `coerceToString`, `isTruthy`, `equals`
- module-like groups:
  - `builtins`: `print`, `println`, `sleep`
  - `dom`: `query`, `create`, `append`, `clear`, `setText`, `html`, `on`
  - `game`: 2D canvas/audio helpers, pixel-buffer blit (`setPixel` / `blitPixels`), images (`loadImage` / `drawImage` / `drawImageRect` / `drawImageEx`), a 2D camera (`setCamera`), key edges (`wasKeyPressed` / `wasKeyReleased`), touches (`getTouches`), gamepad helpers, overlap queries (`overlapRect` / `overlapCircle`), and swept AABB (`sweepRect`)
  - `three`: curated three.js scene helpers

`mlRuntime` is merged via `Object.assign({}, global.mlRuntime || {}, runtime)`, so additional groups can be added without changing the outer loading contract.

## Decision: `game.*` Routing Strategy

For the canvas-games feature (JavaScript backend only), route MALDA `game.*` exactly like `dom.*`:

- `game.createCanvas(...)` -> `mlRuntime.game.createCanvas(...)`
- `game.clear(...)` -> `mlRuntime.game.clear(...)`
- `game.fillRect(...)` -> `mlRuntime.game.fillRect(...)`
- and similarly for other `game.*` members.

### Why this mapping

- Matches the established namespaced runtime pattern (`dom.*` -> `mlRuntime.dom.*`).
- Keeps browser-specific APIs out of global function namespace.
- Preserves existing built-in routing and behavior for `print`/`println`/`sleep`/`string`.
- Fits the existing runtime module layout (`mlRuntime.<module>.*`).

## Implementation Constraints / Invariants

- No behavioral changes to existing built-in mappings.
- No regression in current `dom.*` mapping.
- JavaScript backend remains source of truth for this feature; no C# runtime/transpiled parity is implied in this step.
- Routing should continue to support both:
  - member access (`var f = game.fillRect;`)
  - direct calls (`game.fillRect(...)`)

## Recommended Implementation Shape (for follow-up code change)

Use the existing explicit-special-case style and add `game` alongside `dom` in both relevant transpiler branches:

- `TranspileExpression(MemberAccessExpression)` namespace check
- `TranspileFunctionCall(...)` member-callee namespace check

This is minimal-risk and consistent with current code style. A generic namespace-router helper can be considered later if more namespaces are added, but is not required for introducing `game.*`.

## Decision: `three.*` Routing Strategy

For the 3D scene MVP (JavaScript backend only), route MALDA `three.*` exactly like `dom.*` and `game.*`:

- `three.createRenderer(...)` -> `mlRuntime.three.createRenderer(...)`
- `three.createScene()` -> `mlRuntime.three.createScene()`
- `three.createOrthographicCamera(...)` -> `mlRuntime.three.createOrthographicCamera(...)`
- `three.createShaderMaterial(...)` -> `mlRuntime.three.createShaderMaterial(...)`
- `three.setUniform(material, name, value)` -> `mlRuntime.three.setUniform(material, name, value)`
- `three.render(renderer, scene, camera)` -> `mlRuntime.three.render(renderer, scene, camera)`
- and similarly for the rest of the curated `three.*` surface.

### Why this mapping

- Matches the established namespaced runtime pattern already used by `dom.*` and `game.*`.
- Keeps the MALDA API curated instead of exposing raw `THREE.*` browser interop.
- Preserves current built-in routing and the overall `mlRuntime.<module>.*` contract.
- Allows `three.*` examples to stay idiomatic alongside existing JS backend examples.

### Implementation notes

- `three.*` remains JavaScript-backend-only and browser-hosted.
- Host pages must load `three.js` before `malda-js-runtime.js`.
- Routing must continue to support both:
  - member access (`var renderFn = three.render;`)
  - direct calls (`three.render(renderer, scene, camera)`)
