# Reference Manual — chapter numbering

The manual uses **`ReferenceManual/chapters.json`** as the source of truth for chapter order. Two tools update numbers; use the right one for the job.

## Quick guide

| Goal | Tool |
|------|------|
| Reorder chapters in `chapters.json` (title, breadcrumb, main h1, prev/next footer) | `scripts/sync-reference-manual-chapter-numbers.ps1` |
| Full publish pass: h2/h3 sections, inline “Section X:” links, See Also, `navigation.js`, **`ReferenceManualPDF.html`** | `ReferenceManual/update-chapter-numbers.malda` |

**Typical workflow after editing `chapters.json`:**

```powershell
# From repo root — fast, deterministic (CI-tested)
powershell -File scripts/sync-reference-manual-chapter-numbers.ps1

# Optional — only when you need PDF / deep cross-links / section renumbering
cd ReferenceManual
malda update-chapter-numbers.malda
```

## 1. PowerShell sync (chapter shell)

**Script:** `scripts/sync-reference-manual-chapter-numbers.ps1`  
**Test:** `MaldaLang.Tests/ReferenceManualChapterSyncTests.cs`

Updates per chapter file (from `chapters.json` order):

- `<title>N. Title - MALDA Reference Manual</title>`
- Breadcrumbs: `Home / N. Title`
- `<main><h1>N. Title</h1>` (does not change site header `MALDA Reference Manual`)
- Nav footer Previous / Next labels and targets

Does **not** update: h2/h3 numbering, body cross-references, `navigation.js`, or `ReferenceManualPDF.html`.

Prefer this for day-to-day chapter reordering.

## 2. MALDA script (full manual pipeline)

**Script:** `ReferenceManual/update-chapter-numbers.malda`  
**Run:** `malda update-chapter-numbers.malda` (from `ReferenceManual/` or with `MALDAREFERENCEMANUAL` set)

Still the **authoritative** tool for:

- Renumbering **h2 / h3** inside chapters (`renumberH2`, `renumberH3` flags at top of script)
- Fixing **inline** links like `Section 12: …` and See Also chapter references
- **`navigation.js`** fallback nav labels (when not loading `chapters.json` via HTTP)
- Building **`ReferenceManualPDF.html`** for PDF export
- Graph / VectorDB-assisted link repair when chapter numbers drift in prose

Also updates title, breadcrumb, h1, and nav footer — **overlapping** with the PowerShell script. After a reorder, run PowerShell first (CI guard), then Malda only if you need the extra steps above.

**Not obsolete** — complementary. See [`docs/planning/SCRIPT_CONSOLIDATION.md`](../docs/planning/SCRIPT_CONSOLIDATION.md) for history of merged Malda variants.

## Modifying chapter order

1. Edit `ReferenceManual/chapters.json`.
2. Run `scripts/sync-reference-manual-chapter-numbers.ps1`.
3. Run `dotnet test MaldaLang.Tests --filter ReferenceManualChapterSync` (optional).
4. If publishing PDF or fixing section numbers / cross-links: `malda update-chapter-numbers.malda`.

## Files

| File | Role |
|------|------|
| `chapters.json` | Chapter order and titles |
| `scripts/sync-reference-manual-chapter-numbers.ps1` | Shell numbering sync (PowerShell) |
| `update-chapter-numbers.malda` | Full manual + PDF pipeline (MALDA) |
| `navigation.js` | Sidebar nav (loads `chapters.json` over HTTP) |
| `ReferenceManualPDF.html` | Generated — do not edit by hand |

## Notes

- Preprocessing bakes numbers into HTML so `file://` works without runtime JS.
- Do not edit `ReferenceManualPDF.html` manually; regenerate via `update-chapter-numbers.malda`.
- Legacy `update-chapter-numbers.js` is **removed**; use PowerShell + Malda instead.
