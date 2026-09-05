# Malda language specifications

Formal, versioned contracts for the Malda language. Implementation precedence is documented in each spec.

| Document | Status | Scope |
|----------|--------|--------|
| [malda-language-1.0.md](malda-language-1.0.md) | **Final 1.0** (2026-08-12) | Tier 0 core semantics |
| [backend-capability-matrix.md](backend-capability-matrix.md) | **Active** | Interpreter / C# / JS product + property-test capabilities |
| [tier0-backend-matrix.md](tier0-backend-matrix.md) | **Active** | Tier 0 conformance suite thresholds |
| [ReferenceManual/35-grammar.html](../../ReferenceManual/35-grammar.html) | Phase 2.2 | BNF-style syntax (parser-aligned) |
| [CHANGELOG.md](CHANGELOG.md) | **Active** | Semver policy, deprecation, release notes |

**Spec Final 1.0** declared 2026-08-12 (see [`CHANGELOG.md`](CHANGELOG.md) `[1.0.0]`). Toolchain **1.0.15**: [`docs/releases/v1.0.15.md`](../releases/v1.0.15.md). Types implementation: [`docs/roadmap-p0-types-impl.md`](../roadmap-p0-types-impl.md). Broader maturity themes: [`docs/roadmap-p0-maturity.md`](../roadmap-p0-maturity.md). Next language constructs: [`docs/roadmap-language-constructs.md`](../roadmap-language-constructs.md).

## Verification

Tier 0 interpreter checks (spec anchors):

```powershell
.\scripts\run-tier0-conformance.ps1
```

CI (spec guards):

```powershell
.\scripts\verify-spec-guards.ps1
```

Parser/Lexer-only check:

```powershell
.\scripts\verify-spec-parser-drift.ps1
```

Planning context: [malda-language-purity-roadmap.md](../planning/malda-language-purity-roadmap.md).
