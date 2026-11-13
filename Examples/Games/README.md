# Games and graphics examples

Browser `game.*` canvas games and `three.*` graphics samples. Compile with JavaScript mode, then open the matching `*_runtime_smoke_test.html` host page.

These used to live under `Examples/Web/js/`. DOM/HTTP samples stay in [`Examples/Web/`](../Web/). Shared runtime assets stay in [`Examples/Web/wwwroot/`](../Web/wwwroot/).

## Run

```bash
# Inner loop (compile JS, write a host page, serve a local URL)
malda play Examples/Games/game_bounce.malda
malda play Examples/Games/xor_decision_boundary.malda
malda play Examples/Games/softmax_decision_boundary.malda
malda play Examples/Games/lm_valley.malda
malda play Examples/Games/cartpole_neural.malda
malda play Examples/Games/grand_prix_neural.malda
malda play Examples/Games/digits_cnn.malda
malda play Examples/Games/spoken_reward.malda
malda play Examples/Games/maldanoid.malda
malda play Examples/Games/chess.malda
malda play Examples/Games/malda_platform.malda
malda play Examples/Games/game_tiles_smoke.malda
malda play Examples/Games/three_forest.malda
malda play Examples/Games/three_terrain.malda

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
malda compile Examples/Games/chess.malda --mode js -o Examples/Games/chess.js
malda compile Examples/Games/maldadash.malda --mode js -o Examples/Games/maldadash.js
malda compile Examples/Games/three_cube.malda --mode js -o Examples/Games/three_cube.js
malda compile Examples/Games/three_textured.malda --mode js -o Examples/Games/three_textured.js
malda compile Examples/Games/three_forest.malda --mode js -o Examples/Games/three_forest.js
malda compile Examples/Games/three_terrain.malda --mode js -o Examples/Games/three_terrain.js
malda compile Examples/Games/three_shader_billiards.malda --mode js -o Examples/Games/three_shader_billiards.js
malda compile Examples/Games/three_shader_path_tunnel.malda --mode js -o Examples/Games/three_shader_path_tunnel.js
malda compile Examples/Games/three_shader_mandelbrot.malda --mode js -o Examples/Games/three_shader_mandelbrot.js
malda compile Examples/Games/three_sdf_interpreter.malda --mode js -o Examples/Games/three_sdf_interpreter.js
malda compile Examples/Games/three_sdf_invaders.malda --mode js -o Examples/Games/three_sdf_invaders.js
malda compile Examples/Games/three_sdf_pacman.malda --mode js -o Examples/Games/three_sdf_pacman.js
malda compile Examples/Games/three_shader_race_traffic.malda --mode js -o Examples/Games/three_shader_race_traffic.js
```

Then open the sibling host page (for example `Examples/Games/maldadash_runtime_smoke_test.html`).

