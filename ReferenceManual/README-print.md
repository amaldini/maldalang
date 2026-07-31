# Reference Manual — presentation and print

How the manual is styled on screen, how to print a single chapter, and how to
produce the bound edition for paper.

## Assets

Every page in `ReferenceManual/` loads the same five files. Nothing is fetched
from a CDN, so the manual works offline and from `file://`.

| File | Role |
|------|------|
| `styles.css` | Screen presentation |
| `syntax.css` | Syntax highlighting token colours |
| `print.css` | Paper presentation, linked with `media="print"` |
| `malda-highlight.js` | Dependency-free MALDA / shell / REPL / JSON highlighter |
| `navigation.js` | Sidebar, copy buttons, print action |

`print.css` **must** load after `styles.css`, otherwise the screen rules win the
cascade in the print medium. After adding a chapter, run:

```bash
powershell -File scripts/sync-reference-manual-assets.ps1
```

It is idempotent and only adds what is missing.

## Syntax highlighting

`malda-highlight.js` tokenizes each `<pre><code>` block at load time. The
keyword list mirrors `MaldaLang/Lexer.cs`; keep them in step when the language
gains a keyword.

The language of a block is detected from its content — MALDA, `shell` (a `$`
prompt or a known command), `repl` (a `>` prompt) or `json`. Override it with a
class when detection guesses wrong:

```html
<pre><code class="language-shell">malda run app.malda</code></pre>
```

Blocks that already contain markup, such as the linked keyword index in
`02-lexical-structure.html`, are left untouched.

Each source line is wrapped in `<span class="ln">`. That is what makes the
hanging indent below possible, and it is why `navigation.js` rebuilds newlines
when copying a block to the clipboard.

## Printing one chapter

Use the **Print / PDF** button in the header, or Ctrl+P. Output is A4 with the
navigation, breadcrumbs and copy buttons removed, prose set in a serif face and
code kept at 9pt.

In the browser print dialog, set **Margins: Default** (the stylesheet supplies
its own) and turn **Headers and footers** off.

## The bound edition

```bash
powershell -File scripts/build-reference-manual-book.ps1
```

This reads `chapters.json`, lifts the `<main>` content out of each chapter,
turns cross-chapter links into internal anchors and writes one self-contained
file, `artifacts/reference-manual/malda-reference-manual.html`. Styling
(`book.css`, `syntax.css`) and the highlighter are inlined into it, so the book
can be mailed or archived on its own.

The inlining is not only for convenience: Paged.js re-fetches linked stylesheets
over XHR in order to parse their `@page` rules, and Chrome blocks those requests
under the `file://` origin. With `<link>` tags the book opened from disk hangs
forever at "Paginating...". Keep the styles inline, or the bound edition only
works when served over HTTP.

Then:

1. Open `malda-reference-manual.html` in Chrome or Edge.
2. Wait for the banner in the corner to report the page count (about ten
   seconds for the full manual).
3. Ctrl+P → **Save as PDF**, margins **None**, **Background graphics** on.

The current manual comes to roughly 326 A4 pages.

### What the bound edition adds

- A cover and copyright page, and a contents page with real page numbers.
- Running heads: book title on the left-hand page, chapter title on the right.
- Folios in the outer bottom corner.
- Every chapter opens on a right-hand page.
- Mirrored margins, wider on the binding side.

Page numbers, running heads and the contents folios come from
[Paged.js](https://pagedjs.org/), loaded from a CDN. Without a network the book
still prints correctly, just without those refinements; `-NoPagedJs` skips it
deliberately.

### Other paper sizes

```bash
powershell -File scripts/build-reference-manual-book.ps1 -Trim 7x10
```

`A4` (default), `Letter`, `7x10` and `6x9`. Each preset carries its own margins
and code size; the smaller trims shrink code to keep a usable number of
characters per line.

## Why code wraps instead of scrolling

On screen a long line scrolls sideways. On paper there is nowhere to scroll, so
an unwrapped line would simply be cut off — and 119 of the manual's 5,551 code
lines are wider than an A4 measure at 9pt, one of them 188 characters.

In print, lines wrap and the continuation is indented past the indentation of
the line it belongs to:

```
    var insert = dbExecute(
        "INSERT INTO Tickets (customerName, email, subject, description,
          status, priority, createdAt) VALUES (@customerName, @email, ...)",
```

The indent comes from a `--i` custom property that the highlighter writes on
each line, holding that line's own indentation in columns. Hanging off the
block's left edge instead would push continuations further left than the code
they continue, which reads as a dedent.

Listings of 20 lines or fewer are marked `data-short` and are kept on one page;
longer ones are allowed to break so they do not leave half-empty pages.
