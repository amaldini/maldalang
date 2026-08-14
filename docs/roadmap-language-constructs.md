# MALDA language constructs plan (post–Final 1.0)

**Status:** L1a, L1b, and L2 landed; L1c–L6 plan (L3–L6 remaining)  
**Created:** 2026-08-14  
**Audience:** maintainers choosing the next *language* work after P0 maturity  
**Spec line:** Final 1.0 stays; each landed workstream is a **MINOR** additive contract (or PATCH if docs-only). Breaking parse/runtime is **MAJOR** and is out of scope here.

This is the forward plan for a small set of **AI-first language constructs**. It is not a
wishlist and not a substitute for [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md). P0
trust/tooling work is done: [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md).

**Not in scope:** vertical packs, product apps, drive-by stdlib names, a native `Tree`
type, macros / quote-unquote, session types, typed holes (`??`), actor supervision trees,
Web IDE Desktop parity, JS agents/HTTP/workflows.

---

## Guiding principles

1. **Grammar is for contracts that have stopped moving.** Provider names, tool protocols,
   and model IDs stay in the library. The `chain` keyword is the cautionary tale.
2. **One source, three backends.** A new construct must have an interpreter path and a C#
   transpile path in the same PR. JavaScript may be `n/a` when the feature is host-only
   (prompts, workflows) — say so in [`backend-capability-matrix.md`](spec/backend-capability-matrix.md).
3. **Do not add a second type system.** Deepen `schema` / `type` / `program(Api)` so LLM
   I/O is almost always a validated object, a variant, or a closed program — not loose JSON.
4. **No silent Mode changes.** Prompts that today combine `tools:` with `-> Type` stay
   Mode B. New behavior needs an explicit marker.
5. **Functions beat keywords.** If it can be a builtin, decorator, or `.malda` helper,
   it does not enter the parser.

---

## Themes and priority

| Rank | Workstream | Why | Syntax? |
|------|------------|-----|---------|
| 1 | **L1** Unify schema and sum types | LLM I/O is split across two registries that cannot share a name; `validate()` does not resolve sum types; schema fields cannot nest variants | L1a none; L1b/L1c additive |
| 2 | **L2** Gather-then-extract as a declaration | Mode C is a two-prompt ritual and a gotcha (tools XOR `response_format`) | Yes, explicit marker |
| 3 | **L3** Resource budget beside `@within` | Time bounds exist; token/tool/cost bounds are env hacks on agents | Decorator only |
| 4 | **L4** Workflow determinism beyond the deny-list | WF1001/WF1002 miss helper calls; documented post-Final gap adjacent | No (diagnostics + runtime) |
| 5 | **L5** Grounded values | GraphMemory/RAG citations are not values `match` / `validate` can see | Library first |
| 6 | **L6** Capability tokens | `@effects` is a string allow-list; agents can still invent paths | Deferred until L5/L4 demand it |

Kernel completeness (nested `function`, full `module { }`, JS product parity) is **parallel
and useful**, not this plan. Track those in the spec CHANGELOG / backend matrix, not here.

---

## Sequencing

```text
L1a  registry + validate + nested fields     (no grammar)
  └─ L1b  optional types on variant payloads (small grammar)
       └─ L1c  tagged schema unions          (only if L1a/L1b still leave a hole)

L2   after L1a (extract target is a schema or sum type)

L3   independent of L1/L2; same decorator pipeline as @within

L4   independent; static call-graph then runtime stack mark

L5   library wrapper; grammar only if match/validate must see provenance

L6   not started in this plan
```

Land **L1a** before any new prompt syntax. Do not start L1c, L5 grammar, or L6 in the same
change as L1a.

---

## L1 — Unify schema and sum types

### What is true today

- `schema User { name: string; }` → `SchemaRegistry` → object JSON Schema → `validate("User", v)`.
- `type Intent = Search(q) | Buy(sku, qty)` → `SumTypeRegistry` → `oneOf` + `{ "tag": "Buy", ... }`
  for typed prompts. Constructors may be **name-only** or carry optional payload types
  (`Buy(sku: string, qty: int)`) (**L1b**).
- The same name cannot be both (`SchemaRegistry` / `SumTypeRegistry` throw).
- `validate("Intent", v)` resolves the sum-type `oneOf` schema (**L1a**). Success returns the original dict, not a variant.
- Nested schema fields may name a sum type (`intent: Intent` / `Intent[]`) (**L1a**).
- Typed prompts already accept either a schema name or a sum-type name as `-> Type`
  (`TypedPromptSchemaResolver`).

That split is the highest-leverage language hole: object-shaped LLM output vs intent-shaped
LLM output are two languages.

