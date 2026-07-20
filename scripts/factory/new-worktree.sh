#!/usr/bin/env bash
set -Eeuo pipefail

SLUG="${1:-}"
ROOT="${AEDA_FACTORY_ROOT:-/home/ubuntu/factory/aeda}"
REPO="$ROOT/repo"
WORKTREES="$ROOT/worktrees"

if [[ ! "$SLUG" =~ ^[a-z0-9][a-z0-9-]{1,50}$ ]]; then
    echo "Usage: new-worktree.sh <lowercase-task-slug>" >&2
    exit 2
fi

[[ -d "$REPO/.git" ]] || {
    echo "Factory repository is not bootstrapped: $REPO" >&2
    exit 2
}

BRANCH="factory/$SLUG"
PATHNAME="$WORKTREES/$SLUG"

git -C "$REPO" fetch origin --prune

if git -C "$REPO" show-ref --verify --quiet "refs/heads/$BRANCH"; then
    echo "Local branch already exists: $BRANCH" >&2
    exit 2
fi

if git -C "$REPO" show-ref --verify --quiet "refs/remotes/origin/$BRANCH"; then
    echo "Remote branch already exists: origin/$BRANCH" >&2
    exit 2
fi

if [[ -e "$PATHNAME" ]]; then
    echo "Worktree path already exists: $PATHNAME" >&2
    exit 2
fi

mkdir -p "$WORKTREES"
git -C "$REPO" worktree add -b "$BRANCH" "$PATHNAME" origin/main

git -C "$PATHNAME" config coderabbit.baseBranch main

printf 'FACTORY_TASK_BRANCH=%s\n' "$BRANCH"
printf 'FACTORY_TASK_WORKTREE=%s\n' "$PATHNAME"
printf 'FACTORY_TASK_BASE=%s\n' "$(git -C "$PATHNAME" rev-parse origin/main)"
