# Parser / Reference Manual Drift Audit (Phase 0)

**Date:** 2026-06-04  
**Scope:** Tier 0 surface — lexer, parser, interpreter builtins vs. Reference Manual chapters 02, 03, 07, 08, 09, 22, 23, `newpotentialfeatures.md`, and `SimpleProgrammingLanguage.md` (secondary)  
**Implementation sources:** `MaldaLang/Lexer.cs`, `MaldaLang/Parser/Parser.cs`, `MaldaLang/Interpreter/Interpreter.cs`, `MaldaLang/BuiltIns/BuiltInFunctions.cs`

---

## 1. Executive summary

- **Overall severity: medium–high for documentation trust; low for runtime surprises on documented Tier 0 paths.** The lexer/parser implement a substantially larger Tier 0 surface than `22-grammar.html` describes; the manual’s narrative chapters (03, 07, 08, 09) are largely aligned with behavior where they exist, but **lexical/grammar chapters are stale**.
- **Grammar (2026-06-04, Phase 2.2):** `ReferenceManual/22-grammar.html` now documents workflows, actors, sum types, `async`/`await`, `dict`/`graph`/object literals, exceptions, send/spawn/receive, variant patterns, and compound assignment. Residual drift: generated keyword list in CI (P1), nested `FunctionDecl` policy (P0 deferred).
- **Top gap — keyword inventory split:** `02-lexical-structure.html` lists a **short** keyword set; `23-appendix.html` is closer to the lexer but still wrong (`reply` listed as reserved; **workflow** keywords missing). Parser/lexer keywords are the de facto source of truth.
- **Top misleading doc — type checking:** `03-data-types.html` §4.6 recommends `x == int(x) or x == float(x)` instead of `typeOf()` / `isNumber()`; this is called out in the purity roadmap Phase 1.4.
- **Runtime vs manual (aligned):** `typeOf(42)` → `"integer"` (manual built-ins + `Tier0ConformanceTests`); dict missing keys → `null` (manual §4.4 + interpreter + conformance test). **Future spec drift:** roadmap Phase 4.2 targets tag `"int"` and `"dict"`, not current behavior.
- **`newpotentialfeatures.md`:** “Already implemented” list (match, destructuring, sum types, async/await, prompts) matches parser; actor message declarations are documented in `13-actors.html` and parsed — consistent.

---

## 2. Method

| Compared | Method |
|----------|--------|
| **Lexer** | `TokenType` enum + `Lexer.cs` `Keywords` map + punctuation handlers (`=>`/`->` both become `TokenType.Arrow` via `=`+`>` or `-`+`>`) |
| **Parser** | `Parser.cs` — `Declaration()`, `Statement()`, `MatchExpression()`, `ParsePattern()`, `Primary()`, workflow/actor/prompt/type blocks |
| **Interpreter semantics** | `Interpreter.cs` dictionary indexing; `BuiltInTypeOf` in `BuiltInFunctions.cs` |
| **Reference Manual** | Read/grep: `02-lexical-structure.html`, `03-data-types.html`, `07-expressions.html`, `08-control-structures.html`, `09-functions.html`, `22-grammar.html`, `23-appendix.html`, cross-chapters (`01-introduction.html` include/using, `06-graphs.html`, `13-actors.html`, `31-durable-workflows.html`) |
| **Secondary** | `SimpleProgrammingLanguage.md` §2.3 keywords; `ReferenceManual/newpotentialfeatures.md` |
| **Tests** | `MaldaLang.Tests/Conformance/Tier0/Tier0ConformanceTests.cs` (documented runtime tags) |

Precedence per roadmap: **parser/interpreter > Reference Manual > SimpleProgrammingLanguage.md**.

---

## 3. Keywords & tokens table

Legend: **Y** = present/supported; **N** = not present; **Partial** = mentioned in some manual locations only.

