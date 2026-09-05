# MALDA trust roadmap (trust before syntax)

**Status:** DT0–DT4 and DT6 landed 2026-08-16; DT7 interpret/transpile pairs landed 2026-08-17; DT8 ship-contract registry + exit identity landed 2026-09-05; DT5 named (Second Brain instance others use; out of repo)  
**Created:** 2026-08-15  
**Audience:** maintainers prioritizing the OSS core after P0 + L1–L6

This is the forward plan after
[`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) (complete) and
[`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md)
(L1a–L6 landed; L1c gated). The language surface is no longer the toy gap.
What still reads as toy is **trust**: types that do not bind at publish,
interpreter ≠ transpile, silent gotchas, and no program that hurts if it
breaks. Toolchain **1.0.0** is tagged.

**Not in scope here:** new syntax / L1c / pipes / keywords, new builtins or
flat aliases, a full static type system with runtime enforcement of every
hint, a Temporal-equivalent cluster, actor supervision trees, a public
package registry, Web IDE Desktop parity, or product apps / vertical packs
(those stay outside this repository — `AGENTS.md`).

Do not confuse `--typed-transpile-level` (numeric CLR emit;
[`docs/native-numeric-rollout.md`](native-numeric-rollout.md)) with type
analysis. Flat aliases remain scheduled for a later **MAJOR**. `fn` / `def`
were removed (Unreleased MAJOR) because they read as a teaching-language
costume.

---

## Guiding principle

Ship **measurable trust** before new syntax. Publish is the contract
boundary. Interpret stays dynamic.

---

## Themes and priority

| Rank | Workstream | Why |
|------|------------|-----|
| 0 | **DT0** Roadmap file | One place to track this trust work (this document) |
| 1 | **DT1** First impression | CI / announcement / templates must lead with the characteristic file and say what MALDA is not |
| 2 | **DT2** Strict compile | `var n: int = "abc"` must not emit an `.exe` |
| 3 | **DT3** Transpile smoke | Showcases that claim to ship must compile; one interpret/transpile pair catches silent diffs |
| 4 | **DT4** Loud gotchas | Interpolation and `parseJson` must not succeed wrongly with no feedback |
| 5 | **DT5** One real service | Out of this repo — a process that must stay up |
| 6 | **DT6** Toolchain 1.0 | Same `<Version>` + tag after DT2-default and DT3 — **landed** `v1.0.0` |
| 7 | **DT7** Interpret/transpile pairs | Same `.malda`, same stdout and exit on interpret and C# transpile |

```text
DT0  roadmap file
  ├─ DT1  first impression (docs / CI, parallel)
  └─ DT2  compile --strict-types (opt-in, then transpile default)
       ├─ DT3  expand smoke + interpret/transpile pair
       └─ DT4  interpolation + parseJson diagnostics
            └─ DT6  toolchain 1.0 (landed v1.0.0)
                 └─ DT7  curated interpret/transpile pairs (landed)
DT5  one always-on service (out of repo; dashed dependency for DT6 honesty)
```

---

## DT0 — Roadmap file

**Done when:** this file exists; [`docs/architecture.md`](architecture.md)
Docs layout links here; [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) has a
PATCH tracking-only row.

---

## DT1 — First impression

README and [`docs/start-here.md`](start-here.md) already start from
`Examples/Basics/first_look.malda`. Linux/macOS CI still ran
`hello_world.malda`.

| Concrete work | Done when |
|---------------|-----------|
| Cross-platform CI runs `malda Examples/Basics/first_look.malda` | [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) Linux + macOS |
| Announcement version matches the CLI csproj | [`docs/announcement.md`](announcement.md) |
| Templates / README say what this is **not** | workflow = SQLite single-writer; JS = subset; types = runtime-dynamic hints |

Do not rewrite the Learn Programming Basics examples.

---

## DT2 — Strict compile

`--strict-types` exists on `malda run` only. Analysis already lives in
[`MaldaLang/IDE/StrictTypesOptions.cs`](../MaldaLang/IDE/StrictTypesOptions.cs)
(`Default` vs `Enabled`).

| Phase | Behavior |
|-------|----------|
| **A — opt-in** | `malda compile … --strict-types` refuses emit when analysis has Errors (`Enabled`) |
| **B — transpile default** | `compile --mode transpile` uses `Enabled`; escape is `--lenient-types`. `malda run` stays opt-in |

Interpret remains dynamic. This is not a full type checker — it is the
ship boundary.

**Done when:** filtered tests (`FullyQualifiedName~StrictCompile`);
`var n: int = "abc"` does not produce an `.exe` under transpile default;
compile help lists both flags; gotchas + Reference Manual say publish is
the boundary.

---

## DT3 — Transpile smoke on programs that claim to ship

