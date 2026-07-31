# Ralph Wiggum — PRD-driven autonomous loop

`RalphWiggum.malda` is the reference implementation of a PRD-checklist autonomous agent loop in MALDA. It uses `DevAgent`, **GraphMemory**, post-iteration validation, resume state, and optional git commits.

The name and the pattern come from Geoff Huntley's [Ralph Wiggum technique](https://ghuntley.com/ralph/) — a coding agent run in a loop against a specification, one task per iteration. Huntley used it to build the [`cursed`](https://ghuntley.com/cursed/) language, the demonstration that prompted MALDA itself; this example is the same technique expressed in MALDA rather than in shell.

## Prerequisites

- .NET 8 SDK (or `malda.exe` from a distribution package)
- **OpenRouter** (or configured backend): `OPENROUTER_API_KEY` or `providers.openrouter` in `~/.malda/config.json`
- Optional: **Node.js** for JavaScript syntax validation (`node --check`) in the workdir

## PRD interview (terminal)

Create `PRD.md` before running the development loop:

```bash
export MALDA_RALPH_WORKDIR=/path/to/your/project
export OPENROUTER_API_KEY=your-key
dotnet run --project MaldaLang -- Examples/RalphWiggum/RalphInterview.malda
```

Distribution package: `run_ralph_interview.bat` → `bin\interview\ralph-interview.exe`, `run_ralph.bat` → `bin\loop\ralph.exe` (separate folders so each exe keeps its own `MaldaLang.Executable.dll`).

Flow: **Step 1** terminal questions (profile, vision, structured features, brownfield hint) → **brief preview** → **Step 2** LLM `ask_user` follow-ups → writes `PRD.md` with strict validation + auto-repair → optional smoke loop → run `ralph.exe`.

| Variable | Purpose |
|----------|---------|
| `MALDA_RALPH_LOCALE` | `it` or `en` — prompts and PRD section labels |
| `MALDA_RALPH_INTERVIEW_PROFILE` | `new`, `append`, `features`, or `brief` (skip/reuse Step 1) |
| `MALDA_RALPH_INTERVIEW_MODE` | `new` or `append` when PRD already exists |
| `MALDA_RALPH_BRIEF` | Optional file merged into the interview |
| `MALDA_RALPH_INTERVIEW_OVERWRITE` | Replace existing PRD without prompt |
| `MALDA_RALPH_INTERVIEW_MAX_FOLLOWUPS` | Max `ask_user` questions (default 8) |
| `MALDA_RALPH_INTERVIEW_MAX_LLM_ROUNDS` | Cap LLM tool rounds (default 20 in launcher) |
| `MALDA_RALPH_INTERVIEW_BROWNFIELD` | Include workdir snapshot in LLM prompt |
| `MALDA_RALPH_INTERVIEW_TEMPLATE` | Optional PRD markdown template path |
| `MALDA_RALPH_INTERVIEW_FEWSHOT` | Few-shot checklist excerpt (default: `templates/PRD.fewshot.md`) |
| `MALDA_RALPH_INTERVIEW_SUGGEST_VALIDATE_CMD` | Mention `.ralph-validate.bat` in agent notes |
| `MALDA_RALPH_INTERVIEW_THEN_LOOP` | Run `MALDA_RALPH_LOOP_EXE` for one iteration after success |
| `MALDA_RALPH_LOOP_EXE` | Path to `ralph.exe` (set by distribution launcher) |

Artifacts in the workdir: `.ralph-interview-brief.json` (Step 1 draft), `ralph-interview-summary.md` (post-success summary). Templates: `Examples/RalphWiggum/templates/PRD.template.md`.

Interview uses a **PRD-only DevAgent** (`prdAuthorOnly`): `read_file`, `write_file`, `grep`, `glob`, `list_directory`, `ask_user` — no git, shell, or web search.

`glob()` and `grep()` are transpiler-supported in `ralph.exe` / `ralph-interview.exe` (usable from MALDA helpers like `ralphGlobPaths(workDir, pattern, maxResults)` in `ralph/02-prd.malda`). `glob` excludes `.git`, `node_modules`, `bin`, `obj` by default and caps results (default 200, hard max 500).

## Quick start (Snake demo)

From the repository root:

```bat
Examples\RalphWiggum\snake-demo\run-ralph.bat
```

