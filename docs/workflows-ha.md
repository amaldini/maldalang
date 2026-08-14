# Durable workflows — HA / multi-worker model (v1)

**Status:** Active (W2)  
**Audience:** operators and maintainers deploying `workflow` persistence  
**Related:** [`ReferenceManual/21-durable-workflows.html`](../ReferenceManual/21-durable-workflows.html) §32.10, [`roadmap-p0-maturity.md`](roadmap-p0-maturity.md)

This note documents the **supported** concurrency model for the OSS workflow store. It is intentionally narrower than Temporal-class clusters (deferred on the P0 roadmap).

---

## Deployment model v1

| Role | Allowed | Commands / API |
|------|---------|----------------|
| **Writer** (one process) | Yes | Interpreting/transpiled programs that run steps; `malda workflow start`, `retry`, `resume`, `cancel`, `approve`, `signal`, `dlq requeue`, `maintenance run` |
| **Ops reader** (optional second process) | Yes | `malda workflow list`, `get`, `steps`, `events`, `report`, `metrics`, `dlq list` against the **same** database |

```text
Writer process  ──read/write──►  SQLite (WAL)
Ops process     ──read-only───►  SQLite (WAL)
```

Rules:

1. Run **at most one writer** against a given database file.
2. Point every process at the same absolute `MALDA_WORKFLOW_CONNECTION` if working directories differ. The default `Data Source=./.malda/workflows.db` is relative to CWD and silently opens a different empty DB when CWD changes.
3. Ops processes must not mutate state. Mutating CLI from a second process is **two writers** and is unsupported.

On open, the runtime sets `PRAGMA journal_mode=WAL` and `PRAGMA busy_timeout=5000` so a read-only ops process can inspect the DB while the writer is active without immediate `SQLITE_BUSY` failures under brief contention.

---

## Lease / locking today

| Mechanism | What it is | What it is not |
|-----------|------------|----------------|
| Process singleton `WorkflowEngine.Instance` | One engine per process; in-process lock around provider init | Cross-process coordination |
| SQLite file lock + WAL | Serializes writers at the file level; readers coexist under WAL | Distributed worker claim |
| Stale `RUNNING` recovery (`RecoverStaleRunningState`, default 120s) | After crash/restart, aged `RUNNING` steps are timed out and journaled | A worker ownership lease / heartbeat |
| DLQ requeue (`WHERE requeued_at_utc IS NULL`) | Optimistic single requeue of a dead letter | Instance-level compare-and-swap |

There is **no** worker id, claim column, or schedule-to-start lease. Do not treat “stale RUNNING lease” as Temporal activity-lease semantics.

---

## Failure modes

| Scenario | Outcome |
|----------|---------|
| Writer process crashes mid-step | On next open, stale `RUNNING` steps are recovered (timeout + event). In-flight side effects outside the DB are not rolled back. |
| Machine or DB file lost | All durable state for that file is lost. No multi-machine replica. |
| Two writers on one DB | Unsupported: duplicate step execution, lost updates, or `SQLITE_BUSY` under load. The engine does not claim instances. |
| Different CWDs with relative DB path | Split-brain: each process sees its own empty `./.malda/workflows.db`. Fix with an absolute connection string. |
| Ops reader during writer load | Supported under WAL; may briefly wait up to `busy_timeout` (5s) under contention. |

---

## SQLite limits

- **One logical writer** for correctness of workflow semantics (even though SQLite can serialize some concurrent writes).
- **File locality** — durability is “this box / this volume,” not a cluster.
- **No built-in HA** — no automatic failover, no shared quorum.
- **Provider surface** — only `MALDA_WORKFLOW_PROVIDER=sqlite` is implemented; other names fail fast.

---

## What is not Temporal (yet)

Malda durable workflows do **not** provide:

- Multi-worker task queues or poll/claim
- Cluster membership / history server
- Full history non-determinism detection comparable to Temporal’s replay tooling (L4 walks same-file helpers for the deny-list; it is not HA and not history comparison)
- Multi-machine failover or geo replication
- Schedule-to-start / worker heartbeats as ownership leases

Local durability, retries, DLQ, ops report, and restart recovery remain the product bar for v1.

---

## Migration story

Storage goes through `IWorkflowStorageProvider` (`WorkflowStorage.cs` / `WorkflowPersistence.cs`). A future backend (for example Postgres) would need, at minimum:

1. **Claim / lease columns** (or equivalent) so multiple writers can safely take work
2. **Conditional status transitions** (CAS) on instance and step rows
3. Documented connection pooling and timeout policy replacing SQLite WAL assumptions

Until that ships, stay on **single-writer SQLite** with optional read-only ops. Do not run two mutating processes against one file and expect Temporal-like safety.
