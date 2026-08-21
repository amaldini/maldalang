# __PROJECT_NAME__ (MALDA fullstack scores)

A paddle-and-ball canvas client (`@client`) plus an in-memory score API (`@GET` / `@POST`) in one `.malda` file. The save blob is a `schema Score` checked with `validate("Score", …)` on both sides.

## Run This First

```bash
malda compile app.malda --mode fullstack -o dist
```

That writes `dist/server/` (host exe), `dist/web/` (JS client + `malda-js-runtime.js` + `index.html`), and `dist/manifest.json`. Point the server at the web folder and start it:

```bash
# Linux / macOS
export MALDA_WEB_DIRECTORY="$PWD/dist/web"
./dist/server/app.server.exe

# Windows (cmd)
set MALDA_WEB_DIRECTORY=%CD%\dist\web
dist\server\app.server.exe
```

Open http://localhost:8080/ — Left/Right move the paddle, R restarts after game over. A run posts `{ "name", "points" }` to `/api/scores`. `GET /api/health` and `GET /api/scores` are public JSON.

Desktop IDE F5 on `app.malda` compiles both partitions and starts host + Web Preview together.

`malda play` is JavaScript-only and **refuses** this file. Do not run `app.malda` with the interpreter: `game.*` is JS-only, and the two `boot` functions are partitioned by target.

## What this starter includes

- `schema Score { name: string; points: int; }` — shared registration (client and server both `validate`)
- `@GET("/api/scores")` / `@POST("/api/scores")` — in-memory list, top 10 by points
- `@client startGame` — `game.startFixed`, `wasKeyPressed("r")`, `overlapRect`, `strokeRect` / `drawLine`, `game.save` / `game.load` for a local high score, `httpGet` / `httpPost` for the board
- Two `function boot()` declarations (one `@server()`, one `@client()`) plus a shared `boot();` call so each backend invokes its own entry
- `tests/score.test.malda` — `malda test` checks `validate("Score", …)` without the canvas

`--local-first` does not apply (no SQLite / `malda db` files). Swap the in-memory `scores` list for SQLite when you want persistence across process restarts.

Host prompts / LLM NPCs are optional commentary, not required to run. The game works without an API key.

## Tests

```bash
malda test --format human
```
