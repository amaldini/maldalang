## Summary

<!-- What changed and why (1–3 bullets). Focus on intent, not a file list. -->

-

## Test plan

<!-- How you verified the change. Prefer filtered tests; do not run the full suite. -->

- [ ] Built / smoke-tested the affected path (interpreter and/or transpile if relevant)
- [ ] If the change claims interpret + C# transpile, added an `InterpretTranspilePairTests` case (or `trace` / `n/a`) in [`docs/spec/ship-contract.md`](../docs/spec/ship-contract.md)
- [ ] Filtered tests for the area touched, e.g. `dotnet test MaldaLang.Tests --filter "FullyQualifiedName~RelevantTests"`
- [ ] Docs / `ReferenceManual/` updated if language or built-in behavior changed
- [ ] No hand-edits to generated artifacts (`GeneratedProgram.cs`, generated `.js` from `.malda`)

## Notes for reviewers

<!-- Optional: risks, follow-ups, or screenshots for IDE/UX changes. -->
