task: WORK-17-ci-fix-v2
status: complete
outcome: B1 fixed. Socket ownership for a launched child is now also recorded after its IPC connect succeeds. New concurrent-dispose test and the unchanged Start_adopts test no longer fail under the reviewer's load condition. Full suite 409/409 and shell suites pass. Re-review required before delivery.
artifacts:
  - docs/coordination/install-update/reports/ci-fixes-v2.md
  - docs/coordination/install-update/assignments/ci-fix-v2.md
  - "branch ruach/lucifer-ci-fix, product commit 04d88c54d0cf2c2afbc3c6010490dd0375886b66"
verification:
  - "dotnet build exit 0 on the clean product commit (only the untracked assignment file present); dotnet test --no-build full suite exit 0, Passed 409 Failed 0 (TRX saved)"
  - "deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0"
  - "Same load (24 owned busy loops, 100 s, filter Pending_work|Invalid_or_unmapped_tag|Concurrent_dispose|Start_adopts), valid runs: before fix 6 runs, 3 failed; after fix 7 runs 0 failed; final commit re-run 5 runs 0 failed"
  - "Not run: real CI, hardware, real mpv, release workflow; no remote actions"
review: not-run
discoveries:
  - "Prior-cycle ownership race had two forms in the new test: round 0 and later rounds (27, 33) both left the socket behind"
  - "INVALID attempts, kept only as records and not used as evidence: (1) an earlier first load comparison (before 5 runs/0 failed; after 6 runs/2 failed) ran with both builds exiting 1 because my runner script was broken by an edit, so the tests used stale binaries; (2) an early gate script check on a deliberately broken source (build exit 1) proved the gate stops on build failure and is not a product result"
  - "The freshness check in the later gate is a local sanity guard (mtime), not an acceptance contract; provenance is: build exit 0 on a clean commit, then --no-build tests on those binaries"
blockers: []
baseline: f2faeb0df2fff7a6c1bdd994dda6513d45cf3c28
candidate_revision: 04d88c54d0cf2c2afbc3c6010490dd0375886b66
tested_revision: 04d88c54d0cf2c2afbc3c6010490dd0375886b66

# CI fixes v2: B1 socket ownership race

Sources: first fix `ec924cb` (Dispose serialization, PlayA wait), review evidence `a050559` (reports/ci-review-v1.md, B1 withheld), fast-forwarded into this branch. Combined product commit `04d88c5` = those plus the B1 fix. Reports are evidence only.

## B1 resolution

`Mpv.OpenAsync` sampled `File.Exists(socketPath)` only before each connect attempt. A launched child that bound its socket after the sample but before a successful connect was adopted with `_ownsSocket=false`, so `Dispose` left its socket behind (reviewer: `ownsSocket=False` in Concurrent_dispose round 0 and in Start_adopts).

Change (`Amp.Nfc/Mpv.Launch.cs`, +5 lines): after `ConnectAndVerifyAsync` succeeds and before `Adopt`, if this is a launched child (`ownership` not null) and the path exists, mark it owned. Reasoning: `StartAsync` first refuses a live socket or removes a stale one, so the path was clear at launch; a socket present once connected is the child's. Unchanged: `ConnectAsync` without a child never owns the socket; a live foreign socket is still refused before launch; failure cleanup still deletes only when `Owned`. No test, assertion or timeout changed (`git diff a050559..04d88c5` outside docs touches only Mpv.Launch.cs).

Residual risk: another process replacing the socket path between launch and connect would be recorded as owned and could be deleted at Dispose. This pre-launch/replacement window cannot be distinguished from the child's socket at this layer and was not extended into a design change.

## Results (actual dotnet/script exit codes; full stdout/stderr/TRX kept outside the repository in the session scratchpad)

Load condition: 24 busy-loop shell PIDs created, killed and waited by the script only; 100 s of repeated `dotnet test --no-build` with the four-test filter.

- Before fix (old Launch.cs, build exit 0): 6 runs, 3 failed, each `Concurrent_dispose` "socket still present after Dispose returned" at round 0, 27 and 33.
- After fix (build exit 0): 7 runs, 0 failed.
- Final committed `04d88c5` (build exit 0): 5 runs, 0 failed.
- Full suite `dotnet test --no-build` on `04d88c5`: exit 0, 409 passed, 0 failed. This includes the existing foreign-socket, ConnectAsync and failure-cleanup tests.
- `deploy/test-lucifer-maint.sh`, `-command.sh`, `-install.sh`, `-bootstrap.sh`: each exit 0.

## Invalid attempts (not evidence)

1. First before/after comparison (before 5 runs/0 failed, after 6 runs/2 failed): both `dotnet build` steps exited 1 because my runner script had been broken by an edit, so the tests ran stale binaries. Discarded and redone above with build exit 0.
2. A gate-script probe against a deliberately broken source (build exit 1) verified the gate stops on a build failure; it is not a product result and the source was restored.

## Limits

Only the bounded plan was run; no further stress. Not proven: real CI runner, real mpv/hardware, release workflow. Historical tag runs stay failed. Re-review required before separate delivery.

## Handoff validation

Validator result is returned in the terminal handoff.
