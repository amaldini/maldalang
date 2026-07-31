# Property Testing Contributor Rollout Notes (Sprint 4)

This note captures practical guidelines for writing stable MALDA properties during backend parity rollout.

## Write Stable Properties

- Always use deterministic runs in CI: pass explicit `--seed` and `--iterations`.
- Keep properties pure when possible: avoid file/network/clock side effects unless the property explicitly targets those capabilities.
- Prefer simple invariants over complex multi-assertion blocks; one property should validate one behavior family.
- Use bounded expectations: avoid unbounded loops and timing-sensitive assertions inside properties.

## Seed and Reproducibility

- Every failing property should be rerun with the same seed to confirm reproducibility.
- Include seed and iteration count in bug reports or pull request notes.
- When investigating flakes, keep the seed fixed while reducing iterations to isolate the failing trial quickly.

## Capability and Backend Targeting

- Use `@requires(...)` to declare capability needs (`core`, `actors`, `file-io`, etc.).
- Use `@targets(...)` to explicitly list backend expectations (`interpreter`, `csharp`, `js`).
- JS parity is capability-gated:
  - Unsupported capabilities are `not-applicable` for JS.
  - This should not be treated as a hard parity failure for interpreter/C#.

## Avoid Flaky Patterns

- Do not depend on wall-clock timing inside properties.
- Avoid random behavior from host systems outside MALDA generators.
- Avoid order-dependent assertions when iterating unordered collections.
- If side effects are unavoidable, isolate and reset state per trial.

## Suggested CI Command Pattern

```bash
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~PropertyBehaviorDiffTests"
```

For local checks:

```bash
malda test Examples/Testing/property_core_identity.malda --iterations 100 --seed 1337
```
