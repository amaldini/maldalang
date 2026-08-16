# Istruzioni per tradurre `ReferenceManual/it/*.html`

Leggi prima `ReferenceManual/it/glossary.md` e `ReferenceManual/it/chapters.json`.
I file in `it/` sono copie inglesi con path già riscritti: **sovrascrivi** ogni
pagina assegnata con una traduzione italiana completa.

## Obbligatori

1. `<html lang="it">`
2. Header:
   - `<h1>Manuale di riferimento MALDA&trade;</h1>`
   - `<p>Il linguaggio di programmazione AI-First - Versione 1.0.0</p>`
3. `<title>` capitolo: `{N}. {Titolo da chapters.json} - Manuale di riferimento MALDA`
4. Breadcrumb: `<a href="index.html">Indice</a> <span>/</span> <span>{N}. {Titolo}</span>`
5. `<h1>` in `<main>`: `{N}. {Titolo}` identico a `chapters.json`
6. Asset: `../styles.css`, `../syntax.css`, `../print.css`, `../malda-highlight.js`, `../navigation.js` (index: anche `../index-toc.js`)
7. Link a `docs/`: `../../docs/...` (già così dopo lo script; non riportarli a `../docs/`)
8. Link tra capitoli: stessi filename (`02-tools.html`), stessa cartella
9. **Non tradurre** il contenuto di `<pre><code>...</code></pre>` (né commenti `//` nel codice, né `data-run` / `data-expect` / classi)
10. Non tradurre keyword, built-in, flag CLI, path, nomi propri (vedi glossario)
11. Numeri di sezione (`1.1`, `9.9.3`) invariati
12. Footer licenza: `Copyright (c) 2026 Andrea Maldini. Licensed under the <a href="https://opensource.org/license/mit">MIT License</a> or the <a href="https://www.apache.org/licenses/LICENSE-2.0">Apache License 2.0</a>, a tua scelta.`
13. Nav footer: `← Precedente: N. Titolo` / `Successivo: N. Titolo→` / `← Indice`
14. `See Also` → `Vedi anche` (i titoli dei link usano i titoli italiani di `chapters.json`)
15. `meta description` in italiano
16. Prosa naturale, tecnico-accurata, non letterale parola-per-parola se suona calco

Usa `ReferenceManual/it/index.html` come modello di intestazione se già tradotto.

Non modificare file fuori da `ReferenceManual/it/` e non toccare `chapters.json`, `glossary.md`, `STATUS.md`.
