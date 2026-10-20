task: WORK-18-ci-review
role: reviewer
assignment: docs/coordination/install-update/assignments/ci-review-v1.md
status: complete
outcome: "Not accepted for local or master delivery: one blocking finding (B1). The Mpv.Dispose serialization and the PlayA wait are correct and verified. Under owned CPU load, the new concurrent-dispose regression fails on the fixed candidate because of a separate, pre-existing socket-ownership race in Mpv.OpenAsync. The unchanged Start_adopts test fails the same way, so the corrected CI is not yet dependable"
baseline: f2faeb0df2fff7a6c1bdd994dda6513d45cf3c28
candidate_revision: 7cb8321ba585448543207f434e0a6f4259eed979
reviewed_revision: 7cb8321ba585448543207f434e0a6f4259eed979
tested_revision: ec924cb7fc451b975f934b2eb4e565bef29096c2
artifacts:
  - docs/coordination/install-update/reports/ci-review-v1.md
  - docs/coordination/install-update/assignments/ci-review-v1.md
  - docs/coordination/install-update/reports/ci-implementation.md
  - branch ruach/lucifer-ci-review
verification:
  - "Protected paths: git diff f2faeb0 7cb8321 -- .github deploy Amp.Nfc/PlaySession.cs is empty; the product delta is only Mpv.cs, AlbumPlaybackTests.cs and MpvTests.cs"
  - "Workflow commands at ec924cb (identical product tree to 7cb8321), idle: dotnet build exit 0; dotnet test --no-build exit 0 (Passed 409, Failed 0); deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0; ci.yml package step (publish.sh both RIDs, out dir in scratch, sha256sum -c, tar entry checks) exit 0"
  - "Regression before/after, idle, filter Concurrent_dispose: ec924cb tests with f2faeb0 Mpv.cs exit 1 in 3 of 3 runs ('round 0: child still alive after Dispose returned'); fixed Mpv.cs exit 0 in 3 of 3 runs"
  - "Owned load (24 busy loops, PIDs created, killed and waited by the script), filter Pending_work|Invalid_or_unmapped_tag|Concurrent_dispose: baseline f2faeb0 failed 4 of 4 runs (AlbumPlayback release none 'Collections differ' each time); fixed ec924cb failed 2 of 4 runs, each time Concurrent_dispose 'round 0: socket still present after Dispose returned'"
  - "Scratch-only diagnostic copy (not repo source) reading Mpv._ownsSocket, same load, 6 runs: 2 failed; both failures had ownsSocket=False (Concurrent_dispose round 0, and unchanged Start_adopts_the_child_and_dispose_stops_it_and_removes_only_its_socket at MpvTests.cs:485)"
  - "Fixed ec924cb under the same load, filter Pending_work|Invalid_or_unmapped_tag, 10 runs: 10 of 10 exit 0 (3/3 passed each run)"
  - "Handoff validator: see the Handoff validation section"
review:
  - "Independent review by Reviewer; acceptance withheld on B1"
discoveries:
  - "Mpv.OpenAsync decides socket ownership before each connect attempt (Mpv.Launch.cs:137-149). If the child binds its socket between that File.Exists check and a successful connect in the same iteration, Owned stays false and Dispose (Mpv.cs:177-178) leaves the child's socket file behind. This is pre-existing production behavior, independent of the Dispose fix"
  - "The same race can affect PlaySessionTests socket assertions, including the CI-failing Pending_work line 263. ServeSocketLater uses the same late-bind pattern. It did not reproduce in my 10 fixed loaded runs, so the original line-263 CI failure may have had either cause"
  - "Existing tag runs (tag CI 39350436694, release 39350436703) used immutable tag source and stay failed. A master push can only produce a new master CI result. It cannot correct those historical runs"
blockers:
  - "B1: the new regression test, and the unchanged Start_adopts test, fail under load because of the OpenAsync socket-ownership race. The Implementer amendment must fix it before acceptance"

# CI fix review (WORK-18 v1)

Reviewed `f2faeb0..7cb8321`. The product commit is `ec924cb`. `7cb8321` adds only the implementation assignment and report, so its product tree is identical. Delivery is **not accepted**, for either local or master delivery, until B1 is fixed and re-reviewed. The assignment's default allows two fix/review cycles; this is cycle 1.

## Assessment of the implemented changes

**Mpv.Dispose serialization (Mpv.cs:152-182): correct, no finding.**
- Every caller takes `_gate` only for the cheap `_disposed`/timer step and releases it, then takes `_teardown`. `_gate` is never held while waiting on `_teardown`.
- Inside `_teardown`, the code takes `_gate` only briefly: through `FailPending`, and through `Lose` from the `child.Exited` event on another thread, which does not need `_teardown`.
- Reply TCSs use `RunContinuationsAsynchronously` (Mpv.Ipc.cs:23), so `TrySetException` runs no continuations inline.
- A same-thread re-entrant `Dispose` returns immediately because `Monitor` is re-entrant and `_tornDown` is set. Neither the `_life.Cancel` path nor `StatusChanged` (suppressed after `_disposed`) reaches `Dispose`.
- **Idempotence:** after the first call, later calls do nothing.
- **Bounded shutdown:** a concurrent caller now waits at most the first caller's teardown, which is bounded by `WaitForExit(2000)`. That is the same bound as a single call, so `PlaySession`'s deadline path (`hooks.RunAll()` after `ShutdownTimeout`) keeps its bound.
- The race it removes is genuine. Idle runs of the old code fail the new test deterministically.

