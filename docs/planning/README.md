# Planning notes (historical)

**P0 maturity roadmap (complete):** [`docs/roadmap-p0-maturity.md`](../roadmap-p0-maturity.md).
Next work: post-Final gaps in [`docs/spec/CHANGELOG.md`](../spec/CHANGELOG.md), the deferred
list in that roadmap, and the language-construct plan
[`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md) and the trust plan
[`docs/roadmap-trust.md`](../roadmap-trust.md). Prefer those over
status lines below.

**These files are roadmap and sprint notes, not the source of truth for current behavior.**

Before trusting anything here:

1. Verify against the C# implementation under `MaldaLang/` and `MaldaLang.Compiler/`
2. Prefer [`ReferenceManual/`](../../ReferenceManual/) for user-facing language docs
3. Prefer [`AGENTS.md`](../../AGENTS.md) and [`docs/architecture.md`](../architecture.md) for engine maps
4. Prefer [`docs/spec/`](../spec/) for the draft language spec and capability matrices
5. Prefer [`docs/llm/`](../llm/) when writing or reviewing `.malda` programs

Completed phase summaries may still name deferred work that has since shipped or been abandoned.
Treat status lines as historical unless you confirm them in code.

**Keep:** inventory files (`core-builtin-inventory.txt`, `optional-pack-builtin-inventory.txt`),
`parser-manual-drift-audit.md`, and notes still linked from `docs/spec/` or the purity roadmap.
One-off sprint/proposal notes with no inbound links were removed.
