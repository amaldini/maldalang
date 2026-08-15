# Malda Language Specification 1.0 — Tier 0 Core

**Document:** `docs/spec/malda-language-1.0.md`  
**Status:** Final 1.0 (declared 2026-08-12; Draft from 2026-06-04)  
**Applies to:** Malda Core interpreter and Tier 0 conformance tests (JS Tier 0 is a separate matrix subset; not Final-gated)  
**Normative implementation:** `MaldaLang/Lexer.cs`, `MaldaLang/Parser/Parser.cs`, `MaldaLang/Interpreter/Interpreter.cs`, `MaldaLang/BuiltIns/BuiltInFunctions.cs`

---

## 1. Purpose and scope

This specification defines **Tier 0** semantics: the teachable kernel that runs without optional pack DLLs. It is the contract against which `MaldaLang.Tests/Conformance/Tier0/` is written.

**In scope (Tier 0):**

- Value model, null, truthiness, and core coercion rules
- `match` expressions (literals, identifiers, variants, destructuring patterns used in cases)
- Sum types (`type` declarations) and variant values
- `async` / `await` and task composition via `all()`
- Actor syntax: declaration, `spawn`, `send`, `receive`, `self`
- Dictionary literals (`dict { }`) and missing-key behavior
- Type introspection: `typeOf`, `isNumber` (and related `is*` predicates listed in §10)

**Out of scope (documented elsewhere):**

- Optional packs and platform Tier 2 surfaces (AI tools, workflows, HTTP servers, etc.) — not part of Tier 0
- Full grammar productions — Phase 2.2 (`ReferenceManual/34-grammar.html` expansion)
- Workflows, prompts, components, HTTP server — platform Tier 2
- Full `module { }` blocks — deferred past Final 1.0 (`export type` / `export schema` and selective `import { … } from` shipped; see §14)

---

## 2. Normative precedence

When this document, the Reference Manual, and `SimpleProgrammingLanguage.md` disagree on Tier 0 behavior:

1. **This specification** (once marked *Final*)
2. **Parser and interpreter** (current reference implementation)
3. **Reference Manual** narrative chapters
4. **SimpleProgrammingLanguage.md** (legacy overview)

Until spec 1.0 is marked *Final*, the interpreter plus Tier 0 tests are the practical source of truth; this draft records that behavior and flags intentional future changes.

---

## 3. Programs and execution

A **program** is a list of top-level declarations and statements parsed from one or more `.malda` translation units (`include` splices at parse time; `import` / `using` load isolated modules at runtime).

**Execution model (interpreter):**

1. **Declaration pass** — register classes, actors, functions, prompts, workflows, properties, and sum types without running their bodies.
2. **Statement pass** — execute remaining top-level statements in source order.
3. **Functions** — `function` at top level only (not as nested statements inside blocks). `fn` and `def` are not keywords.

Functions declared in the first pass are callable during the statement pass. `var` bindings are created when their initializer runs; there is no hoisting of `var` initializers across other top-level statements.

---

## 4. Values and types

### 4.1 Runtime value kinds

Every expression evaluates to a **runtime value** with exactly one **value kind**:

| Kind | Malda surface examples | Notes |
|------|------------------------|--------|
| `integer` | `42`, `-1` | 32-bit signed; overflow throws |
| `float` | `3.14`, `1e2` | IEEE double |
| `string` | `"hi"`, `$"x={n}"` | UTF-16 string |
| `boolean` | `true`, `false` | |
| `null` | `null` | Distinct kind, not a number |
| `array` | `[1, 2]` | Ordered, zero-based index |
| `object` | `dict { "a": 1 }`, class instances | Includes dictionary instances |
| `function` | `function f() { }` | User or builtin wrapper |
| `class` | class object | Metaclass reference |
| `variant` | `Ok(7)` after sum-type decl | Tagged union |
| `task` | result of `async expr` | Async work handle |
| `actor` | actor definition object | Internal |
| `actor reference` | `spawn MyActor()` | Handle for `send` |

**Conformance:** `typeof-int.malda`, `is-number.malda` (via `Tier0ConformanceTests` spec anchors).

### 4.2 Static types

Malda 1.0 is **dynamically typed** by default. Optional type hints (`var x: SomeName = …`, function `-> ReturnType`) are validated when `malda` is run with **`--strict-types`** (Phase 4.3): unknown hints error; non-exhaustive sum-type `match` without `default` or all variant cases errors.

