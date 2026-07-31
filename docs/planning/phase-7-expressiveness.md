# Phase 7 — Expressiveness

**Status:** In progress (7.1–7.5 shipped 2026-06-05)  
**Roadmap:** [malda-language-purity-roadmap.md](malda-language-purity-roadmap.md) Phase 7

## 7.1 Pipe `|>` + list comprehensions

| Item | Output |
|------|--------|
| Lexer | `|>` token (`PipeForward`); `\|` alone remains sum-type `Pipe` |
| Parser | `left \|> right` between assignment and ternary; `[expr for x in iter if cond]` |
| Interpreter | `Interpreter.Pipe.cs` — pipe desugar + comprehension loop |
| Transpiler | `TranspilePipe` / `TranspileListComprehension` in `CSharpTranspiler.cs` |
| Tests | `Phase7ExpressivenessTests` |

### Usage

```malda
var evens = [x * 2 for x in range(10) if x % 2 == 0];
var sorted = data |> filter((x) => x.active) |> sort();

function suffix(text, ending) {
    return text + ending;
}
var labeled = "hi" |> suffix("!");
```

Pipe semantics: `left |> f(args…)` → `f(left, args…)`. When `left` is an array and `f` is an array pipeline method (`filter`, `map`, `sort`, …), the call routes to the array method. The right-hand side must be a function call, identifier, or lambda.

List comprehensions require an array iterable (`range()`, array literal, or expression evaluating to an array).

### Tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Phase7ExpressivenessTests"
```

## 7.2 `using` / `defer` for resources

| Item | Output |
|------|--------|
| Parser | `using name = expr { … }` (distinct from top-level package `using P;`); `defer { … }` |
| Interpreter | `Interpreter.Resources.cs` — scoped defer stack + dispose protocol |
| Transpiler | `TranspileUsingResource` / `TranspileDefer`; `RuntimeHelpers` defer + `DisposeResourceAsync` |
| Tests | `Phase72ResourceTests` |

### Usage

```malda
class LogFile {
    function dispose() {
        print("closed");
    }
}

using f = new LogFile() {
    defer { print("cleanup"); }
    print("work");
}

function run() {
    defer { print("done"); }
    print("start");
}
```

Resource `using` binds the initializer, runs the body, then calls `dispose()`, `close()`, or `disconnect()` on the value (first match wins). Package imports remain `using package;` at top level only.

`defer` registers cleanup for the current block, function body, or `using` body; actions run in **LIFO** order when the scope exits (including `return`).

### Tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Phase72ResourceTests"
```

## 7.3 `const` / local immutability

| Item | Output |
|------|--------|
| Lexer/parser | `const name = expr;` and `const name: type = expr;` (also `export const`) |
| Runtime | `Environment` tracks const bindings; assignment/`++` to const throws |
| Strict mode | `ConstImmutabilityDiagnostics` (`malda-const` errors under `--strict-types`) |
| Transpiler | Const scope stack + compile-time assign guard |
| Tests | `Phase73ConstTests` |

### Usage

```malda
const limit = 100;
const name: string = "alice";
print(limit + len(name));

function demo() {
    const factor = 2;
    print(factor * 10);
}
```

`const` bindings cannot be reassigned or mutated via compound assignment / increment. Inner blocks may shadow with `var`. Strict mode reports illegal assignments before run.

### Tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Phase73ConstTests"
```

## 7.4 Tier 0 conformance closure

| Item | Output |
|------|--------|
| Conformance cases | `pipe-sort`, `list-comprehension-filter`, `const-read`, `defer-lifo`, `using-dispose` in `conformance/tier0/cases/` |
| Manifest | **100** cases (`scripts/sync-tier0-manifest.ps1`) |
| Spec | §18 expressiveness constructs in `malda-language-1.0.md` |

Locks Phase 7 semantics into the Tier 0 interpreter + C# parity gate alongside `Phase7ExpressivenessTests`, `Phase72ResourceTests`, and `Phase73ConstTests`.

### Run

```powershell
.\scripts\run-tier0-conformance.ps1
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0MaldaConformanceTests|FullyQualifiedName~Tier0BackendMatrixTests"
```

## 7.5 Dict comprehensions

| Item | Output |
|------|--------|
| Parser | `dict { key: value for x in iter if cond }` and `{ key: value for x in iter if cond }` |
| Interpreter | `EvaluateDictComprehensionAsync` in `Interpreter.Pipe.cs` |
| Transpiler | `TranspileDictComprehension` in `CSharpTranspiler.cs` |
| Tests | `Phase75DictComprehensionTests`; Tier 0 `dict-comprehension-map.malda` |

### Usage

```malda
var users = [
    dict { "name": "alice", "score": 10 },
    dict { "name": "bob", "score": 20 }
];
var byName = dict { u.name: u.score for u in users };
var high = { u.name: u.score for u in users if u.score > 15 };
print(byName["alice"]);
```

Keys must evaluate to strings (same rule as `dict { }` literals). Iterable must be an array.

### Tests

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Phase75DictComprehensionTests"
```
