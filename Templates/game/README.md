# __PROJECT_NAME__ (MALDA canvas game)

A small paddle-and-ball starter on the JavaScript `game.*` canvas API.

## Run This First

```bash
malda play app.malda
```

That compiles to JavaScript, copies `malda-js-runtime.js`, writes a host page, and serves a local URL. Press Ctrl+C to stop. Add `--open` to launch a browser when the OS allows it.

`malda play` serves the preview folder (`.malda-play/` next to `app.malda`), not the source tree. Put images and samples in `assets/` so they are copied into the preview for `game.loadImage("assets/...")` / `game.audioPlaySample("assets/...")`.

## Ship a static build

Preview is the inner loop. Packaging is still JavaScript / PWA compile:

```bash
malda compile app.malda --mode js -o app.js
malda compile app.malda --mode pwa -o dist
```

`--mode js` writes `app.js`, `malda-js-runtime.js`, and `index.html` next to `-o`. Open that folder over HTTP (or keep using `malda play`). `--mode pwa` is the itch.io-style folder. Do not run this file with the interpreter: `game.*` is JavaScript-backend only.

## What this starter includes

- `app.malda`: canvas, `game.start`, arrow-key paddle, `game.overlapRect` bounce, `wasKeyPressed("r")` restart
- `index.html`: load `malda-js-runtime.js`, then `app.js`, then `MaldaApp.main()`
- `assets/`: optional media folder (empty until you add files)

`--local-first` does not apply to this template (no SQLite / `malda db` files).
