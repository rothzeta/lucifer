# Assignment INT-4/integrate (role: implementer; integration)
Task id: INT-4. Workspace: this worktree, branch ruach/luc-11-impl (LUC-11 candidate 39c7dad + report 36f10fc).
Outcome: combine LUC-07 into this branch; combined revision builds, tests pass, and the publish script still works.
Steps: `git merge --no-ff ruach/luc-07-impl` (LUC-07 candidate 88e3099, report d62dfe8). Expect possible README conflict; resolve keeping both. Run dotnet build, dotnet test (DOTNET_ROOT=<home>/.dotnet; expect 279 pass), `deploy/publish.sh --rid linux-arm64 --version 0.0.0-test` (and linux-arm if quick) verifying the checksum, and built-binary sanity (help, invalid command). Never weaken tests.
Scope: merge resolution only. Do not edit BLACKBOARD.md, blackboards/**, task-record.md, docs/reviews/**.
Handoff: docs/coordination/v1/INT-4/impl-report.md with the ruach-handoff block (source revisions, combined candidate/tested revision, resolutions, commands/results, discoveries); validate with `bun <ruach>/skills/ruach-handoff/scripts/validate.ts <report> --repo .`; commit this assignment unchanged with merge and report. Report final SHA.