Extend [`MaldaLang.Tests/TranspileSmokeTests.cs`](../MaldaLang.Tests/TranspileSmokeTests.cs)
(already more than the four files cited in the historical P0 note). Keep
the existing CI filter name.

| Concrete work | Done when |
|---------------|-----------|
| Add 2–4 README showcase files **if** they compile in CI time | Listed, or documented `n/a` |
| One interpret + transpile pair on a small file (same stdout / exit) | Catches silent semantic diffs from [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) |

Large showcases (Ralph / Second Brain) join the list only if already green
and not minutes-long; otherwise a small wrapper or an explicit `n/a`.

---

## DT4 — Loud gotchas

Same pattern as UI1001 / WF1001. Only silences that **run and do the wrong
thing**. Do not start a second type system. Do not change `run` semantics
for ignored hints (that is DT2 on compile).

| Priority | Case | Direction |
|----------|------|-----------|
| 1 | `"n is {n}"` without `$` | Diagnostic if a plain string contains `{ident}` |
| 2 | `parseJson` vs `parseJSON` | Error text names the other builtin |
| 3 | Hints ignored at runtime | DT2 on compile only |

Leave inclusive `randomInt`, flash one-shot, and jobs-vs-workflows as
documented.

**Done when:** gotchas rows updated + filtered tests.

---

## DT5 — One service that must stay up (out of repo)

Not a PR in this repository. The named instance is Second Brain (the
in-repo showcase, run privately). Announcement copy: *I run a Second
Brain instance that others use.* No workplace detail in this repo.

OSS core stops at honest templates/docs and a usable
`malda compile --strict-types`.

---

## DT6 — Toolchain 1.0

**Landed 2026-08-16.** Language Spec was already **Final 1.0**. CLI/Desktop
are `<Version>1.0.0</Version>`; notes in [`docs/releases/v1.0.0.md`](releases/v1.0.0.md);
tag `v1.0.0` (`ReleaseVersionGuardTests`).

DT2-B (transpile default) and DT3 were the hard gates and had already
landed. DT5 remains out of repo and is not a tag gate.

---

## DT7 — Interpret/transpile equivalence pairs

DT3 compile-only smoke does not prove the published binary behaves like
`malda run`. The one-line pair (`io.print("pair-ok")`) was not a contract.

**Contract:** for each listed program, interpret and
`compile --mode transpile` then run. Compare **exit code** and **stdout**
after `\r` strip + trim. On compile failure, fail with paths to
`build_errors.txt` and `GeneratedProgram.cs`. If a pair fails, fix the
transpiler (or the example). Do not weaken to `Contains`. Mark `n/a` only
with a one-line reason.

Stderr is out of v1 (Spectre / logging noise). Redirected OEM best-fit of
Unicode punctuation (em dash vs hyphen) is the same class as the TTY
gotcha — keep pair fixtures ASCII-punctuation. JS / PWA is out (backend
matrix). Workflow journals stay in
[`WorkflowTranspilerParityTests`](../MaldaLang.Tests/WorkflowTranspilerParityTests.cs).

| Surface | Role |
|---------|------|
| [`InterpretTranspilePairTests`](../MaldaLang.Tests/InterpretTranspilePairTests.cs) | Named CI filter; Example files + inline gotcha fixtures |
| [`InterpretTranspilePair`](../MaldaLang.Tests/InterpretTranspilePair.cs) | Shared assert; file cases copy sibling `.malda` into a temp dir |
| [`TranspileSmokeTests`](../MaldaLang.Tests/TranspileSmokeTests.cs) | Compile-only (unchanged) |

**v1 corpus (offline, deterministic):**
`first_look`, `schema_validate`, `schema_sumtype_validate`,
`schema_nested_validate`, `sumtype_typed_payloads`,
`phase6_pure_validate`, `api_program_calc`, `prompt_budget` (no `await`),
`selective_import`, `export_type_schema`, `async_all_example`,
plus inline interpolation, `validate` returns a dict, `str.repeat` /
`math.floor` integer-sink, match guards, `grounded.wrap` (no GraphMemory),
`cap.*` mint/is/forge and abs-path read/write, `result`/`option`,
primary constructors, tagged catch, `?.`, destructuring, `parseJSON` fields,
and `io.getEnvOr` missing-env.

**v1 `n/a` (compile-only smoke):** LLM-awaiting prompts,
`agent_governance_golden`, workflow/job Examples, `grounded_ask`
(GraphMemory score drift), `capability_tokens` Example (relative cwd file I/O;
inline abs-path cap fixtures cover the contract).

**Done when:** filtered tests
(`FullyQualifiedName~InterpretTranspilePairTests`) are in the CI smoke
filter on Windows, Linux, and macOS; Unix runs the published apphost
(no Windows-only skip).