### L1a — One resolution path (no new syntax)

**Concrete work**

- `SchemaRegistry.ResolveSchemaArgument` / `TryResolve` also resolve sum-type names
  (or a tiny `TypeNameResolver` both call).
- `validate("Intent", value)` returns `{ ok, data?, error? }` against the existing `oneOf` schema.
- Schema field types may name a sum type (`intent: Intent`) and expand to that `oneOf`.
- Keep **exclusive names**. Do not allow `schema Foo` and `type Foo` in one program.
- IDE `malda-schema` diagnostics: unknown field type that is neither primitive, schema, nor
  sum type.
- Docs: gotchas row stays for the name clash; syntax pack says `validate("Intent", …)` works.

**Primary paths:** `MaldaLang/BuiltIns/SchemaRegistry.cs`, `SumTypeRegistry.cs`,
`TypedPromptSchemaResolver.cs`, `MaldaLang/IDE/` schema diagnostics, RM schema + data-types
chapters, `docs/llm/malda-gotchas.md` / `malda-syntax.md`.

**Done when (L1a — landed)**

- Filtered tests cover `validate` on a sum type, nested `schema { field: Intent }`, and the
  existing name-clash throw.
- Interpreter and C# transpile agree (transpile inlines nested sum schemas at emit time).
- JS: `n/a` (`schema` / `validate()` is host-only on the product matrix).

**Risk:** `validate` success still returns a **dict**, not a variant. Coercion to
`Search(q)` / `Buy(sku, qty)` stays on `await prompt … -> Intent`. Document that; do not
change `validate` to always box variants in L1a (would surprise object-schema callers).

### L1b — Typed variant payloads

**Concrete work**

- Additive: `type Intent = Search(query: string) | Buy(sku: string, qty: int)`.
- Name-only constructors remain valid (`Search(query)` stays untyped payload).
- Generated JSON Schema uses those field types (primitives + schema/sum names from L1a).
- This is **not** prompt-parameter typing. Prompt params stay name-only (`AGENTS.md`).

**Primary paths:** `Lexer.cs` / `Parser.cs` (constructor param list), `VariantConstructor`,
`SumTypeRegistry.BuildSchema`, grammar chapter, `ReferenceManualGrammarCoverageTests`.

**Done when (L1b — landed)**

- Parser accepts name-only and typed constructor payloads in the same `type`.
- `validate` / typed prompt schemas use those field types; unknown payload types fail like unknown schema fields.
- Prompt parameters stay name-only (`prompt greet(name)` still rejects `name: string`).
- Interpreter and C# transpile agree. JS: constructors stay arity-only (`n/a` for schema emit).
- Spec CHANGELOG **MINOR**. `scripts/verify-spec-parser-drift.ps1` in the same PR.

### L1c — Tagged schema unions (only if still needed)

**Idea (do not implement until L1a/L1b are used):**

```malda
schema Intent =
    Search { query: string }
  | Buy { sku: string; qty: int }
  | Help { };
```

Desugars to a sum type **and** a JSON schema. `await prompt p() -> Intent` yields a variant.
`validate("Intent", …)` uses the same schema.

**Gate:** ship L1c only if authors still have to declare both a `schema` and a `type` for
one LLM contract after L1a/L1b. Prefer extending `type` over a third keyword (`data`).

---

## L2 — Gather-then-extract as a declaration

### What is true today

| Mode | Surface | `response_format` / appendix | On `await` |
|------|---------|------------------------------|------------|
| A | `-> Type`, no `tools` | yes | validate + repair |
| B | `tools:` present | **omitted** (even with `-> Type`) | validate/repair if `-> Type` |
| C | two prompts: tools, then typed without tools | only on the second | as A |

Example: `Examples/Prompts/prompt_tools_then_structured.malda`. Gotcha in
`docs/llm/malda-gotchas.md`.

### Design constraint

**Do not** reinterpret existing `tools:` + `-> Type` as two LLM calls. That would break
Mode B.

### Proposed shape (exact grammar in the implementing PR)

One `prompt` with an **explicit extract marker**, for example a body field or clause that
cannot be confused with Mode B:

```malda
prompt research(question) -> ResearchAnswer {
    gather: ["read_file", "grep"];
    system: "Use tools, then the extract step will structure the answer.";
    user: question;
}
```

Requirements:

- `gather:` (name TBD) + `-> Type` ⇒ two-phase runtime: tool round, then a **fresh** typed
  prompt without tools (Mode A).
- Plain `tools:` + `-> Type` remains Mode B.
- Extract target may be a schema, a sum type (L1a), or `program(Api)`.
- Offline: constructing the `PromptInstance` without `await` must not call the model; the
  example stays runnable like today’s Mode C sample.