| Token / keyword | In lexer? | In parser? | Documented in manual? | Drift notes |
|-----------------|-----------|------------|------------------------|-------------|
| `function` | Y (`TokenType.Function`) | Y (declarations, methods, actors) | Y (`09-functions`, appendix) | Canonical form per project rules |
| `fn` | Y (alias → `Function`) | Y | Partial (`09` yes; `02` **no**) | Alias not in `02` keyword list |
| `def` | Y (alias → `Function`) | Y | Partial (`09` + appendix; `02` **no**) | Same |
| `type` (sum types) | Y | Y (`TypeDeclaration`) | Y (`03` §4.5, `08` variant patterns) | **Not** in `02` keywords; appendix has `type` |
| `match` / `case` / `default` | Y | Y (expr + stmt; stmt may omit trailing `;`) | Y (`08`, grammar stmt/expr) | Grammar `Pattern` lacks **variant** patterns; parser has `VariantPattern` |
| `async` / `await` | Y | Y (unary in `Unary()`) | Y (`07` §async) | **Not** in grammar expressions; appendix precedence omits them |
| `prompt` | Y | Y (`PromptDeclaration`; name-only params) | Y (`09` §prompts) | **Not** in `02` keywords; object-literal + statement bodies parsed |
| `actor` | Y | Y | Y (`13-actors`) | File `13-actors.html` titled **ch. 15** (numbering drift) |
| `message` | Y | Y (in `ActorDeclaration`) | Y (`13` §message declarations) | **Not** in `02` keywords |
| `spawn` | Y | Y (`SpawnExpression`) | Y (`13`) | Not in grammar `Primary` |
| `send` | Y | Y (`SendStatement`; `then`/`timeout`/`catch`) | Y (`13`) | Not in grammar statements |
| `receive` | Y | Y (`ReceiveExpression`) | Y (`13`) | **Not** in `02` keywords |
| `on` / `self` / `then` / `timeout` | Y | Y (actors + send) | Y (`13`, `02` partial) | `then`/`timeout` actor-related; also workflow `timeout` |
| `workflow` | Y | Y (`WorkflowDeclaration`, `WorkflowBlock`) | Y (`31-durable-workflows`; grammar name only) | **No** grammar productions; workflow keywords missing from appendix |
| `step` / `approval` / `wait` | Y | Y | Partial (`31` examples) | `wait` uses identifier `awaitSignal` (not a keyword) |
| `retry` / `backoff` / `delay` / `maxDelay` / `compensate` / `onReject` | Y | Y (step/approval options) | Partial (`31`) | Lexer keywords; **not** in appendix reserved list |
| `dict` | Y | Y (`dict { }` literal) | Y (`03`, `07`) | Grammar `Literal` has no `dict` production |
| `graph` / `directed` / `undirected` | Y | Y (`GraphLiteralExpression`) | Y (`06-graphs`) | Not in grammar |
| `@` decorators | Y (`TokenType.At`) | Y (functions, properties; `@COMPONENT` sugar) | Partial (`22` property; `30-full-stack` REST) | Grammar: `Decorator+ PropertyDecl` only; parser also decorates **functions** |
| `include` | Y | Y (top-level; recursive parse) | Y (`01-introduction` §1.7) | **Not** in `02` keywords |
| `using` | Y | Y (top-level import) | Y (`01` §1.7) | **Not** in `02` keywords |
| `component` | Y | Y (`ComponentDeclaration` / `@COMPONENT`) | Partial (`30-full-stack`; appendix) | **Not** in `02` keywords |
| `property` | Y | Y | Partial (`22`, appendix) | Web/backend feature |
| Sum types `\|` | Y (`TokenType.Pipe`) | Y (`type Name = Ctor \| Ctor`) | Y (`03`) | Pipe used only in type decls, not manual §02 |
| `foreach` | Y | Y | Y (`08`; appendix) | **Not** in `02` keyword list |
| `try` / `catch` / `finally` / `throw` | Y | Y | Y (`08` exception section) | **Absent** from `22-grammar.html` statements |
| `var` + type hint `: Type` | Y (identifier after `:`) | Y | Partial (functions `09`; vars less prominent) | Grammar `VarDecl` includes optional `: Identifier` |
| `=>` / `->` | Y (both → `Arrow`) | Y (lambda body; return types) | Y (`02` =>; `09` ->) | Lexer does not distinguish `=>` vs `->` token types |
| `reply` | **N** (not keyword) | N (builtin call) | Partial (`13`, appendix **lists as reserved**) | **P0 doc error:** appendix claims reserved word |
| `&&` / `\|\|` | Y | Y | Y (`02` operators) | Grammar documents both |
| `++` / `--` / `+=` … | Y | Y | Y (`07`) | Not in grammar |
| `$"..."` / `$"""..."""` | Y | Y | Y (`02`, `07`) | Not in grammar `String` production |
| `{ }` object literal | Y | Y (`ObjectLiteralExpression`) | **Conflict** | `SimpleProgrammingLanguage.md` says no raw `{ foo: 1 }` JSON-style literals; parser **allows** `{ key: value }` expressions |
| `dict` vs `typeOf` tag | N/A | N/A | Partial | `typeOf` returns `"object"` for dict instances (`ValueType.Object`); no `"dict"` tag (roadmap proposes one) |

