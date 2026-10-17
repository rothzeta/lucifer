# Assignment INT-3/integrate (role: implementer; integration)
Task id: INT-3. Workspace: this worktree, branch ruach/luc-06-impl (LUC-06 candidate 6ddc828 + report 0d0a921).

Outcome: combine LUC-10 into this branch; combined revision builds and all tests pass.
Steps: `git merge --no-ff ruach/luc-10-impl` (LUC-10 candidate eb94586, report babac7c; LUC-10 moved the Player.cs loop to PlaySession.cs). Resolve conflicts within the two tasks' scope, keeping both behaviours (LUC-06 outcome mapping/backoff glue in PlayerAlbumApplier etc.; LUC-10 lifecycle/supervision). Keep all tests from both branches (230 and 215 incl. 186 shared); never weaken assertions; adjust only integration glue.
Run `dotnet build` and `dotnet test` (DOTNET_ROOT=<home>/.dotnet) and built-binary sanity (help, invalid command, check with catalogue.example.json). Also confirm shutdown tests leave no leftover child processes or /tmp/ls-* dirs.
Scope: merge resolution and glue only. Do not edit BLACKBOARD.md, blackboards/**, task-record.md, docs/reviews/**.
Handoff: docs/coordination/v1/INT-3/impl-report.md with the ruach-handoff block (source revisions, combined candidate/tested revision, conflict resolutions, commands/results, discoveries); validate with `bun <ruach>/skills/ruach-handoff/scripts/validate.ts <report> --repo .`; commit this assignment unchanged with the merge and report. Report final SHA.
