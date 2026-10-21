task: WORK-26-ci-b2
status: complete
outcome: "B2 resolved with the adopted recipe E1-E3 in two Album test methods. Matrix M1-M7 each run once and all met: replace-delay probe 3/3, append-delay probe 6/6, E3 mutation fails specifically at the new assertion without hanging, full suite 409/409, deadline filter 6/6, diff confined to AlbumPlaybackTests.cs. Review and delivery pending."
artifacts:
  - docs/coordination/install-update/reports/ci-b2.md
  - docs/coordination/install-update/assignments/ci-b2-v1.md
  - "branch ruach/lucifer-ci-fix, product commit 7005bc494635a37efd8ce23fc6cd66b7b89d322b"
verification:
  - "M1 replace-delay 700 ms probe (scratch archive copy of 7005bc494635a37efd8ce23fc6cd66b7b89d322b, fresh build exit 0), filter Replacing_A_with_B|Failed_preparation_of_B|Slow_A_then_valid_B: exit 0, 3/3 (before, reused from ci-fixture-review-v2 at 67426dd: exit 1, line 238 and 476 collections differ)"
  - "M2 append-delay 700 ms probe, same filter plus Stream_error, Failing_streams, Replacing_a_paused_A (Slow_A appears once): fresh build exit 0, test exit 0, 6/6"
  - "M3 mutation (StepAsync returns WhenIdleAsync() instead of Task.CompletedTask) in a scratch copy, filter Slow_A_then_valid_B: build exit 0, test exit 1 in 96 ms, failing with 'scanning must not wait for preparation' (the new E3 assertion); no hang"
  - "M4/M5 unmodified committed candidate: fresh dotnet build exit 0, dotnet test --no-build exit 0, 409/409"
  - "M6 deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline: build exit 0, test exit 0, 6/6"
  - "M7 git diff 67426dd..7005bc494635a37efd8ce23fc6cd66b7b89d322b outside docs: only Amp.Nfc.Tests/AlbumPlaybackTests.cs, 8 insertions, 4 deletions"
  - "Not rerun with rationale: shell suites and packaging (no production, deploy or config file changed); no contention campaign or real CI reproduction"
review: not-run
discoveries:
  - "Harness interruption, not technical evidence: the first scratch-setup command for M1-M3 (which contained a variable-path rm -rf) was cancelled by the Coordinator before it ran anything; no scratch copy existed afterwards. M1-M3 were then run once in newly created uniquely named directories using plain mkdir (fails on collision) with no deletion"
  - "Probes and the M3 mutation exist only in scratch copies under the session scratchpad and were never in the worktree or committed; the committed tree has no probe or mutation text"
blockers: []
baseline: dd3c8992b824802d2ca6adf4a427da951fb755a5
candidate_revision: 7005bc494635a37efd8ce23fc6cd66b7b89d322b
tested_revision: 7005bc494635a37efd8ce23fc6cd66b7b89d322b

# CI B2 report (WORK-26)

Product `7005bc494635a37efd8ce23fc6cd66b7b89d322b` on dd3c899 (source otherwise 67426dd). Only two methods in Amp.Nfc.Tests/AlbumPlaybackTests.cs changed, as written in reports/ci-final-plan.md:

- **E1** `Replacing_A_with_B…`: `await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));` after `Present("04BB")`.
- **E2** `Slow_A_then_valid_B…`: after `Present("04BB")` and before the unchanged queue assertion and the A release, an `Eventually` on `Controller.AppliedRelease == ReleaseB` (10 s hang guard) asserted equal to B. This is B's own commit while A is still stalled; no full idle is awaited while A is gated.
- **E3** `Slow_A_then_valid_B…`: the six-line stopwatch block and the `sw < 500` assertion are replaced by capturing the two `StepAsync` tasks (150 ms fake-clock advance kept) and asserting both `IsCompletedSuccessfully` with the same message. The AlbumRequests assertion and the `using System.Diagnostics` (needed by `Eventually`) are unchanged.

Accepted tradeoff (Coordinator adoption of E3): a hypothetical bounded synchronous wait that returns a completed task no longer fails a 500 ms criterion; none exists in the inspected code. Responsiveness is asserted through completed `StepAsync` tasks while A is gated, plus B committed before A is released; B's queue assertion was not moved after the release.

Unchanged: Rig.Present/Tick, Fast/Normal, the PlayA and B1 changes, all other tests and assertions, all production code.

## Matrix

Results are in the leading block. Probes: M1 delays the reply to the `replace` loadfile by 700 ms in the Album rig handler, M2 delays the `append` reply by 700 ms; both are scratch-only. Each scratch build had exit 0 before its test run. M3's mutation only verifies that the E3 assertion catches a step awaiting gated preparation; it is scratch-only and never committed.

## Failed and invalid attempts

No technical failures. The M3 failure is the expected outcome. The one interrupted command is recorded in discoveries as a harness interruption.

## Limits

Static reasoning that E2 cannot deadlock is supported by the M1/M2 runs. Probes show completion-based synchronization under a slow peer, not CI scheduling. Independent review and a separate delivery are pending.