### 4.3 `typeOf(value)` and `isTag(value, tag)`

`typeOf` returns a **canonical string tag** for the value kind (Phase 4.2):

| Value kind | `typeOf` tag |
|------------|--------------|
| integer | `"int"` |
| float | `"float"` |
| string | `"string"` |
| boolean | `"bool"` |
| null | `"null"` |
| array | `"array"` |
| dictionary (`dict { }`) | `"dict"` |
| other object / class instance | `"object"` |
| function | `"function"` |
| class | `"class"` |
| actor / actor reference | `"actor"` |
| variant (sum type) | `"variant"` |
| task (`async` handle) | `"task"` |

**Deprecation:** legacy tags `"integer"`, `"boolean"`, and `"dictionary"` are accepted only by `isTag(value, tag)` during the one-release deprecation window; `typeOf` returns canonical tags only.

**Recommended type checks:** `isNumber(x)`, `isString(x)`, `isArray(x)`, `isObject(x)`, or `isTag(x, "int")` — not `x == int(x)` idioms.

---

## 5. Null

### 5.1 Literal and comparisons

- `null` is a first-class literal.
- `==` and `!=` treat two `null` values as equal (`IsEqual`: same kind and both null).

### 5.2 Truthiness

`null` is **falsy** (see §6).

### 5.3 Dictionary and object access

For dictionary instances (`dict { "key": value, … }`):

- **Bracket access** `d["missing"]` when the key is absent evaluates to **`null`**, not an error.
- **Member access** `d.missing` follows the same rule for dictionary instances.

**Conformance:** `dict-missing-null.malda`.

Class field access and missing members may still throw or return defaults per class semantics; only **dictionary** missing keys are guaranteed to yield `null` in Tier 0.

---

## 6. Truthiness

Used by `if`, `while`, `&&`, `||`, and `not`.

| Condition | Truthy? |
|-----------|---------|
| `null` | false |
| `false` | false |
| `true` | true |
| `integer` (any, including `0`) | true |
| `float` (any, including `0.0`) | true |
| `string` (including `""`) | true |
| `array`, `object`, `function`, `variant`, `task`, `actor reference`, … | true |

**Short-circuit:** `&&` and `||` evaluate the right operand only when required.

---

## 7. Coercion and operators

### 7.1 Numeric operations

- `+`, `-`, `*`, `/`, `%` require numeric operands unless a class overload (`__add__`, etc.) applies.
- **String concatenation:** if the left operand of `+` is a string, string conversion applies to the other side per `EvaluatePlus` rules.
- **Integer overflow** on `+`, `-`, `*`, unary `-`, and `++`/`--` throws `RuntimeException` with message `Integer overflow.`

### 7.2 Equality

- `==` / `!=` use structural equality for primitives (same kind and same payload).
- Mixed kinds are unequal unless overloads apply on objects.

### 7.3 Ordered comparison

`<`, `<=`, `>`, `>=` require numeric operands (or object overloads).

### 7.4 Builtin coercions

Tier 1 builtins such as `int()`, `float()`, and `string()` perform explicit conversion and may throw on invalid input. They are **not** implicit coercions in expressions unless documented for a specific builtin.

---

## 8. Sum types and variants

### 8.1 Declaration

```malda
type Result = Ok(value) | Err(message);
type Intent = Search(query: string) | Buy(sku: string, qty: int);
```

- Declares a sum type name and one or more **constructors**. Constructor parameters are positional by order at the call site.
- Each constructor parameter may optionally include a payload type (`name: SchemaType`, the same form as schema fields: primitives, `[]`, `?`, schema or sum-type names). Name-only parameters (`Search(query)`) remain valid and stay untyped in the generated JSON Schema.
- Mixing typed and untyped arms in one `type` is allowed (`Help()` + `Buy(sku: string, qty: int)`).
- Payload types are **not** prompt-parameter typing. Prompt parameters stay name-only (`prompt greet(name)`).
- Constructors are invoked as `Ok(7)`, `Err("failed")`, producing **variant** values. Constructor calls are not statically type-checked; the types feed JSON Schema for `validate` and typed prompts.

### 8.2 Variant shape

A variant value has:

- a **tag** (constructor name, e.g. `"Ok"`);
- a **payload** (ordered list of runtime values, one per constructor parameter).

### 8.3 `typeOf` on variants

`typeOf` on a variant returns `"variant"`. Use `match` for tag-specific logic.