---

## 4. Grammar chapter gaps

### 4.1 In parser — **not** in `22-grammar.html` (or only named, no production)

| Construct | Parser location | Grammar status |
|-----------|-----------------|----------------|
| `try` / `catch` / `finally` / `throw` | `TryStatement`, `ThrowStatement` | Missing |
| `foreach` / `for (var x in arr)` | `ForeachStatement`, `ForStatement` | Only classic `for (...;...;...)` |
| `send` … `then` … `timeout` … `catch` | `SendStatement` | Missing |
| `spawn Actor()` | `SpawnExpression` | Missing |
| `receive()` | `ReceiveExpression` | Missing |
| `dict { "k": v }` | `DictionaryLiteralExpression` | Missing |
| `graph directed/undirected { nodes: …, edges: … }` | `GraphLiteralExpression` | Missing |
| `async expr` / `await expr` | `AsyncExpression`, `AwaitExpression` | Missing |
| `{ key: value }` object literal | `ObjectLiteralExpression` | Missing (grammar `ObjectPattern` only) |
| Variant patterns `Ok(v)` | `VariantPattern` in `ParsePattern()` | `Pattern` has no variant production |
| `match` as statement without `;` | `Statement()` wraps `MatchExpression` | `MatchStmt` requires `;` after statement |
| Compound assignment `+=`, `*=` | `AssignmentStatement` | Missing |
| Prefix/postfix `++`/`--` | `Unary` / `PostfixExpression` | Missing |
| Interpolated strings | Lexer + expr | Missing |
| Ternary `? :` | `Ternary()` | Missing |
| `ActorDecl` body (`message`, `on`) | `ActorDeclaration` | **Named** in `Program`, **no** `ActorDecl` rules |
| `PromptDecl` | `PromptDeclaration` | **Named**, no rules |
| `WorkflowDecl` (`step`, `approval`, `wait = awaitSignal(...)`) | `WorkflowDeclaration` | **Named**, no rules; `31-durable-workflows` links here incorrectly |
| `TypeDecl` (sum types) | `TypeDeclaration` | **Named**, no rules |
| `ComponentDecl` | `ComponentDeclaration` | **Named**, no rules |
| Decorators on **functions** | `FunctionDeclarationWithDecorators` | Only `DecoratedPropertyDecl` |
| `IncludeStmt` / `UsingStmt` | Top-level | In grammar |
| Parameter decorators `@PathParam` | Parser supports on function params | Missing |

### 4.2 In grammar — **not** enforced / differs in parser

| Grammar rule | Drift |
|--------------|-------|
| `FunctionDecl` as `Statement` | Parser only parses `function`/`fn`/`def` at **top level** (`Declaration()`), not inside `Statement()` / blocks |
| `MatchCase` → `Statement ";"` | Parser allows optional `;` and match-as-statement without trailing `;` |
| `LambdaParams "=>"` | Token is generic `Arrow`; `->` also used for return types (same token type) |
| `Primary` → `Literal` only | Parser `Primary` is much richer |

### 4.3 Manual claims grammar is authoritative

- `31-durable-workflows.html` “See Also” → `22-grammar.html` for “workflow grammar reference” — **link target does not contain workflow productions**.

---

## 5. Semantic drift

### 5.1 Runtime behavior (manual vs code)

| Topic | Manual says | Code does | Severity |
|-------|-------------|-------------|----------|
| **Dict missing key** | `d["missing"] == null` (`03` §4.4) | `DictionaryInstance.TryGetEntry` → `RuntimeValue.Null()` | **Aligned** |
| **`typeOf` integer** | Returns `"integer"` (`11-built-in-functions.html`) | `BuiltInTypeOf` → `"integer"` for `ValueType.Integer` | **Aligned** (roadmap future: `"int"`) |
| **`typeOf` dict** | Not specified as `"dict"` | Dict literals → object/dictionary instance → `"object"` | **Gap** vs future Tier 0 spec |
| **Type checking idiom** | `if (x == int(x) or x == float(x))` (`03` §4.6) | Works via coercion but is awkward; `typeOf` / `isNumber` clearer | **P0 misleading** |
| **`await` non-Task** | Runtime error (`07`) | Interpreter enforces Task type on await | **Aligned** |
| **Prompt params** | Name-only in examples (`09`) | Parser: name-only — **no** `param: type` in prompts | **Aligned** |
| **`newpotentialfeatures`** | Sum types, async, match implemented | Parser + tests confirm | **Aligned** |

