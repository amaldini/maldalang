# Workflow examples

Durable `workflow` / `step` / retry / compensate patterns with local SQLite persistence.

## Run

```bash
malda Examples/Workflows/simple_step.malda
malda Examples/Workflows/determinism_helpers.malda
malda Examples/Workflows/retry_and_inspect.malda
malda Examples/Workflows/runprogram_in_step.malda
```

## Ops report smoke

Seed a FAILED instance, then inspect with the unified CLI report (instance + steps + timeline + DLQ):

```bash
malda Examples/Workflows/ops_report.malda
malda workflow report <instanceId>
malda workflow report <instanceId> --json
malda workflow dlq list --pending-only
```

Use the same working directory for `malda` and `malda workflow …` so both hit `./.malda/workflows.db` (or set an absolute `MALDA_WORKFLOW_CONNECTION`).

See also root README sections on durable workflow CLI operations (`malda workflow list`, …).

## Notes

- Local durable execution (memoized steps on one SQLite file), not a highly available cluster.
- **Single writer** per database; a second process may run **read-only** ops (`list` / `get` / `report` / …) against the same absolute `MALDA_WORKFLOW_CONNECTION`. See [`docs/workflows-ha.md`](../../docs/workflows-ha.md).
- Distinct from the lightweight job queue (`enqueueJob` / `claimJob` in `Examples/Web/job_queue_basic.malda`).
- `runprogram_in_step.malda` is a closed `api` plan (`evalPrompt` offline) executed with `step result = runProgram(prog)`. Produce the JSON before `startWorkflow`; do not `await` in the workflow body.

Catalog: [`metadata.json`](metadata.json).
