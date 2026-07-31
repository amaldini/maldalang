# Phase 4.5 — Tagged catch

**Status:** Complete (2026-06-04)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 4.5

## Goal

Filter `catch` clauses by exception shape using a guard expression, e.g. `catch (e if e.kind == "IO")`.

## Shipped

- Parser: `catch (identifier if expression) { … }` — filter requires a bound variable.
- Interpreter: clauses evaluated in order; first matching filter runs; fixed prior bug where every clause was treated as a match.
- Recommended throw shape for tags: `throw dict { "kind": "IO", "message": "…" };` so `e.kind` / `e.message` work in filters and handlers.
- C# transpiler: `MALDAException` preserves thrown values; catch filters emit a guard before the catch body.

## Examples

```malda
try {
    throw dict { "kind": "IO", "message": "disk full" };
} catch (e if e.kind == "IO") {
    print("io: " + e.message);
} catch (e) {
    print("fallback: " + e);
}
```

String throws still work with untagged `catch (e)` or filters comparing the whole value: `catch (e if e == "plain")`.

## Error model (with Phase 4.4)

| Mechanism | Use when |
|-----------|----------|
| `result` / `option` | Expected failure/success without stack unwind |
| `throw dict { kind, message }` + tagged `catch` | Structured errors with recovery branches |
| Untagged `catch (e)` | Generic fallback |

## Next

- Phase 5 — multi-backend conformance expansion
- Phase 7.2 — `using` / `defer` (after 4.5)