**Rejected here:** a second top-level keyword, macros, or compiling Mode C via `eval`.

**Primary paths:** `PromptDeclaration`, `Parser.cs`, `PromptValue.Execution.cs`,
`CSharpTranspiler` prompt emit, IDE hover/diagnostics, RM §9, gotchas (replace the ritual
row with the new form), `Examples/Prompts/`.

**Done when (L2 — landed)**

- One `prompt` with `gather:` + `-> Type` replaces the two-prompt Mode C ritual.
- Plain `tools:` + `-> Type` remains Mode B (not two LLM calls).
- Interpreter and C# transpile agree. JS: `n/a` (prompts are host-only).
- Offline `prompt(...)` without `await` does not call the model.
- Spec CHANGELOG **MINOR**. `scripts/verify-spec-parser-drift.ps1` in the same PR.

**Risk:** two LLM round-trips and repair loops need a clear error if gather fails. Do not
feed tool JSON into `validate` until the extract step.

---

## L3 — Resource budget (`@budget` beside `@within`)

### What is true today

- `@within(ms)` on functions and prompts: `DeclarationBounds`, `WithinBoundsContext`
  (deadline stack), `BoundsDiagnostics` under `--strict-types`.
- Agent context trimming uses `MALDA_AGENT_CONTEXT_BUDGET_TOKENS` (env), not a declaration.

### Proposed shape

Additive decorator, **do not overload** `@within`’s positional ms argument:

```malda
@within(5000)
@budget(tokens: 4000, tools: 8)
prompt answer(q) -> Answer { ... }
```

- Unknown keys error at diagnostics time (same strictness as `@within`).
- Runtime: abort with a dedicated message when a bound trips (tokens ≈ prompt+completion
  if the backend reports usage; otherwise a documented best-effort count).
- `tools:` count is the number of **invocations** in that prompt/agent turn, not the
  length of the allow-list.
- Optional `cost` only if a backend already exposes it; otherwise omit from v1.

**Primary paths:** `DeclarationBounds.cs`, `WithinBoundsContext.cs` (or a sibling
`ResourceBoundsContext`), `BoundsDiagnostics.cs`, `PromptValue.Execution.cs`, `Agent.cs` /
`Conversation.cs` usage hooks, RM §9.7, Phase 6 tests style (`Phase6EffectsTests`).

**Done when:** decorator parse + diagnostics + runtime abort tests; transpile honors the
same decorator; env var remains a **fallback** for undeclared agents, not a second API.

**Out of L3:** making `@pure` follow a call-graph (that is L4-shaped work for effects).

---

## L4 — Workflow determinism beyond the deny-list

### What is true today

- Runtime: `BuiltInRegistry.GetWorkflowBehavior` + `CheckWorkflowDeterminism` on **direct**
  builtin names (`now`, `random*`, `sleep`, filesystem/HTTP, …).
- IDE: `WorkflowDeterminismDiagnostics` — same list, **no call-graph**. Comment in source:
  “no call-graph analysis”.
- Gotcha: a helper that calls `now()` from a workflow body outside `step` is assumed safe.

### Proposed shape (still not Temporal)

1. **Static:** from a `workflow` body (outside `step` / `onReject`), walk same-file
   user `function` callees (bounded depth) and flag deny-listed builtins. Unknown /
   imported callees: one **Info** diagnostic, not a hard error (no whole-program yet).
2. **Runtime:** if a deny-listed builtin runs while the interpreter is in a deterministic
   workflow section, throw WF1001/WF1002 **even when nested in a helper**. Reuse the
   existing “in workflow / in step” flags; do not invent history comparison.

**Done when:** tests that a `function helper() { now(); }` called from the workflow body
fails at runtime and (when helper is in-file) at IDE; `sleep` inside `step` still allowed.
Docs: gotchas row updated; [`docs/workflows-ha.md`](workflows-ha.md) stays single-writer
SQLite — this is not HA.

**Out of L4:** distributed replay, actor supervision, changing step memoization-by-name.

---

## L5 — Grounded values (library first)

Citations from GraphMemory / tools / files stay on the side. A first-class `Grounded`
keyword is not justified until `match` / `validate` need it.

**v1 (no grammar):** a small namespaced helper (prefer extending an existing namespace
or a `.malda` module under `Examples/` / `packages/` before a new global). Shape:

```malda
var g = grounded.wrap(answer, citations);
g.value;       // payload
g.citations;   // array of { source, id?, span? }
g.sourced;     // bool
```

