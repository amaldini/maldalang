# Workflow examples

Durable `workflow` / `step` / retry / compensate patterns with local SQLite persistence.

## Run

```bash
malda Examples/Workflows/simple_step.malda
```

See also root README sections on durable workflow CLI operations (`malda workflow list`, …).

## Notes

- Local durable execution (memoized steps on one SQLite file), not a highly available cluster.
- Distinct from the lightweight job queue (`enqueueJob` / `claimJob` in `Examples/Web/job_queue_basic.malda`).

Catalog: [`metadata.json`](metadata.json).