`malda_platform.malda` is the kit showcase (atlas tiles, `followCamera`, `sweepRects` landings, key edges, sample SFX, `startFixed`) with spinning coins plus a facing player via `game.drawImageEx`, coin-hit `tintFill` flash, an additive spark, and HUD via `pushCamera`. Bounce stays a minimal `game.start` primitive-draw loop. `game_sprite_smoke.malda` also zooms with `setCameraZoom`, tints / `tintFill`s `drawImageEx`, calls `setBlend` (`add` glow + multiply strip), queries atlas/canvas size, measures HUD text, strokes a circle around the click marker, and calls `setPixelated`. `game_tiles_smoke.malda` is the G17 tile grid: `drawTiles` from the atlas, `tileAt` gem pickups, and `sweepTiles` landings. `chess.malda` is the turn-based primitive-draw showcase: click or tap to move, pieces slide (knights hop), legal-square highlights, castling / en passant / queen promotion, checkmate and stalemate, undo, optional 1–3 ply CPU (default 1), and `game.save` / `load` for the live position plus score. `maldanoid.malda` stays primitive-draw (`fillRect` / `fillCircle`, no sprites) but uses G2/G3/G5: `startFixed`, velocity-axis `overlapRect` bounces, aimed near-vertical serves, touch-follow paddle, hit sparks + `setCamera` punch, combo bar, result panels, and `game.save` / `load` for high score plus best combo. Showcase autoplay (`#malda-autoplay`, or N) trains a 4-12-1 net on a wall-bounce intercept coach and blends the paddle over as the loss falls. The HUD plots that loss, the twelve hidden units, and the coach and net aims; the same two aims are ticks above the paddle. When the loss falls to 0.03 the network plays alone and the gold tick is only the coach's aim. Press N, or the Neural button on the title screen and in the HUD, to start that mode from `malda play`. A key, click, touch, or stick returns control and does not write the high score. `grand_prix_neural.malda` is a broadcast-view race on simplified centerlines of eight Grand Prix circuits. Eight cars share a 12-20-2 net; a pure-pursuit coach labels steer and throttle, `nn.denseBackward` fits those labels, and the coach blend fades as the loss falls. Q/E or the buttons change circuit, C follows the leader, Tab drives. `digits_cnn.malda` trains a four-filter conv net on marker digits, then classifies ink drawn on the 16×16 page. `spoken_reward.malda` fits a 4-12-1 net (x, y, x², y²) to a reward you paint or name in a sentence; the creature climbs the net, so it keeps walking the old hill for a moment after the sentence changes. `three_forest.malda` is the raster 3D kit: 2400 trees as two `createInstancedMesh` meshes (trunks and tinted crowns) plus 300 rocks, placed with `setInstance`, a sun from `setLightShadow` that follows the camera via `lookAt`, `setShadows` / `setShadow`, and linear `setFog` in the sky color. F fog, H shadows, + / - tree count (`setInstanceCount`), I stats. `three_terrain.malda` is the modelling-tool kit: a terrain mesh from computed `buf.float32` / `buf.int32` buffers (`createBufferGeometry`), grid lines (`createLines`) and markers (`createPoints` + `setDrawRange`), orbit controls with `fitToBounds` and `setProjection`, click-to-paint faces through `pick` and in-place `updateGeometry`, a raise brush on shift + click, a minimap drawn into a render target (`renderToTarget`, `getTargetTexture`, `readPixels`), a water shader composed at runtime from `glsl.source`, and an ID-buffer mode (`pickId`). `maldadash.malda` is the Boulder Dash-style cave showcase: `drawTiles` atlas, `followCamera` + zoom, actor `drawImageEx` (flip / spin / tintFill), `sweepTiles` boulder pushes and spark bounces, `setBlend` torch + additive sparks, key/gamepad/touch, sample SFX, `startFixed`, and a saved high score.

`three_shader_billiards.malda` is a playable GPU ray-traced pool table on the same `three.createShaderMaterial` path as `three_shader_raytracer.malda`. Host MALDA aims the cue and steps 2D circle physics (cushions, pockets, friction); the kernel traces `uBall0`–`uBall15` plus the cue stick. Digit decals stay procedural. The cue stays locked on the white ball; mouse on the table aims (A/D fine-tunes). Hold click or Space to charge power, release to shoot; R reracks; arrows orbit; `[` `]` zoom; `C` or Stop camera zeros orbit speed. Sliders set cushion e, ball e, and felt friction. The compiled table is playable from [Reference Manual 39](../../ReferenceManual/39-appendix-gpu-billiards.html).

`three_shader_mandelbrot.malda` is an infinite Seahorse Valley zoom on the same `three.createShaderMaterial` / `@shader()` path. The host builds a BigInt reference orbit (`three.mandelbrotOrbit`, decimal strings for C) and the kernel applies scaled perturbation so the dive stays sharp past float32 ulp. Sliders set iterate budget and wrap decades (defaults 1024 / 8). Hold Space to pause; `[` `]` change speed.

That tunnel demo is a CC-BY-NC-SA-4.0 conversion of [Frostbyte’s path march](https://fragcoord.xyz/s/tbe1g319); it is not under the repository MIT OR Apache-2.0 dual licence.

JS / PWA samples need `malda compile … --mode js` (or the Desktop/Web IDE browser path). A canvas client plus server scores is `malda new game --fullstack` (`Templates/game-fullstack/`). See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md) and [`docs/javascript-backend.md`](../../docs/javascript-backend.md).

Games kit G0–G17 is landed. Contracts: [`docs/roadmap-games.md`](../../docs/roadmap-games.md). Peer comparison (what the kit still lacks vs Love2D / Pico-8 / Phaser): [`docs/games-2d-gap-analysis.md`](../../docs/games-2d-gap-analysis.md).

Catalog fields: [`metadata.json`](metadata.json).

