# Phase 4.2 — Canonical `typeOf` tags

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 4.2  
**Depends on:** [phase-4.1-type-annotations.md](phase-4.1-type-annotations.md)

## Goal

Align runtime `typeOf` string tags with informational type hints (`int`, `bool`, `dict`, …) and surface sum-type / async kinds that previously returned `"unknown"`.

## Shipped

- `Tier0TypeTags` — canonical tag registry, `GetTag`, `MatchesTag`, legacy alias normalization (`integer`→`int`, `boolean`→`bool`, `dictionary`→`dict`).
- `BuiltInTypeOf` uses `Tier0TypeTags.GetTag`; `dict { }` instances report `"dict"`, not `"object"`.
- `isTag(value, tag)` — tag check with legacy alias support during deprecation.
- C# transpiler `RuntimeHelpers.TypeOfValue` / `IsTag` aligned with interpreter.
- Conformance: `Tier0ConformanceTests` tag cases; `Tier0TypeTagsTests`.

## Canonical tags

`int`, `float`, `string`, `bool`, `array`, `dict`, `object`, `null`, `variant`, `task`, `function`, `class`, `actor`

## Deprecation

| Legacy tag | Canonical | Migration |
|------------|-----------|-----------|
| `integer` | `int` | Prefer `typeOf(x) == "int"` or `isTag(x, "int")` |
| `boolean` | `bool` | Prefer `"bool"` / `isTag` |
| `dictionary` | `dict` | Prefer `"dict"` for `dict { }` values |

Removal of legacy **string literals** in comparisons is scheduled for the next **MAJOR** spec bump per [CHANGELOG.md](../spec/CHANGELOG.md).

## Next

- ~~Phase 4.3 — `--strict-types`~~ — [phase-4.3-strict-types.md](phase-4.3-strict-types.md)
