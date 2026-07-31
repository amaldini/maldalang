# Malda language specifications

Formal, versioned contracts for the Malda language. Implementation precedence is documented in each spec.

| Document | Status | Scope |
|----------|--------|--------|
| [malda-language-1.0.md](malda-language-1.0.md) | **Draft 1.0** (2026-06-04) | Tier 0 core semantics |
| [ReferenceManual/22-grammar.html](../../ReferenceManual/22-grammar.html) | Phase 2.2 | BNF-style syntax (parser-aligned) |
| [CHANGELOG.md](CHANGELOG.md) | **Active** (2026-06-04) | Semver policy, deprecation, release notes |

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
