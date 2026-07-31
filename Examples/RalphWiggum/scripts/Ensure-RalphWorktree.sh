#!/usr/bin/env bash
set -euo pipefail

# Ensures a git worktree exists for Ralph Wiggum and prints the project workdir (one line, stdout).
# Usage (from repo root):
#   ./Examples/RalphWiggum/scripts/Ensure-RalphWorktree.sh . snake-demo Examples/RalphWiggum/snake-demo

REPO_ROOT="$(cd "${1:-.}" && pwd)"
NAME="${2:?worktree name required}"
PROJECT_REL="${3:-.}"
BRANCH="${4:-ralph/$NAME}"
WORKTREES_PARENT="$(dirname "$REPO_ROOT")/ralph-worktrees"
WORKTREE_PATH="$WORKTREES_PARENT/$NAME"
PROJECT_REL="${PROJECT_REL#/}"
RALPH_WORKDIR="$WORKTREE_PATH/$PROJECT_REL"

if ! git -C "$REPO_ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Not a git repository: $REPO_ROOT" >&2
  exit 1
fi

mkdir -p "$(dirname "$WORKTREE_PATH")"

if ! git -C "$REPO_ROOT" worktree list --porcelain | grep -Fq "worktree $WORKTREE_PATH"; then
  if git -C "$REPO_ROOT" show-ref --verify --quiet "refs/heads/$BRANCH"; then
    git -C "$REPO_ROOT" worktree add "$WORKTREE_PATH" "$BRANCH"
  else
    git -C "$REPO_ROOT" worktree add -b "$BRANCH" "$WORKTREE_PATH"
  fi
fi

if [[ ! -d "$RALPH_WORKDIR" ]]; then
  echo "Ralph project path not found: $RALPH_WORKDIR" >&2
  exit 1
fi

if [[ ! -f "$RALPH_WORKDIR/PRD.md" ]]; then
  echo "PRD.md not found in: $RALPH_WORKDIR" >&2
  exit 1
fi

echo "$RALPH_WORKDIR"
