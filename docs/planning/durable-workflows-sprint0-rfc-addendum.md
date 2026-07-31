# Durable Workflows Sprint 0 RFC Addendum

Status: Approved  
Scope: Durable Workflows implementation baseline and invariants for Sprints 1-7  
Source references:
- `workflowProposal.md`
- `.cursor/plans/durable_workflows_sprints_1be4b186.plan.md`

## 1) Decision Freeze

This addendum captures the Sprint 0 decisions required to avoid rework and to keep interpreter/transpiler/runtime behavior aligned.

- **Approval syntax model**
  - Decision: keep `approval` as a distinct workflow AST node and runtime concept.
  - Rationale: explicit wait semantics and clearer transition validation.

- **Parallel step groups**
  - Decision: out of scope for v1 (no parallel step groups).
  - Rationale: prioritize deterministic replay, persistence correctness, and recovery semantics.

- **Idempotency declaration**
  - Decision: explicit idempotency declaration optional in v1.
  - Enforcement: warn-oriented policy, with deterministic replay + persisted journal as primary guard.

- **Compensation policy**
  - Decision: compensation is recommended broadly and mandatory only for policy-tagged critical steps.
  - Enforcement: diagnostics and policy-driven checks; runtime supports compensation lifecycle and outcomes.

## 2) Canonical State Model

### Workflow lifecycle states

- `PENDING`
- `RUNNING`
- `WAITING_APPROVAL`
- `WAITING_SIGNAL`
- `COMPENSATING`
- `COMPLETED`
- `FAILED`
- `CANCELLED`
- `COMPENSATED`

### Step lifecycle states

- `PENDING`
- `RUNNING`
- `SUCCEEDED`
- `FAILED`
- `TIMED_OUT`
- `SKIPPED`
- `COMPENSATING`
- `COMPENSATED`
- `COMPENSATION_FAILED`

## 3) Transition Invariants

- No duplicate successful re-execution for a replayed step with an existing successful journal entry.
- Terminal workflow states are immutable except through explicitly legal recovery/requeue flows.
- Retry attempt progression is monotonic and bounded by configured maximums.
- Timeout and retry transitions must be journaled before subsequent attempts.
- External controls (`approve`, `signal`, `requeue`) must enforce legal transition checks.
- Compensation execution order is reverse of successfully completed compensable steps.
- Compensation terminal outcomes:
  - all compensation succeeded -> `COMPENSATED`
  - any compensation failure -> `FAILED` with compensation diagnostics

## 4) Error/Diagnostic Taxonomy Baseline

- `WF1001`: non-deterministic operation in workflow body
- `WF1002`: side-effecting operation outside legal workflow step boundaries
- `WF1003`: duplicate step identifier
- `WF1004`: invalid retry/backoff option combination
- `WF1006`: illegal transition/state operation

## 5) Persistence and Recovery Boundaries

- Durable stores must track:
  - workflow instances
  - step attempts/states
  - events for timeline reconstruction
  - dead letters and requeue audit metadata
- Startup/restart recovery must reconcile stale running work without violating replay contract.
- Storage provider is abstracted behind runtime contracts; SQLite remains default provider.

## 6) Acceptance Baseline for Plan Closure

Plan closure for durable workflows requires:

- parser/runtime/transpiler/CLI slices implemented and covered by targeted tests,
- observability/metadata fields present for timeline and automation use,
- DLQ/requeue + retention/maintenance operations available with guardrails,
- documented migration/runbook guidance in source docs.

## 7) Sign-off

This file serves as the Sprint 0 mini-RFC addendum and decision freeze artifact for the durable workflows plan.
