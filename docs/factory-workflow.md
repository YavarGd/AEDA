# Factory development workflow

This document defines how The Factory changes AEDA.

## Core rules

1. `main` is never used as a worker workspace.
2. Each objective gets its own branch named `factory/<task-slug>`.
3. Each objective gets its own Git worktree on the Factory VPS.
4. Production and QA operate on the same task worktree, but QA does not modify product files.
5. Factory runtime artifacts belong under `.factory/` and are not committed.
6. Product changes are committed only after deterministic validation passes.
7. A draft pull request is opened before a task is considered ready for integration.
8. Windows CI must restore, test, and build AEDA successfully before merge.
9. A failed, incomplete, rate-limited, or unauthenticated external review is not a clean QA result.
10. Merging into `main` requires Yavar's approval until an explicit autonomous-merge policy is adopted.

## Task lifecycle

1. Hermes CEO receives an objective.
2. The Factory creates `factory/<task-slug>` from the latest `origin/main` in a dedicated worktree.
3. `factory-production` delegates implementation to MiMo Code.
4. Production runs applicable Linux-compatible checks and records its MiMo log.
5. `factory-qa` inspects tracked and untracked files, runs deterministic checks, and invokes the configured external reviewer once.
6. Failed QA creates a repair task in the same worktree, followed by another independent QA pass.
7. After local QA is acceptable, the branch is pushed and a draft pull request is opened.
8. GitHub Actions performs the authoritative Windows restore, test, Debug build, and Release build.
9. The CEO reports the task IDs, changed files, test/build evidence, QA status, CI status, and remaining risks.
10. Yavar approves or rejects the merge.

## Verdict semantics

- `PASS`: required deterministic checks and required external review completed successfully.
- `PASS_WITH_NOTES`: only explicitly documented non-blocking findings remain.
- `PASS_WITH_EXTERNAL_QA_BLOCKED`: deterministic checks passed but external review did not complete.
- `BLOCKED_EXTERNAL_QA`: external review is mandatory for the task and did not complete.
- `FAIL`: a confirmed defect, failed test, failed build, or unmet acceptance criterion remains.

## Windows validation

The Linux VPS may edit and inspect the repository, but GitHub Actions on a Windows runner is the authoritative build environment for the WinUI solution. The required commands are:

```powershell
dotnet restore PersonalAI.slnx
dotnet test PersonalAI.Tests/PersonalAI.Tests.csproj --configuration Release --no-restore
dotnet build PersonalAI.slnx --configuration Debug --no-restore
dotnet build PersonalAI.slnx --configuration Release --no-restore
```
