# Durable Workflows Test Matrix Skeleton

Status: Sprint 0 baseline matrix  
Scope: Unit, integration, CLI, transpiler parity, and reliability slices for durable workflows.

## 1) Matrix Overview

| Area | Layer | Goal | Status |
|---|---|---|---|
| Parser/AST | Unit | Validate workflow syntax and diagnostics | Active |
| Runtime engine | Unit/Integration | Lifecycle, journaling, replay, retries, waits | Active |
| CLI | Integration | Command behavior, output formats, exit codes | Active |
| Transpiler parity | E2E | Interpreter vs transpiled behavioral parity | Active |
| Language server | Unit/Integration | Workflow symbols, hover, diagnostics | Active |
| Reliability | Integration | Restart/fault/requeue/retention safety | Active |

## 2) Core Scenario Buckets

### A. Parser and diagnostics

- Workflow declaration parsing
- Step/approval/awaitSignal parsing
- Options parsing (`retry`, `backoff`, `delay`, `maxDelay`, `timeout`, `compensate`)
- Diagnostics:
  - `WF1003` duplicate step id
  - `WF1004` invalid retry/backoff combos

### B. Deterministic runtime and persistence

- Start/get/steps lifecycle
- Replay contract: no duplicate successful step execution
- Runtime deterministic boundaries:
  - `WF1001`
  - `WF1002`
- SQLite persistence schema and query behaviors

### C. Retry/timeout and recovery

- Backoff formulas (fixed/linear/exponential + cap/jitter)
- Timeout -> retry -> success
- Fail after max attempts
- Startup recovery of stale running work

### D. Human-in-the-loop and compensation

- Approval wait -> approve/reject/timeout transitions
- Await-signal wait -> signal/timeout transitions
- Compensation reverse-order execution
- Compensation success vs partial-failure terminal outcomes

### E. Transpiler parity

- Same source workflow behavior in interpreter and transpiled mode
- Parity checks:
  - workflow status
  - step states/attempts
  - emitted event shapes (metadata included)

### F. CLI hardening and automation

- `workflow` command family behavior and transition checks
- Human output mode stability
- JSON output mode stability
- Exit code contract for success/not-found/invalid-transition/validation

### G. Observability and governance

- Event taxonomy coverage and timeline reconstruction
- Metrics snapshots consistency
- Correlation metadata propagation (`correlationId`, `workflowInstanceId`, `stepName`, `attempt`)
- Guardrail enforcement (enabled flag, retry/payload/runtime limits)

### H. Provider abstraction and operations

- Storage provider contract compliance (default SQLite)
- DLQ creation/list/requeue lifecycle
- Retention/archival/compaction maintenance safety
- Bounded restart/fault-injection reliability scenarios

## 3) Exit Criteria Mapping (Plan-Level)

| Sprint | Required Evidence Type |
|---|---|
| 1 | Parser/AST unit tests + diagnostics coverage |
| 2 | Runtime + crash/replay persistence tests |
| 3 | Retry/timeout/recovery tests + lifecycle metrics/events |
| 4 | Approval/signal/compensation integration tests + CLI control tests |
| 5 | Interpreter vs transpiler parity e2e + CLI hardening tests |
| 6 | Observability/LSP/governance tests |
| 7 | Provider seam + DLQ/requeue + retention/reliability tests + docs evidence |

## 4) Evidence Collection Template

For each sprint closure review, record:

- Implemented features (file-level pointers)
- Targeted test commands run
- Pass/fail counts
- Residual risks
- Follow-up backlog items

## 5) Operational Notes

- Never run full repository suite for sprint validation; use focused test filters.
- Prefer reproducible reliability tests (fixed seeds, bounded runtime windows).
- Keep parity assertions behavior-oriented (state/events), not formatting fragile.

