# Phase 4.1 — Type annotations (informational)

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 4.1

## Goal

Surface `param: Type` and `-> Type` hints in the IDE without changing runtime behavior until `--strict-types` (Phase 4.3).

## Shipped (initial)

- Parser already stores `TypeHint`, `ParameterTypeHints`, `ReturnType` on declarations.
- `Tier0TypeHints` registry shared by diagnostics and completions.
- `TypeHintDiagnostics` — `malda-types` **information** diagnostic for unknown hint names.
- `TypeHintCompletions` — IDE/LSP completion after `:` (var/param/field) and `->` / `=>` (return).
- Tests: `TypeHintDiagnosticsTests`, `TypeHintCompletionTests`, existing `TypeHintTests`.

## Known type hints (informational registry)

`int`, `integer`, `float`, `double`, `string`, `bool`, `boolean`, `array`, `object`, `dict`, `dictionary`, `null`, `variant`, `task`, `void`, `any`.

## Next steps
- Document hints in Reference Manual / spec §4 (ongoing)
- ~~Align `typeOf` tags with hints~~ — done in [phase-4.2-type-tags.md](phase-4.2-type-tags.md)
- ~~Wire `--strict-types` to reject unknown hints~~ — [phase-4.3-strict-types.md](phase-4.3-strict-types.md)