This creates (or reuses) a **git worktree** at `../ralph-worktrees/snake-demo` on branch `ralph/snake-demo`, so Ralph’s commits do not mix with your main working tree (compiler, docs, etc.). Add that folder as a second bookmark in SourceTree if you use it.

Or manually:

```bash
# Optional: ensure worktree (Windows PowerShell)
powershell -File Examples/RalphWiggum/scripts/Ensure-RalphWorktree.ps1 \
  -RepoRoot . -Name snake-demo -ProjectRelPath Examples/RalphWiggum/snake-demo

export MALDA_RALPH_WORKTREE=../ralph-worktrees/snake-demo
export MALDA_RALPH_PROJECT_REL=Examples/RalphWiggum/snake-demo
export OPENROUTER_API_KEY=your-key
dotnet run --project MaldaLang -- Examples/RalphWiggum/RalphWiggum.malda
```

With a built CLI:

```bash
malda run Examples/RalphWiggum/RalphWiggum.malda
```

(Set `MALDA_RALPH_WORKDIR` to a directory that contains `PRD.md`, or use `MALDA_RALPH_WORKTREE` + `MALDA_RALPH_PROJECT_REL`.)

## How it works

1. Read `PRD.md` in the workdir (checklist: `[ ]`, `[TODO]`, `[DONE]`).
2. Each iteration implements **one** open item.
3. Validate files in the workdir (MALDA parse, HTML/JSON/JS structure, merge markers).
4. Persist `.ralph-state.json` and `.ralph-memory.*` for resume and continuity.
5. Stop when all items are `[DONE]`, validation passes, and the agent signals completion (`TASK_COMPLETE`, `RALPH_DONE`, or auto-complete when PRD is fully done).

### PRD file hints (large projects)

Under the current open checklist item, optional lines narrow validation and guide the agent:

```markdown
- [TODO] [P0] **F2 — API endpoint** (depends: F1)
  - Files: src/Api/OrdersController.cs, src/Api/Program.cs
  - Verify: tests/Orders.Tests.csproj
  - Acceptance: POST /orders returns 201
```

- **`Files:`** — primary paths to edit (shown in the iteration prompt).
- **`Verify:`** — extra paths always checked when `MALDA_RALPH_VALIDATE_SCOPE=changed` (union with `git status` and `MALDA_RALPH_VALIDATE_ALWAYS`).

### Validation hooks and changed files

When `.ralph-validate.bat`, `.ralph-validate.sh`, or `MALDA_RALPH_VALIDATE_CMD` run, Ralph passes:

| Variable | Content |
|----------|---------|
| `RALPH_WORKDIR` | Absolute workdir path |
| `RALPH_CHANGED_FILES` | Comma-separated paths from `git status` (modified/staged/untracked) |
| `RALPH_VALIDATION_FILES` | Full set used for file validation (git ∪ PRD hints ∪ always list) |

Copy `templates/ralph-validate.bat.sample` to `.ralph-validate.bat` in your workdir and tailor `dotnet test` / filters to `RALPH_CHANGED_FILES`.

`runCommand(cmd, args, workDir, envObject)` accepts an optional trailing **object** of extra environment variables for child processes.

### Recommended env for large codebases

```bat
set MALDA_RALPH_VALIDATE_SCOPE=changed
set MALDA_RALPH_VALIDATE_HOOK=on_change
set MALDA_RALPH_VALIDATE_FALLBACK=none
set MALDA_RALPH_VALIDATE_EXCLUDE=wwwroot,bin,obj,node_modules
set MALDA_RALPH_VALIDATE_CMD=dotnet test Your.Tests --filter "FullyQualifiedName~AreaUnderWork"
```

Use a narrow `MALDA_RALPH_PROJECT_REL` / worktree, PRD items with `Files:` / `Verify:`, and `glob` + `grep` for discovery (see agent rules in `ralph/05-loop.malda`).

## Module layout

Implementation lives under `ralph/` (included by `RalphWiggum.malda`). See `ralph/ARCHITECTURE.md` for include order and shared globals.

## Linux / macOS

```bash
Examples/RalphWiggum/scripts/Ensure-RalphWorktree.sh snake-demo Examples/RalphWiggum/snake-demo
Examples/RalphWiggum/snake-demo/run-ralph.sh
```

See `snake-demo/PRD.md` for a completed standalone-game demo.

## Git worktree (recommended)

When Ralph runs inside the same repo where you edit MALDA itself, use a **dedicated worktree** so `git status` and auto-commits stay isolated.