---

## DT8 — Ship contract (post-DT7)

DT7 is a curated allowlist. A file that *claims* to ship still needed an
oracle, not only `compile --mode transpile`.

| Concrete work | Done when |
|---------------|-----------|
| Pair helper compares interpret exit vs transpile exit | Mixed success/failure fails; both-nonzero is error identity (`AssertSameFailureFromSource`) |
| Registry + guard | [`docs/spec/ship-contract.md`](spec/ship-contract.md) lists smoke/pair paths, `malda new` templates, README showcases; `ShipContractGuardTests` in CI |
| Workflow journal in smoke | `WorkflowTranspilerParityTests` in the CI filter |
| Known-divergence fixtures | Nested `result.map`, dict `append`+`length`, `--typed-transpile-level 2`, `error()` failure token |

Not in this slice: HTTP request traces (`Templates/webapi` stays `n/a` /
`http-trace pending`), Second Brain ASK wrapper, Tier 0 C# on every PR.

---

## Explicitly deferred

- New top-level builtins / flat global aliases
- Full static type system with runtime enforcement of all hints
- L1c tagged schema unions (gate in the language-constructs plan)
- Distributed Temporal-equivalent cluster
- Web IDE feature parity with Desktop
- Public package registry
- Actor supervision trees
- Removing flat aliases (next spec **MAJOR**). `fn` / `def` already removed.

---

## Success metrics

| Metric | Target |
|--------|--------|
| Compile | `malda compile --mode transpile` fails on hint mismatch (default) with documented `--lenient-types` |
| Smoke | README “shippable” Examples are in `TranspileSmokeTests` or marked `n/a` |
| Pairs | Curated offline Examples + gotcha fixtures match interpret vs C# transpile stdout (`InterpretTranspilePairTests`) |
| Gotchas | Plain-string interpolation and `parseJson` are no longer total silences |
| Version | Toolchain **1.0.0** tagged after DT2-B + DT3 |
| Use | Announcement: “I run a Second Brain instance that others use.” |

---

## Working agreements

1. Verify against code, [`ReferenceManual/`](../ReferenceManual/), and
   [`docs/spec/`](spec/) — not against old `docs/planning/` status lines.
2. Prefer filtered `dotnet test MaldaLang.Tests --filter "…"`.
3. Do not hand-edit `GeneratedProgram.cs` or generated `.js`.
4. Use `function` (`fn` / `def` are syntax errors). Prompt params stay name-only.
5. Desktop IDE = reference; Web IDE = playground.
6. Dual `MIT OR Apache-2.0`.

---

## Related documents

| Doc | Role |
|-----|------|
| [`docs/architecture.md`](architecture.md) | Engine map; Docs layout links here |
| [`docs/roadmap-p0-maturity.md`](roadmap-p0-maturity.md) | Completed P0 trust/tooling |
| [`docs/roadmap-language-constructs.md`](roadmap-language-constructs.md) | Post-Final language constructs |
| [`docs/spec/CHANGELOG.md`](spec/CHANGELOG.md) | Spec semver + tracking rows |
| [`docs/llm/malda-gotchas.md`](llm/malda-gotchas.md) | Silent failures to shrink |
| [`docs/native-numeric-rollout.md`](native-numeric-rollout.md) | `--typed-transpile-level` (not this plan) |
| [`docs/announcement.md`](announcement.md) | Public weaknesses list |

---

## Revision history

| Date | Change |
|------|--------|
| 2026-08-15 | Initial trust roadmap (DT0–DT6) |
| 2026-08-15 | DT0–DT4 landed: roadmap + links; CI `first_look`; compile `--strict-types` / transpile default + `--lenient-types`; smoke + interpret/transpile pair; `malda-interp` + `parseJson`/`parseJSON` errors |
| 2026-08-15 | DT5 announcement: Second Brain instance that others use (no workplace detail) |
| 2026-08-16 | DT6 landed: toolchain 1.0.0 (same `<Version>`, `docs/releases/v1.0.0.md`, tag `v1.0.0`) |
| 2026-08-17 | DT7 landed: `InterpretTranspilePairTests` (curated Examples + gotcha fixtures; CI smoke filter) |
| 2026-08-21 | DT7 corpus widened: existing nested-schema / typed-payload Examples + inline gotcha pairs (`grounded.wrap`, `cap.*`, `result`/`option`, primary constructors, tagged catch, `?.`, destructuring, `parseJSON` fields, `io.getEnvOr`). No new showcase Examples. |
| 2026-09-05 | DT8 landed: pair exit identity, `docs/spec/ship-contract.md` + `ShipContractGuardTests`, workflow journal in CI smoke, nested `result.map` / list-append / typed-transpile-level 2 / `error()` fixtures |