### 5.2 `SimpleProgrammingLanguage.md` (secondary)

| Topic | Drift |
|-------|-------|
| §2.3 keywords | **Severely stale** — no `match`, `async`, `dict`, `actor`, `prompt`, `try`, etc. |
| §3.1 `int` type name | Uses `int`; runtime `typeOf` uses `"integer"` |
| JSON/object literals | States MALDA doesn’t support `{ foo: 1 }` top-level; parser supports **`{ key: value }` object literals** in expressions |
| Exception catch | Notes all `catch` clauses match any type — aligned with `08-control-structures.html` |

### 5.3 Chapter / breadcrumb numbering (HTML)

Systematic **file slug vs displayed chapter number** mismatch (renumbering script / nav drift). Examples: `13-actors.html` titled ch. 15; `22-grammar.html` title 34 vs breadcrumb 33; `23-appendix.html` title 35 vs breadcrumb 34.

**Broken link:** `02-lexical-structure.html` §3.5 links `08-functions.html#lambda` — file is **`09-functions.html`**.

### 5.4 `newpotentialfeatures.md` vs parser

Pattern matching, destructuring, sum types, async/await, prompts, actor message sugar — **consistent** with parser and manual where documented.

---

## 6. Prioritized fix list

### P0 — wrong or misleading (fix before spec 1.0)

1. ~~**`03-data-types.html` §4.6**~~ — **Fixed 2026-06-04:** `typeOf` / `isNumber`.
2. ~~**`23-appendix.html` §35.1**~~ — **Fixed 2026-06-04:** removed `reply`; added workflow keywords; note on `reply` builtin.
3. ~~**`22-grammar.html`**~~ — **Fixed 2026-06-04:** expanded productions (Phase 2.2); partial-grammar warning removed.
4. ~~**`02-lexical-structure.html` §3.3**~~ — **Fixed 2026-06-04:** keyword list synced with lexer; lambda link → `09-functions.html`.
5. **Grammar `FunctionDecl` as nested `Statement`** — Document top-level-only restriction or change parser (deferred to P1).

### P1 — missing documentation

1. Workflow keyword block and productions (or dedicated grammar snippet).
2. ActorDecl, PromptDecl, TypeDecl, ComponentDecl productions.
3. Variant patterns in grammar; `match` statement semicolon rules.
4. Document `{ key: value }` vs `dict { }`.
5. Single reserved-word list generated from `Lexer.cs`.
6. Fix `02` → `09-functions.html` lambda link.
7. Update or deprecate `SimpleProgrammingLanguage.md` §2.3 keywords.

### P2 — cosmetic / navigation

1. ~~Fix chapter numbering~~ — **Fixed 2026-06-04:** `scripts/sync-reference-manual-chapter-numbers.ps1` + `ReferenceManualChapterSyncTests`.
2. ~~Align see-also chapter numbers~~ — partial (built-ins / workflows); run sync script after `chapters.json` edits.
3. Document `->` vs `=>` (same `Arrow` token).

---

## 7. Suggested owners

| Item | Owner |
|------|--------|
| `03-data-types` type-checking section | **Doc** |
| `02` / `23` keywords, appendix `reply`, links, chapter numbers | **Doc** |
| `22-grammar.html` expansion or partial banner | **Doc** (+ **Parser** review) |
| `typeOf` tag canonicalization | **Both** (spec + `BuiltInTypeOf` + tests) |
| Generate keyword list from `Lexer.cs` in CI | **Both** |
| `FunctionDecl` nesting policy | **Both** |
| Object literal `{ }` vs `dict { }` policy | **Both** if restricting syntax |

---

## Appendix: Implementation quick reference

**Lexer:** ~97 keyword map entries including actor, workflow, `dict`, `graph`, `match`, `async`/`await`, `prompt`, `type`, `include`, `using`, `component`, `property`, etc.

**Top-level declarations:** `include`, `using`, `workflow`, `actor`, `class`, `prompt`, `property`, `type`, `component`, decorated function/property, `function`/`fn`/`def`.

**Conformance anchors:** `MaldaLang.Tests/Conformance/Tier0/Tier0ConformanceTests.cs`.