Wire into one GraphMemory ASK path as an **opt-in** so a showcase exists. Stdlib soft
freeze: do not add a flat `grounded()` alias.

**v2 (only if v1 is used in anger):** a value kind or schema field `grounded<string>`
visible to `match`. That is a spec MINOR of its own.

---

## L6 — Capability tokens (deferred)

`@effects("io")` is a name allow-list. Unforgeable capabilities (`FileRead` passed into a
tool) need a new value model and three-backend story. **Do not start** until L4 nested
effects and L5 provenance show that string allow-lists are the actual incident class.

---

## Explicitly out of scope (do not start as this plan)

| Item | Reason |
|------|--------|
| Native `Tree` | A tree is a directed acyclic `graph`; nested dicts already feed `AnsiConsole.tree` / `ui.*` |
| Macros / quote / compile-time LLM expand | Breaks interpreter ↔ C# ↔ JS and reproducible builds |
| Reinterpreting Mode B as Mode C | Silent break |
| Third keyword `data` before L1c gate | Prefer extending `schema` / `type` |
| Full static types with runtime enforcement of every hint | P0 deferred |
| Nested functions / `module { }` | Kernel completeness, separate |
| JS agents, HTTP, workflows | Backend matrix; not Final-gated |
| Actor supervision trees | P0 deferred list |

---

## Spec, docs, tests (every landing PR)

Follow [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) “How to propose a spec change”:

1. Spec prose + CHANGELOG **MINOR** (or PATCH if docs-only).
2. If the parser moves: `ReferenceManual/34-grammar.html`, `docs/llm/malda-grammar.md`,
   `scripts/verify-spec-parser-drift.ps1`.
3. Reference Manual chapter + `docs/llm/` gotchas/syntax; bump pack **Applies to** version
   only when cutting the release that ships the construct.
4. Example under `Examples/Prompts/` or `Examples/Workflows/` as appropriate.
5. Filtered tests only — never the full suite. Suggested filters:

| Workstream | Filter fragment |
|------------|-----------------|
| L1 | `Schema` / `SumType` / `TypedPrompt` / `validate` |
| L2 | `Prompt` / `SchemaToLlm` / `TranspiledTypedPrompt` |
| L3 | `Phase6Effects` / `Bounds` |
| L4 | `WorkflowDeterminism` / `WorkflowRuntime` |
| L5 | `GraphMemory` / a new `Grounded*` test class |

6. Transpile smoke for any new example that must ship as `.exe`.
7. Dual licence header on new C# files (`MIT OR Apache-2.0`). No new top-level builtin
   without the `AGENTS.md` checklist.

---

## Success metrics

| Metric | Target |
|--------|--------|
| L1a | `validate` + nested fields work for sum-type names; clash still throws |
| L1b | Optional constructor types in schema emit; name-only still parses — **landed** |
| L2 | One declaration replaces the two-prompt Mode C example; Mode B unchanged — **landed** |
| L3 | `@budget` trips in tests without breaking `@within` |
| L4 | In-file helper calling `now()` from a workflow body is WF1001 |
| L5 | At least one opt-in ASK/GraphMemory path returns citations on a wrapper |
| L6 | Still deferred |

The language win is not more keywords. It is: **LLM output is a schema, a variant, or a
`program(Api)`**, gather/extract is a declaration, bounds and determinism are enforced
through helpers, and provenance is a value when we need it.

---

## Related documents

| Doc | Role |
|-----|------|
| [`docs/architecture.md`](architecture.md) | Engine map |
| [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) | Completed P0; deferred non-language items |
| [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) | Spec semver + post-Final gaps |
| [`docs/spec/malda-language-1.0.md`](spec/malda-language-1.0.md) | Tier 0 kernel (L1/L2 are mostly Tier 2 prompt/schema) |
| [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) | Mode A/B/C, schema vs type names, WF deny-list |
| [`docs/workflows-ha.md`](workflows-ha.md) | W2 ops model (unchanged by L4) |
| [`ReferenceManual/09-functions.html`](../ReferenceManual/09-functions.html) | Prompts, `@pure`/`@effects`, `program(Api)` |
| [`ReferenceManual/13-graphs.html`](../ReferenceManual/13-graphs.html) | Why not `Tree` |
| [`docs/announcement.md`](announcement.md) | Why not macros |

---

## Revision history

| Date | Change |
|------|--------|
| 2026-08-14 | Initial plan: L1–L6 from post-P0 language-construct discussion (Graph vs Tree, no macros, AI-first grammar filter) |
| 2026-08-14 | L1a landed: `validate` + nested schema fields resolve sum-type names |
| 2026-08-14 | L2 landed: `gather:` + `-> Type` gather-then-extract prompts |
