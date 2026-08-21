# Games and graphics examples

Browser `game.*` canvas games and `three.*` graphics samples. Compile with JavaScript mode, then open the matching `*_runtime_smoke_test.html` host page.

These used to live under `Examples/Web/js/`. DOM/HTTP samples stay in [`Examples/Web/`](../Web/). Shared runtime assets stay in [`Examples/Web/wwwroot/`](../Web/wwwroot/).

## Run

```bash
# From repo root
malda compile Examples/Games/game_bounce.malda --mode js -o Examples/Games/game_bounce.js
malda compile Examples/Games/game_sprite_smoke.malda --mode js -o Examples/Games/game_sprite_smoke.js
malda compile Examples/Games/game_input_smoke.malda --mode js -o Examples/Games/game_input_smoke.js
malda compile Examples/Games/game_collision_smoke.malda --mode js -o Examples/Games/game_collision_smoke.js
malda compile Examples/Games/maldadash.malda --mode js -o Examples/Games/maldadash.js
malda compile Examples/Games/three_cube.malda --mode js -o Examples/Games/three_cube.js
malda compile Examples/Games/three_shader_path_tunnel.malda --mode js -o Examples/Games/three_shader_path_tunnel.js
```

Then open the sibling host page (for example `Examples/Games/maldadash_runtime_smoke_test.html`).

`maldadash.malda` is a Boulder Dash-style cave (dirt, gravity rocks, diamonds, fireflies) on `game.*`.

That tunnel demo is a CC-BY-NC-SA-4.0 conversion of [Frostbyte’s path march](https://fragcoord.xyz/s/tbe1g319); it is not under the repository MIT OR Apache-2.0 dual licence.

JS / PWA samples need `malda compile … --mode js` (or the Desktop/Web IDE browser path). See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md) and [`docs/javascript-backend.md`](../../docs/javascript-backend.md).

Catalog fields: [`metadata.json`](metadata.json).