| Step | Command / tool |
|------|----------------|
| Create / reuse worktree | `scripts/Ensure-RalphWorktree.ps1` (Windows) or `scripts/Ensure-RalphWorktree.sh` (Unix) |
| Default location | `<parent-of-repo>/ralph-worktrees/<name>` |
| Default branch | `ralph/<name>` |
| Ralph project path | `MALDA_RALPH_PROJECT_REL` inside the worktree (e.g. `Examples/RalphWiggum/snake-demo`) |
| Merge into main | From main repo: `git merge ralph/<name>` |
| Remove worktree | `scripts/Remove-RalphWorktree.ps1 -RepoRoot . -Name <name>` |
| Dry-run remove | `... -WhatIf` (prints target, does not remove) |

`Remove-RalphWorktree.ps1` refuses to run if the target is the main repository, inside the main repo, outside `ralph-worktrees/`, or not listed in `git worktree list`. Git also blocks removing the main working tree.

SourceTree: add the worktree folder as a **separate local repository** bookmark; create/remove worktrees from the terminal.

## Key environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `MALDA_RALPH_WORKDIR` | *(derived)* | Project directory with `PRD.md` (overrides worktree derivation) |
| `MALDA_RALPH_WORKTREE` | — | Git worktree root checkout |
| `MALDA_RALPH_PROJECT_REL` | `.` | Path from worktree root to project dir |
| `MALDA_RALPH_PRD` | `PRD.md` | PRD path relative to workdir |
| `MALDA_RALPH_MAX_ITER` | `10` | Max iterations |
| `MALDA_RALPH_MODEL` | — | Override LLM model (else `agents.defaults.model` in config, then `providers.openrouter.model`) |
| `MALDA_CONFIG` | — | Path to `config.json` (else `.malda/config.json` from cwd/parents, then `~/.malda/config.json`) |
| `MALDA_AGENT_VERBOSE` | `true` | Live `[llm]` / `[tool]` logging during `agent.think()` |
| `MALDA_RALPH_VERBOSE` | — | Alias for `MALDA_AGENT_VERBOSE` |
| `MALDA_AGENT_RICH` | `true` | Colored agent verbose CLI (auto-off when output is redirected) |
| `MALDA_RALPH_RICH` | — | Alias for `MALDA_AGENT_RICH` |
| `MALDA_RALPH_QUIET` | `false` | Skip startup banner/table |
| `MALDA_RALPH_VERBOSE_CONFIG` | `false` | Print full configuration panel at startup |
| `MALDA_AGENT_TOOL_DETAIL` | `compact` | Tool log detail: `compact` or `full` |
| `MALDA_RALPH_TOOL_DETAIL` | — | Alias for `MALDA_AGENT_TOOL_DETAIL` |
| `MALDA_AGENT_RESPONSE_LINES` | `15` | Max lines in end-of-iteration agent response panel |
| `MALDA_RALPH_RESPONSE_LINES` | — | Alias for `MALDA_AGENT_RESPONSE_LINES` |
| `MALDA_AGENT_LLM_PREVIEW` | `compact` | Inline `[llm]` final preview during `think()`: `compact` or `full` |
| `MALDA_AGENT_LLM_THINKING` | `compact` | Show LLM reasoning/planning as `[think]` during `think()`: `off`, `compact`, or `full` |
| `MALDA_AGENT_LLM_STREAM` | `true` | Use OpenAI-style SSE streaming for HTTP LLM clients; live `[think]` tokens when thinking is enabled |
| `MALDA_AGENT_STATUS_EVERY` | `4` | Repeat enriched status banner every N LLM rounds during `think()` |
| `MALDA_AGENT_MAX_LLM_ROUNDS` | `0` | Max tool-call rounds per `think()` (0 = unlimited). Stops endless re-read/verify loops. |
| `MALDA_RALPH_MAX_LLM_ROUNDS` | — | Alias for `MALDA_AGENT_MAX_LLM_ROUNDS` (sample demo sets `25`) |
| `MALDA_RALPH_STATUS_EVERY` | — | Alias for `MALDA_AGENT_STATUS_EVERY` |
| `MALDA_RALPH_PROJECT_TITLE` | — | Override project name in status header (default: workdir folder) |
| `MALDA_RALPH_LLM_PREVIEW` | — | Alias for `MALDA_AGENT_LLM_PREVIEW` |
| `MALDA_RALPH_LLM_THINKING` | — | Alias for `MALDA_AGENT_LLM_THINKING` |
| `MALDA_RALPH_LLM_STREAM` | — | Alias for `MALDA_AGENT_LLM_STREAM` |
| `MALDA_RALPH_RESUME` | `true` | Skip iterations already in `.ralph-state.json` |
| `MALDA_RALPH_RESUME_POLICY` | `all` | `all` (legacy) or `success-only` (recommended: retry failed iterations) |
| `MALDA_RALPH_VALIDATE` | `true` | Post-iteration file validation |
| `MALDA_RALPH_VALIDATE_ONLY` | `false` | Run validation and exit (no agent loop) |
| `MALDA_RALPH_VALIDATE_DEPTH` | `recursive` | `flat` or `recursive` workdir walk |
| `MALDA_RALPH_VALIDATE_SCOPE` | `all` | `all` (full workdir) or `changed` (git-modified paths only) |
| `MALDA_RALPH_VALIDATE_HOOK` | `always` | When to run `.ralph-validate.*` / `VALIDATE_CMD`: `always`, `on_change`, `never` |
| `MALDA_RALPH_VALIDATE_FALLBACK` | `all` | If `scope=changed` and nothing to scan: `all` (full workdir) or `none` (skip file checks) |
| `MALDA_RALPH_VALIDATE_ALWAYS` | — | Comma-separated paths always unioned into `changed` validation (with git diff + PRD hints); `PRD.md` is added automatically |
| `MALDA_RALPH_VALIDATE_EXCLUDE` | — | Extra comma-separated dirs to skip during validation |
| `MALDA_RALPH_VALIDATE_CMD` | — | Shell command; non-zero exit adds validation errors |
| `MALDA_RALPH_AUTO_FORMAT_JS` | `false` | Auto-run Prettier (via npx) on JS / inline HTML scripts when syntax check fails |
| `MALDA_RALPH_RESET_EACH` | `true` | `true`, `false`, `phase`, or `auto` (trim when context grows; recommended for long runs) |
| `MALDA_RALPH_CONTEXT_BUDGET_TOKENS` | *(derived)* | Max estimated input tokens before auto-trim (overrides ratio) |
| `MALDA_RALPH_CONTEXT_BUDGET_RATIO` | `0.75` | Fraction of context limit used when budget tokens not set |
| `MALDA_RALPH_CONTEXT_LIMIT_TOKENS` | `1048576` | Model context window for budget calculation |
| `MALDA_RALPH_CONTEXT_AUTO_TRIM` | `true` | Set `false` to disable automatic context trimming |
| `MALDA_RALPH_AUTO_REMEMBER` | `false` | Store full prompt/response each `think()` |
| `MALDA_RALPH_MEMORY_MAINTAIN` | on when `MAX_ITER>5` | After each iteration: `consolidate`/`reflect`, `prune`, `enforceLimits` |
| `MALDA_RALPH_MEMORY_REFLECT` | `false` | Use `reflect()` instead of `consolidate()` during maintenance (needs LLM client) |
| `MALDA_RALPH_MEMORY_REFLECT_EVERY` | `1` | Run reflect every N maintenance cycles |
| `MALDA_RALPH_MEMORY_REFLECT_MIN_EPISODIC` | — | Override min episodic count for reflect |
| `MALDA_RALPH_MEMORY_PRUNE_DAYS` | `30` | Prune consolidated episodics older than N days |
| `MALDA_RALPH_MEMORY_MAX_NODES` | `5000` | Episodic node cap via `enforceLimits()` |
| `MALDA_RALPH_MEMORY_SCOPE` | `ralph:{project}` | Memory scope for agent queries and progress tools |
| `MALDA_RALPH_MEMORY_RESET` | `false` | `forgetByScope` before load (fresh memory for same workdir) |
| `MALDA_RALPH_MEMORY_SEED_INTERVIEW` | `true` | Seed semantic facts from `.ralph-interview-brief.json` at loop start |
| `MALDA_RALPH_MEMORY_PHASE_QUERY` | `false` | Phase-scoped `[GraphMemory context]` in prompt (off by default — `agent.think()` already injects memory) |
| `MALDA_RALPH_MEMORY_RERANK_MIN_NODES` | `200` | Enable LLM rerank in phase query when node count exceeds threshold |
| `MALDA_RALPH_MEMORY_INDEX_DIR` | — | Optional extra directory for `reindexDocuments` (e.g. docs folder) |
| `MALDA_RALPH_MEMORY_INDEX_PATTERN` | `**/*.md` | Glob pattern when `MEMORY_INDEX_DIR` is set |
| `MALDA_MEMORY_EMBED` | `hash` | Embedding mode: `hash`, `bow`, or `llama` (see assistant config for model paths) |
| `MALDA_RALPH_PRD_STRICT` | `true` | Fail iteration if PRD checklist unchanged after successful validation |
| `MALDA_RALPH_REQUIRE_SIGNAL` | `false` | Require explicit `TASK_COMPLETE` / `RALPH_DONE` even when PRD is complete |
| `MALDA_RALPH_PLAN_ONLY` | `false` | Analyze PRD and write `ralph-plan.md` without modifying project files |
| `MALDA_RALPH_PREFLIGHT` | `strict` | `strict`, `warn`, or `off` — checks before loop starts |
| `MALDA_RALPH_REPORT` | `json` | `json`, `html`, `both`, or `off` — write `ralph-run-report.*` in workdir |
| `MALDA_RALPH_NOTIFY` | — | `desktop`, `webhook:<url>`, or CSV combo |
| `MALDA_RALPH_MAX_PHASE_RETRIES` | `3` | Stall detection threshold (same phase + same PRD progress) |
| `MALDA_RALPH_ABORT_ON_STALL` | `false` | Exit loop when stall threshold exceeded |
| `MALDA_RALPH_GIT_COMMIT` | `false` | Auto-commit modified files after successful validation |
| `MALDA_RALPH_COMMIT_MESSAGE_TEMPLATE` | — | Optional git commit message template |
| `MALDA_AGENT_THINK_TIMEOUT_MS` | — | Max duration for one `agent.think()` (alias: `MALDA_RALPH_ITER_TIMEOUT_MS`) |

