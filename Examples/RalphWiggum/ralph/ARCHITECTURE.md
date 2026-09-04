# Ralph Wiggum module layout

Entry point: [`../RalphWiggum.malda`](../RalphWiggum.malda)

## Include order (required)

1. `00-env.malda` — environment helpers, `createAgentClient`, path resolution
2. `01-cli.malda` — Spectre.Console UI, logging, turn summaries
3. `02-prd.malda` — PRD parsing, phases, completion signals
4. `03-validation.malda` — structural validation, scoped file sets, custom hooks (`RALPH_*` env for subprocesses). `.malda` files go through `checkMalda` (same diagnostics as `malda check --json`); warnings/info do not fail the iteration.
5. `04-state-memory.malda` — `.ralph-state.json`, GraphMemory setup/maintenance (`setupRalphGraphMemory`, `maintainRalphMemory`, PRD reindex, interview seed), git helpers
6. `05-loop.malda` — prompts, preflight, `autonomousAgentLoop`, plan-only
7. `06-report.malda` — `ralph-run-report.json` / `.html`
8. `07-notify.malda` — desktop / webhook notifications
9. `08-interview.malda` — terminal PRD interview (`RalphInterview.malda` only)

After includes, the entry file sets `var config = getMaldaConfig();` and runs bootstrap.

## Interview entry

[`../RalphInterview.malda`](../RalphInterview.malda) includes `00-env`, `01-cli`, `02-prd`, and `08-interview`. Distribution build compiles it to `ralph-interview.exe`.

## Shared globals

- `config` — from `getMaldaConfig()`, used by `createAgentClient()`

## Workdir artifacts

- `.ralph-state.json`, `.ralph-memory.*`, `ralph-run-report.json`, `ralph-plan.md` (plan-only)
- `.ralph-validate.bat` / `.ralph-validate.sh` (optional; see `templates/ralph-validate.bat.sample`)

## Targeted validation (large projects)

Documented in [`../README.md`](../README.md). Summary:

| Mechanism | Role |
|-----------|------|
| `MALDA_RALPH_VALIDATE_SCOPE=changed` | Validate git-modified paths (+ union below), not the whole workdir |
| `MALDA_RALPH_VALIDATE_HOOK=on_change` | Run `VALIDATE_CMD` / `.ralph-validate.*` only when git reports changes |
| `MALDA_RALPH_VALIDATE_FALLBACK` | `all` or `none` when `changed` has no paths |
| `MALDA_RALPH_VALIDATE_ALWAYS` | Extra comma-separated paths always included |
| PRD `Files:` / `Verify:` lines | Per-item hints (prompt + validation union); parsed in `02-prd.malda` |
| `mergeValidationRelPaths` in `03-validation.malda` | Builds the path list each iteration |
| Hook env vars | `RALPH_WORKDIR`, `RALPH_CHANGED_FILES`, `RALPH_VALIDATION_FILES` passed to hook subprocesses |

`05-loop.malda` calls `collectModifiedFiles` (in `04-state-memory.malda`), merges PRD hints, then `validateWorkdirWithContext`.
