#!/usr/bin/env bash
set -Eeuo pipefail

REMOTE="${AEDA_FACTORY_REMOTE:-https://github.com/YavarGd/AEDA.git}"
ROOT="${AEDA_FACTORY_ROOT:-/home/ubuntu/factory/aeda}"
REPO="$ROOT/repo"
WORKTREES="$ROOT/worktrees"

mkdir -p "$ROOT" "$WORKTREES"

if [[ ! -d "$REPO/.git" ]]; then
    git clone --origin origin --branch main "$REMOTE" "$REPO"
else
    git -C "$REPO" remote set-url origin "$REMOTE"
fi

git -C "$REPO" fetch origin --prune
git -C "$REPO" checkout main

git -C "$REPO" diff --quiet
git -C "$REPO" diff --cached --quiet

git -C "$REPO" merge --ff-only origin/main

git -C "$REPO" config user.name "The Factory"
git -C "$REPO" config user.email "71191271+YavarGd@users.noreply.github.com"
git -C "$REPO" config pull.ff only
git -C "$REPO" config fetch.prune true
git -C "$REPO" config rerere.enabled true
git -C "$REPO" config coderabbit.baseBranch main

printf 'FACTORY_AEDA_REPO=%s\n' "$REPO"
printf 'FACTORY_AEDA_WORKTREES=%s\n' "$WORKTREES"
printf 'FACTORY_AEDA_MAIN=%s\n' "$(git -C "$REPO" rev-parse HEAD)"
