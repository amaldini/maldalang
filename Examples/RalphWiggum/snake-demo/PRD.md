# PRD — Snake.html (demo Ralph Wiggum)

## Visione prodotto

Creare **un unico file standalone** `snake.html` (HTML + CSS + JS inline, zero dipendenze esterne) che dimostri un Snake all'avanguardia: grafica eccezionale, gameplay fluido a 60 FPS, modalità **Auto-Play** con IA che punta a punteggi elevati, e polish da showcase.

**Vincoli non negoziabili**
- Output finale: solo `snake.html` in questa cartella (nessun altro asset richiesto per giocare).
- Apribile con doppio click nel browser, offline.
- Nessuna libreria CDN; tutto inline.
- Codice leggibile; per file piccoli preferire `writeFile` completo invece di molti patch.

**Metriche di successo**
- Auto-play raggiunge regolarmente punteggi ≥ 50 senza collisioni evidenti.
- FPS stabile; animazioni fluide.
- UI moderna: tipografia, colori, feedback visivo, responsive base (desktop + mobile).

---

## Checklist feature (priorità alta → bassa)

Implementare **una feature per iterazione**. Segnare `[DONE]` solo quando la feature è completa, il file passa la validazione Ralph, ed è testabile aprendo `snake.html`.

Sintassi estesa Ralph: `[P0]`/`[P1]`/`[P2]`, `(depends: F1)`, righe `Acceptance:`, opzionali `Files:` / `Verify:` sotto ogni item.

### Fondamenta

- [TODO] [P0] **F1 — Scaffold e loop di gioco** (depends: none)
  - Files: snake.html
  - Verify: snake.html
  - Acceptance: canvas responsive; griglia 24×24; serpente iniziale; cibo random; movimento a tick; crescita al mangiare; game over su muro/self-collision; restart (R); controlli frecce e W/S/D (A riservato ad auto-play in F4)

- [TODO] [P0] **F2 — Estetica premium** (depends: F1)
  - Acceptance: sfondo gradiente animato; griglia con glow; serpente con testa distinta e corpo degradé; cibo pulsante; HUD (score, best placeholder, stato); transizione morbida su game over

### Gameplay avanzato

- [TODO] [P1] **F3 — High score e persistenza** (depends: F1)
  - Acceptance: `localStorage` per il record; animazione su nuovo record; reset record con Shift+R

- [TODO] [P1] **F4 — Auto-Play con IA** (depends: F1, F3)
  - Acceptance: toggle Auto (tasto A + pulsante); BFS verso il cibo con fallback safe verso la coda; punteggi credibili in auto

- [TODO] [P1] **F5 — Velocità adattiva** (depends: F1)
  - Acceptance: accelerazione ogni N punti; indicatore livello/velocità in HUD; pausa (P)

### Polish

- [TODO] [P2] **F6 — Juice** (depends: F2)
  - Acceptance: particelle su cibo/game over; screen shake; trail sul serpente; beep Web Audio (sintetici, no file esterni)

- [TODO] [P2] **F7 — UX finale** (depends: F2, F4)
  - Acceptance: schermata titolo con istruzioni; game over con statistiche (score, lunghezza, tempo); pulsanti Play / Auto / Restart; commento in cima al file con istruzioni rapide

- [SKIP] [P2] **F8 — Multiplayer locale** — deferred

---

## Note per l'agente

- Lavorare **solo** in questa directory.
- Dopo ogni feature: rileggere `snake.html`, aggiornare checklist, non segnare `[DONE]` se la validazione fallisce.
- Quando tutte le voci aperte sono `[DONE]` e il gioco è valido, rispondere con `TASK_COMPLETE`, `RALPH_DONE`, o `{"ralph":"done"}`.
