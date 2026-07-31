# Reference Manual numbering scripts

## Two tools (2026-06-04)

Chapter numbering is split between a **fast PowerShell sync** and the **full MALDA pipeline**.

| | `scripts/sync-reference-manual-chapter-numbers.ps1` | `ReferenceManual/update-chapter-numbers.malda` |
|---|---|---|
| **Status** | Preferred for chapter-order changes | Still required for full publish |
| **Updates** | `<title>`, breadcrumb, `<main><h1>`, nav footer | Above **plus** h2/h3, inline “Section X:”, See Also, `navigation.js`, `ReferenceManualPDF.html` |
| **CI** | `ReferenceManualChapterSyncTests` | Manual run |
| **Runtime** | PowerShell only | `malda` + builtins (`embedBagOfWords`, VectorDB, graph) |

The Malda script is **not obsolete**. The PowerShell script does **not** replace PDF generation or in-chapter section renumbering.

**Recommended order:** PowerShell sync → (optional) Malda full pass.

Details: `README-numbering.md`, `docs/planning/phase-1.4-manual-alignment.md`.

---

## History: consolidated Malda variants

### Consolidated three scripts into one (MALDA)

**Before:**

- `update-chapter-numbers.malda` — original basic script
- `update-chapter-numbers-ai.malda` — AI-powered with custom embeddings
- `update-chapter-numbers-ai-refactored.malda` — refactored with built-in embeddings

**After:** one unified `update-chapter-numbers.malda` (current file in `ReferenceManual/`).

The refactored variant was chosen because it uses built-in `embedBagOfWords`, VectorDB/graph support, and inline “Section X:” link handling.

Old duplicate Malda scripts were removed (see git history).

### PowerShell sync added (Phase 1.4)

- `scripts/sync-reference-manual-chapter-numbers.ps1` — deterministic chapter shell from `chapters.json`
- Overlaps Malda on title / breadcrumb / h1 / footer only; Malda remains source for PDF and deep renumbering

---

## `update-chapter-numbers.malda` feature list

1. **Page shell** (overlaps PowerShell): titles, breadcrumbs, main h1, nav footer  
2. **In-chapter structure**: h2/h3 renumbering (`renumberH2`, `renumberH3`)  
3. **Cross-references**: inline “Section X:”, See Also, semantic link repair via graph/VectorDB  
4. **Outputs**: `navigation.js` updates, `ReferenceManualPDF.html`  
5. **Optional**: language rename (`oldLanguageName` / `newLanguageName`)

### Usage

```bash
cd ReferenceManual
malda update-chapter-numbers.malda
```

### Configuration (top of script)

```malda
var renumberH2 = true;   // Renumber h2 section headings per chapter
var renumberH3 = false;  // Renumber h3 subsection headings
```
