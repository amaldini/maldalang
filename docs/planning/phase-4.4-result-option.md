# Phase 4.4 — `Result` / `Option` stdlib

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 4.4

## Goal

Provide ergonomic helpers for variant-style success/failure and optional values without requiring every script to declare its own sum types.

## Shipped

### `result` module

| Method | Behavior |
|--------|----------|
| `result.ok(value)` | Variant `Ok(value)` |
| `result.err(value)` | Variant `Err(value)` |
| `result.map(r, fn)` | Maps payload when `Ok`; passes through `Err` |
| `result.unwrapOr(r, default)` | Payload when `Ok`, else `default` |
| `result.isOk` / `result.isErr` | Boolean tag tests |

### `option` module

| Method | Behavior |
|--------|----------|
| `option.some(value)` | Variant `Some(value)` |
| `option.none()` | Variant `None()` |
| `option.map(o, fn)` | Maps payload when `Some`; passes through `None` |
| `option.unwrapOr(o, default)` | Payload when `Some`, else `default` |
| `option.isSome` / `option.isNone` | Boolean tag tests |

Tags align with common Malda sum-type names (`Ok`, `Err`, `Some`, `None`) and work with user-declared types using the same constructor names.

### Null-conditional operator (`?.`, `?[ ]`)

- `expr?.field` — if `expr` is `null`, result is `null`; otherwise normal member access.
- `expr?.["key"]` — same for dictionary/object string index.

Implemented in parser + interpreter (Tier 0). C# transpiler supports `result.*` / `option.*` calls.

## Examples

```malda
var r = result.ok(10);
var v = result.unwrapOr(result.map(r, (x) => x + 1), 0);

var o = option.some("hi");
print(option.unwrapOr(o, ""));

var name = user?.profile?.name;  // null-safe chain (per-step ?.)
```

## Next

- ~~Phase 4.5 — tagged `catch`~~ — [phase-4.5-tagged-catch.md](phase-4.5-tagged-catch.md)
- IDE completions for `result.` / `option.`
