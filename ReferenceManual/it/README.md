# Manuale di riferimento MALDA (italiano)

Traduzione italiana del Reference Manual. L’inglese in
[`../`](../) resta la **fonte di verità**: i content guard, gli snippet
eseguibili e l’allineamento al codice girano solo su quella copia.

## Layout

| Path | Ruolo |
|------|--------|
| `chapters.json` | Titoli e descrizioni italiani; `category` resta la chiave inglese |
| `*.html` | Stessi nomi file dei capitoli inglesi |
| `glossary.md` | Termini fissi |
| `STATUS.md` | Hash SHA-256 del file inglese da cui è stata tradotta ogni pagina |

CSS, highlighter e `navigation.js` sono quelli della cartella padre
(`../styles.css`, `../navigation.js`, …). Lo switcher **Italiano / English**
è iniettato nell’header.

## Come aggiornare una pagina

1. Modifica prima l’inglese in `ReferenceManual/{file}`.
2. Porta la stessa modifica in `ReferenceManual/it/{file}` (prosa italiana,
   blocchi `<pre><code>` identici all’inglese, path `../` e `../../docs/`).
3. Rigenera lo STATUS:

```bash
python3 scripts/sync-reference-manual-it-status.py
```

4. Filtra i test:

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~ReferenceManual"
```

Non tradurre `docs/llm/` (pack per gli agenti di coding: l’inglese è la lingua
di lavoro dei modelli).

## Edizione rilegata (libro)

Il libro italiano **non** è un secondo corpus. È lo stesso script della
copia inglese, puntato a questa cartella:

```bash
pwsh scripts/build-reference-manual-book.ps1 -Locale it
```

Output: `artifacts/reference-manual/malda-reference-manual-it.html`. Aprilo in
Chrome o Edge, attendi il banner con il numero di pagine, poi Ctrl+P → Save as
PDF (margini **None**, **Background graphics** on). Dettagli in
[`../README-print.md`](../README-print.md).

La copertina è `../assets/cover-it.svg`; copyright, indice e testatina sono
chrome dello script. Per aggiornare il libro: aggiorna prima l’HTML in `it/`,
poi rigenera. Non modificare a mano il file in `artifacts/`.
