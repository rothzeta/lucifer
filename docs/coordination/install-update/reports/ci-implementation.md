task: WORK-17-ci-fix
status: complete
outcome: Both tag-CI failures reproduced under CPU load and fixed (one production Mpv.Dispose race, one test synchronization); full suite 409/409 and shell suites pass; independent review and delivery not yet done.
artifacts:
  - docs/coordination/install-update/reports/ci-implementation.md
  - "branch ruach/lucifer-ci-fix, product commit ec924cb7fc451b975f934b2eb4e565bef29096c2"
  - docs/coordination/install-update/assignments/ci-fix-v1.md
verification:
  - "dotnet build exit 0 and dotnet test --no-build full suite exit 0 (409 passed) on tested revision"
  - "deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0"
  - "Loaded repetition (24 busy loops, 100 s, 3 focused tests): baseline 8/8 runs failed, fixed 0/8 runs failed"
  - "New regression fails on baseline Mpv.cs (round 0: child still alive after Dispose returned), passes with fix"
  - "Not run: CI itself, workflow, hardware, real mpv, systemd, release/tag; no remote writes"
review: not-run
discoveries:
  - "Master CI passed identical code: both failures are scheduling-dependent, not configuration; workflows unchanged"
  - "Retained ruach/lucifer-publish commit 76737b0 (Mpv.Dispose) was reused in design and test shape only; 8e016b7 (PlaySession order) was NOT needed"
  - "Other tests using Present then immediate asserts have the same latent 100 ms idle-wait flake class; left unchanged as out of scope"
blockers: []
candidate_revision: ec924cb7fc451b975f934b2eb4e565bef29096c2
tested_revision: ec924cb7fc451b975f934b2eb4e565bef29096c2

# CI fix implementation report

Base f2faeb0 (branch ruach/lucifer-ci-fix). Product commit ec924cb is the tested revision; the report commit only records evidence. Scratch logs/TRX were kept outside the repository in the session scratchpad (`base-*`, `reg-*`, `after-*` files with `.out`/`.err`/`.trx`); the relevant failure text is reproduced below.

## Exact CI failures (tag CI run 39350436694; release run 39350436703 returned an empty `--log-failed`, so only tag CI logs were readable; master CI 39350434577 passed identical code)

Result: `Failed: 2, Passed: 406, Total: 408`.

1. `AlbumPlaybackTests.Invalid_or_unmapped_tag_leaves_playback_unchanged(release: "none")`: `Assert.Equal() Failure: Collections differ; Expected: ["tA1","tA2","tA3"]; Actual: []` at `PlayA` (AlbumPlaybackTests.cs:212), called from line 301.
2. `PlaySessionTests.Pending_work_that_ignores_cancellation_is_abandoned_at_the_deadline_with_a_failure`: `Assert.False() Failure; Expected: False; Actual: True` at PlaySessionTests.cs:263 (`File.Exists(Socket)` after the run returned).

## Root causes

1. Test synchronization. `Rig.Present` waits at most 100 ms for the apply to go idle, then `PlayA` asserted the queue immediately. On a slow runner the apply (stubbed HTTP + fake mpv IPC) had not finished. Production contract unaffected.
2. Production race in `Mpv.Dispose`. The first caller set `_disposed` and tore down; a concurrent caller returned immediately, before the child was killed and the socket deleted. In the test, `PlaySession` shutdown deadline (1 s) races the run's own 1 s idle wait, so `hooks.RunAll()` (outer) and `using mpv` (inner) dispose concurrently; the outer returned and `ShutdownTimeoutException` was thrown with the child/socket not yet released.

## Changes (product commit ec924cb)

- `Amp.Nfc/Mpv.cs`: `Dispose` now serializes teardown under a separate `_teardown` lock, so every caller returns only after child stop and socket removal. Idempotent; `_gate` is never held while waiting on `_teardown`. Design follows retained commit 76737b0 (reused concisely; not merged wholesale).
- `Amp.Nfc.Tests/AlbumPlaybackTests.cs`: `PlayA` waits (`Eventually`, 10 s ceiling, polling the observable queue) rather than asserting right after the 100 ms idle wait. The asserted contract is unchanged; the ceiling only bounds a hang.
- `Amp.Nfc.Tests/MpvTests.cs`: new `Concurrent_dispose_returns_to_every_caller_only_after_the_child_and_socket_are_released` (two threads released by a Barrier, each checks child/socket right after its own Dispose returns; bounded 50 rounds / 30 s).
- Not changed: PlaySession.cs, workflows, install/update code, other tests.

## Evidence (exit codes are dotnet's own, from files; no tail/tee in the exit path)

Baseline (HEAD f2faeb0), no load: `dotnet build` exit 0; `dotnet test --no-build` exit 0, 408 passed. The failures do not reproduce idle.

Baseline under load (24 busy-loop shell PIDs created and reaped by the script only, 100 s, repeated `dotnet test --no-build --filter "Pending_work_that_ignores|Invalid_or_unmapped_tag"`): 8 runs, 8 failed runs, 12 test failures: AlbumPlayback `release:"none"` Collections differ at line 212 in all 8 runs (Actual `[]` x6, `["tA1"]` x2) and Pending_work Assert.False x4 (line 262 child alive x2, line 263 socket exists x2). Same lines/messages as CI.

Regression before/after (only the new test, `--filter Concurrent_dispose`): old Mpv.cs + new test: exit 1, `round 0: child still alive after Dispose returned`; fixed Mpv.cs: exit 0, 1 passed.

Fixed under identical load: 8 runs, 0 failed runs (every run 3/3 passed).

Fixed candidate, full commands:
- `dotnet build`: exit 0
- `dotnet test --no-build` (TRX saved): exit 0, `Passed: 409, Failed: 0` (408 + 1 new)
- `deploy/test-lucifer-maint.sh`, `test-lucifer-command.sh`, `test-lucifer-install.sh`, `test-lucifer-bootstrap.sh`: each exit 0
- `deploy/publish.sh` packaging step was not run (not touched).

An earlier baseline full-suite run piped through `tail`/`tee` was diagnostic only and is superseded by the file-based run above (also exit 0, 408).

## Expected CI effect and limits

Both flaky failures should stop; the Dispose fix removes a genuine early-return in teardown. Not proven: actual CI runner, real mpv, hardware, release workflow (its failure was the same `dotnet test` step). The 100 ms idle wait remains in `Rig.Tick` for other tests that assert immediately after `Present`; they did not fail in CI and were left alone.

## Handoff validation

Run result recorded in the terminal handoff (validator output cannot be embedded in the file it validates without circularity); see below if appended.