`three_sdf_interpreter.malda` raymarches signed distance fields with one shader that is compiled once. The host writes the scene as bytecode into a float data texture (`three.createDataTexture`): UNION and CUT instructions carry a shape each, and GROUP instructions wrap a tree of shapes sorted by position, each with a bounding sphere and a jump past it. A pixel needs up to ~180 distance evaluations, so each ray first walks the tree once, skipping the groups it misses, and keeps the few shapes it passes near in a local array (`var cand: int[MAX_CAND];`); every march step then runs only those. That is about 35 times faster than running the whole program per step, and B turns it off to compare. Adding, cutting, undoing or animating shapes only calls `three.updateDataTexture`; the status line counts those uploads. The same bytecode also runs on the host (`cpuSceneDistance`) to find the surface under the mouse. Click drops the tool, 1-4 pick the shape, X toggles add / cut, Backspace undoes, `[` `]` change the blend, M toggles motion.

`three_sdf_invaders.malda` is a 3D space-invaders game on the same interpreter shader. Every frame the host rewrites the program texture with the whole battlefield: invaders that sway their limbs and turn to face the cannon, the cannon, lasers, helix bombs, the mystery saucer, explosion goo (debris spheres with a wide smooth-union blend) and shockwave rings, under a tree of GROUPs. Shields come first, each in its own group: a hit appends a CUT sphere after that shield, so the crater is carved by the bytecode and its rim glows while the heat stored in the instruction cools, and the host runs the same shield bytecode (`cpuShield`) to know whether a shot hit material or flew through an old hole. Besides the shapes to march, each ray keeps a second local list of glowing shapes that add a halo along the ray and light the surface it ends on. The floor reflects the scene with a second traced ray. The raymarch renders into a render target (`three.createRenderTarget`) and a second `@shader()` pass runs FXAA, then a vignette or, with C, a CRT mode (curved tube, scanlines, aperture grille, color fringing, bloom); the auto render scale may also climb to 1.5x for supersampling when the GPU has headroom. Enter starts, arrows or A / D move, Space fires, P pauses, F toggles reflections, X the antialiasing, C the CRT mode, B the ray culling, M mutes; shooting the saucer gives ten seconds of rapid fire.

`three_sdf_pacman.malda` is a 3D maze chase on that same interpreter. Every frame the host rewrites the program with merged wall boxes, the dots that are still on the board, the muncher and the four ghosts, under the same GROUP tree. The muncher is an SDF sphere with a wedge mouth (`sdPacman`); each ghost is an SDF body with eyes (`sdGhost`), blue while frightened and eyes-only on the way home. The host calls `shapeRadius` by name when it builds the group spheres. Power dots are the only glowing shapes. Reflections stay off until F, because a second march of the maze is expensive. Enter or an arrow starts, arrows or WASD turn (the next turn is buffered until the tile), P pauses, and X, C, B and M are the same toggles as invaders. The high score is `game.save("sdf-pacman-hi")`.

`three_shader_race_traffic.malda` is the 3D neural race with road traffic, and it hands the cars to the ray tracer the way `three_sdf_interpreter.malda` hands it a scene. There is no fixed `uCars[8]` uniform: every frame the host writes one float data texture (`three.updateDataTexture`) with a vehicle table (position, heading, steer, model) and a program of CAR and GROUP instructions. The GROUPs form a tree of bounding spheres over the cars, split by position. Each primary or shadow ray walks the program once and jumps past every group it misses or reaches only beyond its nearest hit. The car parts are still exact box and cylinder intersections, so there is no march and no candidate list. Up to 56 road cars (G toggles them, `[` `]` change how many) drive two lanes at about a third of race pace, slow in the bends, keep a gap in their lane, and move to the other lane when a racer closes in behind (blue flags). A road car does not change lane onto a racer beside it or just behind it. Contact with a racer uses the equal-mass bounce between racers. Each racer also casts three car rays (ahead and 0.3 rad to each side) against the oriented boxes of the other cars and measures how fast it closes on the car ahead. Those four inputs widen the net to 19-20-2. The coach turns them into a time to contact and, under 2 s, steers to the side with more room and lifts; the net learns the overtakes from those labels. The HUD shows the car rays and counts incidents (HITS, once per road car per second). B turns the group skip off to show what it saves. The tenth circuit, Malda City, is a street circuit on a grid of blocks (`gridRoute` turns its corners into tight 90° bends). Away from the track the ground shader draws the street grid itself (`cityGround`: asphalt, centre line, zebra crossings, pavements, lots); each block carries 2×2 instanced buildings, taller toward the middle, and a few blocks are parks. Every junction the circuit crosses has traffic lights; road cars stop at red and at amber when they can, so queues form, while the racers do not stop.
