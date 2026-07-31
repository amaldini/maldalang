# Phase 6 — Effects & structured data

**Status:** Complete (2026-06-05)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 6

## Delivered

| Item | Output |
|------|--------|
| 6.1 `@pure` | `PureEffectsDiagnostics` under `--strict-types` (`malda-pure` errors) |
| 6.1 `@effects` | Allow-list IO on non-pure functions (`malda-effects` errors) |
| 6.1 IO catalog | `PureEffectsBuiltIns` — file/print/sleep/spawn/agent/workflow builtins |
| 6.2 `schema` | `schema Name { field: type; }` declarations → `SchemaRegistry` |
| 6.2 `validate()` | Global builtin `validate(schema, value)` → `{ ok, data?, error? }` |
| 6.3 `@within(ms)` | `BoundsDiagnostics` + `WithinBoundsContext` on **functions** and **prompts** |
| CI example | `Examples/Agents/phase6_pure_validate.malda`, `Phase6EffectsTests` |

## Usage

```malda
schema ToolInput {
    name: string;
    note: string?;   // optional field
}

@pure()
function normalizeName(name) {
    return upper(trim(name));
}

var check = validate("ToolInput", rawInput);
if (check.ok) {
    print(normalizeName(check.data.name));
}

@effects("print")
function logTool(msg) {
    print(msg);
}

@within(500)
function boundedWork() {
    return upper("ok");
}

@within(30000)
prompt summarize(text) {
    user "Summarize: " + text;
}
```

Strict mode rejects `@pure` functions that call IO builtins, `print`, `await`, or non-@pure user functions.  
`@effects("print", "io")` allow-lists IO builtins/namespaces on impure functions.  
`@within(ms)` enforces a wall-clock bound per function or `await prompt(...)` call (checked between statements, around `sleep()`, and during agent `think()` via `ThinkDeadlineUtc`).

## Tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Phase6EffectsTests"
```