---

## 9. `match` expressions

### 9.1 Syntax

```malda
var result = match subject {
    case pattern: body;
    case otherPattern: body;
    default: body;
};
```

- `match` is an **expression**; it produces a value.
- It may also appear as a **statement**; optional trailing `;` is accepted after the closing `}`.
- Cases are tried **in source order**; the first matching pattern wins.

### 9.2 Patterns (Tier 0)

Supported pattern forms include:

| Pattern | Matches |
|---------|---------|
| Literal | Same literal value (`42`, `"x"`, `true`, `null`) |
| Identifier `x` | Any value; binds `x` in case body |
| `_` | Any value; no binding |
| `Ctor(p1, p2)` | Variant with tag `Ctor`; binds parameters |
| `[a, b]` | Array of length 2 (and similar fixed shapes) |
| `[head, …rest]` | Array with rest binding |
| `{ key: pat }` | Object/dict with field patterns |

If no case matches and there is no `default`, evaluation throws: `Match expression had no matching case and no default case.`

### 9.3 Case body environment

Bindings introduced by the pattern are visible only in that case’s body. The case body runs in a **child environment** chained to the environment active at the `match`; outer bindings remain visible unless shadowed.

### 9.4 Case body value

- **Expression statement** body: value is the expression result.
- **Block** body: **last expression wins** — if the last statement is an expression statement, its value is the result; otherwise the result is `null`.

**Conformance:** `match-literal.malda`, `sum-type-match.malda`, `match-object-*.malda`, `match-block-expression.malda`.

---

## 10. Type predicates (Tier 1 builtins in core)

These builtins are part of the core distribution and are specified here because conformance tests depend on them:

| Builtin | Behavior |
|---------|----------|
| `isNumber(x)` | `true` iff kind is `integer` or `float` |
| `isString(x)` | `true` iff kind is `string` |
| `isArray(x)` | `true` iff kind is `array` |
| `isObject(x)` | `true` iff kind is `object` |
| `typeOf(x)` | Returns tag string per §4.3 |

---

## 11. Async tasks

### 11.1 `async` expression

```malda
var t = async callee(args);
var t2 = async 42;
```

- **`async` applied to a call** starts the call and produces a **task** value without blocking until the callee’s first suspension point (e.g. `await` inside the callee, or `sleep()`).
- **`async` applied to a non-call expression** wraps the evaluated value in an already-completed task.

**Environment rule (1.0):** When `async` starts a user function call, the interpreter must restore the **caller’s environment** after scheduling the task so subsequent `var` bindings in the same scope are not stored on the callee’s activation record. Reference: `WrapCallAsTask` in `Interpreter.CallDispatcher.cs`.

**Known limitation:** Overlapping hot-started user tasks that call `sleep` before the next `var` binding in the same block can race on the shared interpreter environment; bind tasks before starting overlapping sleeps, use immediate-return callees in examples, or `await` between bindings.

### 11.2 `await` expression

- Operand must be a **task**; otherwise: `await requires a task value.`
- `await` blocks until the task completes and yields the task’s result value.

### 11.3 `all(t1, t2, …)` and `all(array)`

`all` composes existing tasks (variadic or single array argument):

- Non-task arguments are treated as already-completed values.
- Returns a **task** whose result is an **array** of results in **input order**.
- **Best-effort concurrency:** all children run to completion; failures do not cancel siblings; after all finish, the **first** error encountered is rethrown.
- Empty `all()` returns a completed task of `[]`.

**Conformance:** `async-await.malda`; extended cases in `InterpreterTests` / `TranspiledAsyncTests`.

---

## 12. Actors (Tier 0 syntax)

Actors provide message-oriented concurrency. Full fairness and scheduling guarantees are **implementation-defined** in 1.0; syntax and message delivery shape are normative.

### 12.1 Actor declaration

```malda
actor Counter {
    message increment();
    on increment() {
        // handler body
    }
}
```

- **`message`** declarations define the message name and parameter list (types optional, not enforced in Tier 0).
- **`on handlerName(...)`** defines a **message handler** (instance method semantics).
- Fields, constructors, and static members follow class-like rules inside the actor body.

Actors are registered in the declaration pass; handlers run with actor instance state.

### 12.2 Spawning

```malda
var ref = spawn Counter(arg1, arg2);
```

- `spawn ActorName` requires a prior `actor ActorName` declaration.
- Evaluates constructor arguments left-to-right.
- Returns an **actor reference** value.