Full list is documented in the header comment of `RalphWiggum.malda` and `ralph/ARCHITECTURE.md`.

## CLI output

With `MALDA_AGENT_RICH=true` (default), Ralph uses Spectre.Console for a compact startup table, enriched iteration headers (project · phase · PRD progress · iter), and semantic success/error lines. During `agent.think()`, the same status banner repeats every **4 LLM rounds** (configurable via `MALDA_AGENT_STATUS_EVERY`). Each LLM round can show a live `[think]` line streamed token-by-token when `MALDA_AGENT_LLM_STREAM=true` (default) and thinking is enabled (`MALDA_AGENT_LLM_THINKING=compact` or `full`). Agent tool calls use **compact** mode by default. Set `MALDA_AGENT_TOOL_DETAIL=full` for the legacy verbose tool log. Ralph-prefixed env vars (`MALDA_RALPH_*`) remain supported as aliases for the agent settings above.

## Local artifacts (gitignored)

Created in the **workdir**, not next to `RalphWiggum.malda`:

- `.ralph-state.json`
- `.ralph-memory.graph.json`, `.ralph-memory.metadata.json`, `.ralph-memory.vectordb.bin`
- `ralph-run-report.json` / `ralph-run-report.html` (when `MALDA_RALPH_REPORT` is enabled; includes `memory` stats when GraphMemory is active)

### GraphMemory CLI (inspect Ralph memory)

From the workdir (or pass `--path`):

```bat
malda memory stats --path .ralph-memory
malda memory reindex --path .ralph-memory --dir . --pattern PRD.md
malda memory prune --path .ralph-memory --type episodic --older-than-days 30
malda memory reflect --path .ralph-memory --dry-run
malda memory export-bundle --path .ralph-memory -o memory-bundle.json
```

## Distribution package

Build a downloadable CLI zip from this repository (no git clone required for end users):

```bash
build_malda_distribution.bat
# → artifacts/dist/malda-<version>-win-x64.zip
```

Or download a pre-built archive from
[GitHub Releases](https://github.com/amaldini/maldalang/releases).

From the unzipped folder, run Ralph with the interpreter:

```bash
malda run Examples/RalphWiggum/RalphWiggum.malda
```

> Ralph can be compiled with `--mode transpile` (embedding helpers and `LlamaEmbedder`
> string coercion are supported). Prefer the interpreter while iterating; use transpile
> when you need a standalone `ralph.exe`.

## Reference

- Reference Manual §16.18 (Ralph Wiggum) — [`ReferenceManual/14-agent-orchestration.html`](../../ReferenceManual/14-agent-orchestration.html)
