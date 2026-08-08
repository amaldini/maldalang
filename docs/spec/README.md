# Malda language specifications

Formal, versioned contracts for the Malda language. Implementation precedence is documented in each spec.

| Document | Status | Scope |
|----------|--------|--------|
| [malda-language-1.0.md](malda-language-1.0.md) | **Draft 1.0** (2026-06-04) | Tier 0 core semantics |
| [backend-capability-matrix.md](backend-capability-matrix.md) | **Active** | Interpreter / C# / JS product + property-test capabilities |
| [tier0-backend-matrix.md](tier0-backend-matrix.md) | **Active** | Tier 0 conformance suite thresholds |
| [ReferenceManual/22-grammar.html](../../ReferenceManual/22-grammar.html) | Phase 2.2 | BNF-style syntax (parser-aligned) |
| [CHANGELOG.md](CHANGELOG.md) | **Active** (2026-06-04) | Semver policy, deprecation, release notes |

Toward a 1.0 language release, keep the Draft status until call-site type checking and schema/`validate` are stable; also track literal type warnings (IDE), this capability matrix + guard, a versioned `docs/llm/` pack per release, and macOS CI smoke.

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