### 12.3 Sending messages

```malda
send ref handlerName(arg1, arg2);
```

- Target must be an **actor reference**; otherwise: `Can only send messages to actor references.`
- Optional `then`, `timeout`, and `catch` clauses (see Reference Manual ch. Actors) control replies and timeouts.

### 12.4 `receive` and `self`

- **`receive()`** (in actor context) waits for the next message and returns its payload (async in interpreter).
- **`self`** refers to the current actor reference inside a handler.

**Conformance:** Actor behavior is covered in `ActorParityTests` (Phase 5 matrix); Tier 0 spec records syntax and value kinds (`typeOf` → `"actor"` for references).

---

## 13. Errors

### 13.1 RuntimeException

Recoverable and bug-reporting failures in the interpreter surface as **`RuntimeException`** with a message and optional source line/file. Uncaught exceptions terminate the current run unless caught by `try` / `catch`.

### 13.2 `try` / `catch` / `finally` / `throw`

Tier 0 includes structured exception statements. Catch clauses are **type-agnostic** in 1.0 (any caught value binds to the catch variable). Detailed exception typing is Phase 4.

---

## 14. Modules (interim — Phase 3)

### 14.1 Import

`import` loads a module into an **isolated environment**, then merges **exported** bindings into the importer (see §14.2).

| Form | Semantics |
|------|-----------|
| `import "path/to/module.malda";` | Resolve `path` relative to the importer file (same rules as `include`), execute module, merge exports |
| `import { a, b } from "path/to/module.malda";` | Same load; merge **only** named bindings from the export surface (error if a name is absent) |
| `import { a, b } from package;` | Same selective merge for an installed / workspace package |
| `import package;` | Load installed package entry (same resolver as `using`) |
| `import alias = package;` | Merge exports into a single namespace object bound to `alias` (not combinable with `{ … } from`) |
| `using package;` | **Deprecated alias** of `import package;` for packages (unchanged behavior) |

Design note: [selective-imports.md](../selective-imports.md).

`include` remains parse-time textual inclusion (all top-level symbols become globals in the host unit).

### 14.2 Export

Top-level `export` on `function`, `var`, `class`, `type`, or `schema` marks a name as visible to importers.

- If a module file contains **any** `export` declaration, **only** exported names are merged (and surfaced to IDE / transpile expand).
- If a module file contains **no** `export` declaration, **all** top-level bindings / types / schemas are merged (backward compatible with SDK preludes).
- `export type T` includes **T** and all of T’s **constructors** on the export surface. Selecting `T` in `import { T } from …` merges those constructors into the importer.
- `export schema S` includes **S** on the export surface (no runtime binding required for selective import; `validate("S", …)` uses the registry populated when the module loaded).

### 14.3 Sum types and modules

Sum-type constructors are defined in the module file where the `type` is declared. Cross-module use: `export type` (or an open module with no exports) plus import / selective import. Constructor tags remain match subjects; `typeOf` returns the kind tag `"variant"`.

Design notes: [selective-imports.md](../selective-imports.md), [phase-3-modules-design.md](../planning/phase-3-modules-design.md).

---

## 15. Conformance suite

**Primary gate:** file-driven suite under `conformance/tier0/` (`manifest.json` + `cases/*.malda` / `*.expect`).

| ID | Case file | Spec section |
|----|-----------|----------------|
| T0-01 | `match-literal.malda` | §9 |
| T0-02 | `dict-missing-null.malda` | §5.3 |
| T0-03 | `typeof-int.malda` | §4.3 |
| T0-04 | `sum-type-match.malda` | §8, §9 |
| T0-05 | `async-await.malda` | §11 |
| T0-06 | `is-number.malda` | §10 |

Manifest IDs `T0-001`…`T0-101` enumerate the full suite (101 cases). Multi-backend rules: [tier0-backend-matrix.md](tier0-backend-matrix.md).

Run:

```powershell
.\scripts\run-tier0-conformance.ps1
.\scripts\report-tier0-parity.ps1
```

Spec anchors: `Tier0ConformanceTests` delegates to file cases (no duplicated inline Malda). See [tier0-construct-coverage.md](../planning/tier0-construct-coverage.md).

---

## 18. Expressiveness constructs (Phase 7)

Normative summary; implementation detail: [phase-7-expressiveness.md](../planning/phase-7-expressiveness.md).

### 18.1 Pipe `|>`

