# Games and graphics examples

Browser `game.*` canvas games and `three.*` graphics samples. Compile with JavaScript mode, then open the matching `*_runtime_smoke_test.html` host page.

These used to live under `Examples/Web/js/`. DOM/HTTP samples stay in [`Examples/Web/`](../Web/). Shared runtime assets stay in [`Examples/Web/wwwroot/`](../Web/wwwroot/).

## Run

```bash
# Inner loop (compile JS, write a host page, serve a local URL)
malda play Examples/Games/game_bounce.malda
malda play Examples/Games/maldanoid.malda
malda play Examples/Games/malda_platform.malda
malda play Examples/Games/game_tiles_smoke.malda

# From repo root (explicit compile; then open the sibling host page)
malda compile Examples/Games/game_bounce.malda --mode js -o Examples/Games/game_bounce.js
malda compile Examples/Games/game_sprite_smoke.malda --mode js -o Examples/Games/game_sprite_smoke.js
malda compile Examples/Games/game_input_smoke.malda --mode js -o Examples/Games/game_input_smoke.js
malda compile Examples/Games/game_collision_smoke.malda --mode js -o Examples/Games/game_collision_smoke.js
malda compile Examples/Games/game_tiles_smoke.malda --mode js -o Examples/Games/game_tiles_smoke.js
malda compile Examples/Games/game_audio_sample_smoke.malda --mode js -o Examples/Games/game_audio_sample_smoke.js
malda compile Examples/Games/game_fixed_save_smoke.malda --mode js -o Examples/Games/game_fixed_save_smoke.js
malda compile Examples/Games/malda_platform.malda --mode js -o Examples/Games/malda_platform.js
malda compile Examples/Games/maldanoid.malda --mode js -o Examples/Games/maldanoid.js
malda compile Examples/Games/maldadash.malda --mode js -o Examples/Games/maldadash.js
malda compile Examples/Games/three_cube.malda --mode js -o Examples/Games/three_cube.js
malda compile Examples/Games/three_textured.malda --mode js -o Examples/Games/three_textured.js
malda compile Examples/Games/three_shader_billiards.malda --mode js -o Examples/Games/three_shader_billiards.js
malda compile Examples/Games/three_shader_path_tunnel.malda --mode js -o Examples/Games/three_shader_path_tunnel.js
```

Then open the sibling host page (for example `Examples/Games/maldadash_runtime_smoke_test.html`).

`malda_platform.malda` is the kit showcase (atlas tiles, `followCamera`, `sweepRects` landings, key edges, sample SFX, `startFixed`) with spinning coins plus a facing player via `game.drawImageEx`, coin-hit `tintFill` flash, an additive spark, and HUD via `pushCamera`. Bounce stays a minimal `game.start` primitive-draw loop. `game_sprite_smoke.malda` also zooms with `setCameraZoom`, tints / `tintFill`s `drawImageEx`, calls `setBlend` (`add` glow + multiply strip), queries atlas/canvas size, measures HUD text, strokes a circle around the click marker, and calls `setPixelated`. `game_tiles_smoke.malda` is the G17 tile grid: `drawTiles` from the atlas, `tileAt` gem pickups, and `sweepTiles` landings. `maldanoid.malda` stays primitive-draw (`fillRect` / `fillCircle`, no sprites) but uses G2/G3/G5: `startFixed`, velocity-axis `overlapRect` bounces, aimed near-vertical serves, touch-follow paddle, hit sparks + `setCamera` punch, combo bar, result panels, and `game.save` / `load` for high score plus best combo. `maldadash.malda` is the Boulder Dash-style cave showcase: `drawTiles` atlas, `followCamera` + zoom, actor `drawImageEx` (flip / spin / tintFill), `sweepTiles` boulder pushes and spark bounces, `setBlend` torch + additive sparks, key/gamepad/touch, sample SFX, `startFixed`, and a saved high score.

`three_shader_billiards.malda` is a playable GPU ray-traced pool table on the same `three.createShaderMaterial` path as `three_shader_raytracer.malda`. Host MALDA aims the cue and steps 2D circle physics (cushions, pockets, friction); the kernel traces `uBall0`–`uBall15` plus the cue stick. Digit decals stay procedural. The cue stays locked on the white ball; mouse on the table aims (A/D fine-tunes). Hold click or Space to charge power, release to shoot; R reracks; arrows orbit; `[` `]` zoom; `C` or Stop camera zeros orbit speed. Sliders set cushion e, ball e, and felt friction. The compiled table is playable from [Reference Manual 37](../../ReferenceManual/37-appendix-gpu-billiards.html).

That tunnel demo is a CC-BY-NC-SA-4.0 conversion of [Frostbyte’s path march](https://fragcoord.xyz/s/tbe1g319); it is not under the repository MIT OR Apache-2.0 dual licence.

JS / PWA samples need `malda compile … --mode js` (or the Desktop/Web IDE browser path). A canvas client plus server scores is `malda new game --fullstack` (`Templates/game-fullstack/`). See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md) and [`docs/javascript-backend.md`](../../docs/javascript-backend.md).

Games kit G0–G17 is landed. Contracts: [`docs/roadmap-games.md`](../../docs/roadmap-games.md). Peer comparison (what the kit still lacks vs Love2D / Pico-8 / Phaser): [`docs/games-2d-gap-analysis.md`](../../docs/games-2d-gap-analysis.md).

Catalog fields: [`metadata.json`](metadata.json).