**PlayA wait (AlbumPlaybackTests.cs:212-214): correct, no finding.**
- It still asserts the exact queue `["tA1","tA2","tA3"]`. `Eventually` waits on the observable queue length with a 10 s ceiling.
- There is no sleep and no weakened expectation. A wrong queue of length 3 still fails `Assert.Equal`, and a hang still fails at the ceiling.
- Before the change, the failure was a pure test-timing problem: `Present`'s 100 ms idle wait. I reproduced it on the baseline under load (4 of 4 runs).

## Findings

### B1 (blocking, required): socket ownership is decided before connecting, so the child's socket can be left behind; the new regression is unstable under load

- **Location:** `Amp.Nfc/Mpv.Launch.cs:137-149` (`OpenAsync`). Consequences appear at `Amp.Nfc/Mpv.cs:177-178`, `Amp.Nfc.Tests/MpvTests.cs:516-523` (new test) and `MpvTests.cs:485` (unchanged `Start_adopts_the_child_and_dispose_stops_it_and_removes_only_its_socket`).
- **Problem:** each loop iteration sets `ownership.Owned = true` only if `File.Exists(socketPath)` before `ConnectAndVerifyAsync`. A child that binds its socket after that check but before the connect is still connected to and adopted, but with `ownsSocket=false`. `Dispose` then correctly refuses to delete a socket it believes it does not own, and the child's socket file stays behind. A real mpv can hit the same window.
- **Why it matters:**
  - The candidate adds a 50-round test that hits this window in round 0 under the scheduling pressure that caused the original CI failures. It also leaves the existing `Start_adopts` test exposed to it.
  - The corrected master CI could therefore fail again on a slow runner.
  - In production, a leftover socket file after shutdown is mitigated, not prevented: the next start's `RefuseLiveSocketOrRemoveStale` removes it.
  - The `Pending_work` line-263 socket assertion that failed in CI depends on the same ownership flag, so it is plausibly exposed too.
- **Evidence:** all failures came from runs under my own 24-loop load (PIDs created, killed and waited by the script; nothing else running).

  Fixed candidate `ec924cb`, unmodified, runs 1 and 4 of 4:
  ```
  Failed Amp.Nfc.Tests.MpvTests.Concurrent_dispose_returns_to_every_caller_only_after_the_child_and_socket_are_released [640 ms]
  Assert.All() Failure: 2 out of 2 items in the collection did not pass.
  [0]: Item:  Tuple (False, True)
       Error: round 0: socket still present after Dispose returned
  [1]: Item:  Tuple (False, True)
       Error: round 0: socket still present after Dispose returned
  at ... MpvTests.cs:line 520
  ```
  The child was dead and both callers returned after teardown, so the Dispose fix held. Only the socket remained.

  Scratch copy of `ec924cb` whose test only adds a reflection read of `Mpv._ownsSocket` to the message (6 loaded runs, 2 failed):
  ```
  Error: round 0: socket still present after Dispose returned; ownsSocket=False   (x2 callers)
  Failed ...MpvTests.Start_adopts_the_child_and_dispose_stops_it_and_removes_only_its_socket [289 ms]
  Assert.False() Failure  Expected: False  Actual: True   at MpvTests.cs:line 485
  ```
- **Suggested direction (do not implement here; the Implementer amendment handles it):**
  - Decide ownership for a launched child after a successful connect, not only before it, for example by re-checking `File.Exists` after `ConnectAndVerifyAsync` and before `Adopt`.
  - Alternatively, treat the socket as owned whenever `StartAsync` launched the child and the connect succeeded. This relies on `RefuseLiveSocketOrRemoveStale` (Mpv.Launch.cs:31) having already guaranteed that no file existed, or that only a dead stale file existed and was removed, before launch.
  - **Foreign-socket safety:**
    - Keep the refusal of a live foreign socket (the `Socket_served_by_another_live_instance_is_neither_taken_nor_deleted` contract).
    - Keep `ConnectAsync` (no child) never owning the socket.
    - Keep the `StartAsync` failure path (Mpv.Launch.cs:67) deleting only an owned socket.
  - Any residual risk is a different process creating the path between the pre-launch check and the child's bind. Record that risk explicitly rather than widen deletion.
  - Keep the new and existing tests unchanged, and verify both under the same bounded owned load.

No other material findings. Optional only: the 100 ms `Rig.Tick` idle-wait flake class in other `AlbumPlaybackTests` was correctly left out of scope, as the implementation report notes.

## CI expectation vs historical runs

- Historical tag CI 39350436694 and release 39350436703 ran immutable `v0.1.1` source. They remain failed and no master push changes them.
- The deliverable is a passing master CI on the corrected source. B1 must be fixed first; otherwise that CI may still flake.
- No tag, workflow, rerun or remote action was performed.

## Unverified

The actual GitHub runner, real mpv, hardware and the release workflow were not run. My load runs are a bounded reproduction (roughly 30 short runs), not a statistical failure rate.

## Commands and evidence location

- Commands ran in the review worktree, plus `git archive` copies in session scratch: `src-base` (f2faeb0), `src-fixed` (ec924cb), `src-oldmpv` (ec924cb with f2faeb0 Mpv.cs) and `src-diag` (ec924cb plus the reflection diagnostic in the test message).
- Each `dotnet` and shell command wrote stdout, stderr and TRX to separate files. Exit codes were taken directly, with no pipes in the exit path.
- Scratch is disposable. The failure text quoted above is the preserved evidence.

## Handoff validation

The validator command and result are reported in the terminal handoff, because embedding them here would be circular.