- `left |> f(args…)` desugars to `f(left, args…)`.
- When `left` is an array and `f` is an array pipeline method (`filter`, `map`, `sort`, …), the call routes to the array method on `left`.
- Right-hand side must be a function call, identifier, or lambda.

### 18.2 List and dict comprehensions

- `[expr for name in iterable if condition]` builds a new array.
- `dict { key: value for name in iterable if condition }` and `{ key: value for name in iterable if condition }` build a new dictionary.
- `iterable` must evaluate to an array (`range()`, array literal, or expression).
- Optional `if condition` filters elements.
- Dict comprehension keys must evaluate to strings (same as `dict { }` literals).

### 18.3 Resource `using` and `defer`

- `using name = expr { body }` binds `expr`, runs `body`, then calls `dispose()`, `close()`, or `disconnect()` on the value (first match). Distinct from package import `using P;`.
- `defer { stmt }` registers cleanup for the current block, function body, or `using` body; deferred actions run **LIFO** on scope exit (including `return`).

### 18.4 `const` bindings

- `const name = expr` and `const name: type = expr` declare immutable locals (also `export const`).
- Reassignment, compound assignment, and `++`/`--` on a `const` binding are errors at runtime.
- Under `--strict-types`, illegal assignments are static errors (`malda-const`).
- Inner scopes may shadow a `const` with `var`.

### Conformance anchors

| Case file | Construct |
|-----------|-----------|
| `pipe-sort.malda` | Pipe `|>` |
| `list-comprehension-filter.malda` | List comprehension |
| `dict-comprehension-map.malda` | Dict comprehension |
| `const-read.malda` | `const` read |
| `defer-lifo.malda` | `defer` LIFO |
| `using-dispose.malda` | Resource `using` |

---

## 19. Classes — primary constructor

Additive syntax; classic `class Name { … }` is unchanged.

```malda
class Point(x, y);
class Point(x, y) { function total() { return this.x + this.y; } }
```

- After `class Identifier`, `(` starts a **primary constructor**. Each parameter is a **public** instance field of the same name (optional `: Type` hints are stored on those fields).
- MALDA synthesizes a constructor named like the class whose body is `this.param = param;` for each parameter, in source order.
- A body is optional: `class Point(x, y);` or `class Point(x, y) { … }`. Body members are appended after the synthesized fields and constructor.
- **Illegal in v1:** combining a primary constructor with `extends`; declaring `function ClassName(...)` in the same class; declaring `var` with a primary parameter's name.
- Construction is still `new Point(3, 4)`. Equality remains identity unless the class defines `__eq__`.
- This form is distinct from sum types (`type Point = Point(x, y);`, constructed without `new`) and from schemas (`schema Point { x: int; y: int; }`).

**Implementation:** parser desugar in `Parser.ClassDeclaration`; interpreter and transpilers see a normal class.

---

## 16. Planned amendments (non-normative roadmap)

Versioning and deprecation rules: [CHANGELOG.md](CHANGELOG.md).

| Change | Target phase | Notes |
|--------|----------------|-------|
| ~~`typeOf` tags `"int"`, `"dict"`~~ | Phase 4.2 | **Done** — legacy literals via `isTag` until next MAJOR |
| ~~`--strict-types`~~ | Phase 4.3 | **Done** — CLI flag; see [phase-4.3-strict-types.md](../planning/phase-4.3-strict-types.md) |
| Multi-backend parity matrix | Phase 5 | C# transpile + JS |
| Formal grammar sync | Phase 2.2 | Done — `34-grammar.html` |
| Interpreter task isolation | Post–1.0 | Fix concurrent `async` + `sleep` binding race |

---

## 17. Revision history

| Date | Version | Change |
|------|---------|--------|
| 2026-06-04 | Draft 1.0 | Initial Tier 0 spec (Phase 2.1); anchors Tier0ConformanceTests |
| 2026-06-04 | Draft 1.0 | §14 modules: `import`, `export`, sum-type scoping (Phase 3) |
| 2026-06-05 | Draft 1.0 | §18 expressiveness: pipe, comprehensions, `using`/`defer`, `const` (Phase 7) |
| 2026-08-12 | Final 1.0 | Spec Final declared; Tier 0 interpreter + C# conformance green (`run-tier0-conformance.ps1`) |
| 2026-08-14 | Final 1.0 | §8.1 optional constructor payload types (`Buy(sku: string, qty: int)`) — additive L1b |
