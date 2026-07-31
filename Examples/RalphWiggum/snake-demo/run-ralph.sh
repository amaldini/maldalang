#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
SCRIPTS_DIR="$SCRIPT_DIR/../scripts"
PROJECT_REL="Examples/RalphWiggum/snake-demo"

export MALDA_AGENT_RICH="${MALDA_AGENT_RICH:-true}"
export MALDA_AGENT_VERBOSE="${MALDA_AGENT_VERBOSE:-true}"

MALDA_RALPH_WORKDIR="$(bash "$SCRIPTS_DIR/Ensure-RalphWorktree.sh" "$REPO_ROOT" snake-demo "$PROJECT_REL")"
export MALDA_RALPH_WORKDIR
export MALDA_RALPH_WORKTREE="$(dirname "$(dirname "$MALDA_RALPH_WORKDIR")")/ralph-worktrees/snake-demo"
export MALDA_RALPH_PROJECT_REL="$PROJECT_REL"
export MALDA_RALPH_PRD="${MALDA_RALPH_PRD:-PRD.md}"
export MALDA_RALPH_MAX_ITER="${MALDA_RALPH_MAX_ITER:-12}"
export MALDA_RALPH_MODEL="${MALDA_RALPH_MODEL:-deepseek/deepseek-v4-flash}"
export MALDA_RALPH_VALIDATE="${MALDA_RALPH_VALIDATE:-true}"
export MALDA_RALPH_VALIDATE_DEPTH="${MALDA_RALPH_VALIDATE_DEPTH:-recursive}"
export MALDA_RALPH_RESUME="${MALDA_RALPH_RESUME:-true}"
export MALDA_RALPH_RESUME_POLICY="${MALDA_RALPH_RESUME_POLICY:-success-only}"
export MALDA_RALPH_RESET_EACH="${MALDA_RALPH_RESET_EACH:-auto}"
export MALDA_RALPH_PRD_STRICT="${MALDA_RALPH_PRD_STRICT:-true}"
export MALDA_RALPH_PREFLIGHT="${MALDA_RALPH_PREFLIGHT:-strict}"
export MALDA_RALPH_MAX_PHASE_RETRIES="${MALDA_RALPH_MAX_PHASE_RETRIES:-3}"
export MALDA_RALPH_GIT_COMMIT="${MALDA_RALPH_GIT_COMMIT:-true}"
export MALDA_RALPH_REPORT="${MALDA_RALPH_REPORT:-json}"
export MALDA_RUN_COMMAND_POLICY="${MALDA_RUN_COMMAND_POLICY:-whitelist}"

echo "Ralph Wiggum - Snake demo (git worktree)"
echo "Workdir:  $MALDA_RALPH_WORKDIR"
echo "Model:    $MALDA_RALPH_MODEL"
echo

cd "$REPO_ROOT"
dotnet run --project MaldaLang/MaldaLang.csproj -- Examples/RalphWiggum/RalphWiggum.malda
