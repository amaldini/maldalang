# Phase 1.4 — Manual alignment (complete)

**Date:** 2026-06-04

## Done

| Item | Change |
|------|--------|
| `03-data-types` §4.6 | Already fixed in P0 (`typeOf` / `isNumber`) |
| Chapter numbering | `scripts/sync-reference-manual-chapter-numbers.ps1` syncs title, breadcrumb, h1, nav-footer from `ReferenceManual/chapters.json` (35 chapters) |
| Built-ins ch. 12 | §12.2 `math` / `str` / `io`; §12.25 optional-packs note (generic; no vertical API table) |
| Appendix | Optional-packs appendix describes out-of-tree packs via `loadNativeModule` |
| `12-input-output` | Documents `io.print` as preferred |
| `31-durable-workflows` | Grammar see-also points to partial BNF + `Parser.cs` |
| CI guard | `ReferenceManualChapterSyncTests` |

## Regenerate chapter numbers

See `ReferenceManual/README-numbering.md` for the two-tool workflow.

```powershell
# Chapter shell (CI-tested) — run after every chapters.json edit
powershell -File scripts/sync-reference-manual-chapter-numbers.ps1

# Full manual + PDF — when you need h2/h3, cross-links, ReferenceManualPDF.html
cd ReferenceManual
malda update-chapter-numbers.malda
```

`update-chapter-numbers.malda` is **not** obsolete; it complements the PowerShell sync (`ReferenceManual/SCRIPT_CONSOLIDATION.md`).

## Deferred

- Full `22-grammar.html` expansion (P1 in drift audit)
- `SimpleProgrammingLanguage.md` keyword refresh
- `ReferenceManualPDF.html` monolith regen (separate publish step)
