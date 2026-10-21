task: WORK-22-ci-fixture-review-v2
role: Reviewer
status: complete
outcome: "B1 as defined is RESOLVED at 67426dd: four correct completion waits, 4/4 under the 700 ms append probe, full suite 409/409. Acceptance is still WITHHELD for ruach/lucifer-install-update and GitHub master because of B2, an omission in my v1 review. Replacing_A_with_B and Slow_A_then_valid_B (pre-release assert) still assert the B queue after only Present's 100 ms window. Both fail when the same 700 ms delay is moved to the replace reply. Coordinator or owner to dispose."
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-review-v2.md
  - docs/coordination/install-update/assignments/ci-fixture-review-v2.md
  - docs/coordination/install-update/reports/ci-fixture-review-v1.md
reviewed_revision: 67426dd6946d9df81b56052c73829972face7f4e
tested_revision: 67426dd6946d9df81b56052c73829972face7f4e
evidence_revision: 3fbc4cac72fdb61142e9722da53ab2c8ac0966a3
baseline: 57d343e84b1d7aa82699bfd446bd08d2c621a35c
verification:
  - "Branch fast-forwarded from d51e277 to 3fbc4ca; non-docs content of 3fbc4ca equals 67426dd"
  - "Delta 57d343e..67426dd outside docs: Amp.Nfc.Tests/AlbumPlaybackTests.cs only, +4/-0. Each line is await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10)), at lines 254, 445, 479 and 676"
  - "Fresh dotnet build at the worktree root: exit 0. dotnet test --no-build (full): exit 0, 409/409"
  - "Append probe: a scratch copy (git archive 67426dd) with the unchanged 700 ms append delay in both rigs; fresh build exit 0. The four B1 tests exited 0, 4/4 passed"
  - "Concern probe on the same scratch copy, one run, no retries: the Album delay moved from append to replace; fresh build exit 0. Filter Replacing_A_with_B|Failed_preparation_of_B|Slow_A_then_valid_B exited 1. Replacing_A_with_B failed at line 238 (Expected [tB1,tB2], collections differ at position 1). Slow_A_then_valid_B failed at line 476, before the release, with the same message. Failed_preparation_of_B passed. The scratch copy was then removed"
  - "Reused with rationale: deadline filter 6/6 and focused-5 probe pass from v1 at 57d343e, plus shell suites and production/package evidence. The new delta adds waits only to four non-deadline tests and touches no production, deadline test, rig, Present or timeout definition"
  - "Not run: real CI, contention or stress campaigns, deadline filter rerun"
review:
  - "B1 resolved; acceptance withheld on B2; O1-O3 deferred as assigned"
discoveries:
  - "The v1 append-only probe could not expose B2, because the fake peer updates its queue when a loadfile is received, before the delayed reply. A receipt-side margin needs a delay before the next request is sent, which the replace delay provides"
blockers:
  - "B2: Replacing_A_with_B_leaves_all_and_only_B_in_order_exactly_once (Present at line 236, assert at 238) and Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive (Present at 475, assert at 476) assert B's full queue after only Present's swallowed 100 ms wait. This is the B1 defect class. Decide on a test-only wait or explicit risk acceptance"

# CI fixture review v2 (WORK-22): B1 re-review

Reviewed: product `67426dd` (on `57d343e`), at evidence commit `3fbc4ca` on `ruach/lucifer-ci-fixture-review`. My v1 review (`ci-fixture-review-v1.md`) accepted every original package condition at `57d343e`. Those conditions are unaffected, because the new delta is four added lines in four non-deadline tests.

## B1 disposition: resolved

| Test | Wait placement | Assessment |
|---|---|---|
| `Replacing_a_paused_A…` (254) | after `Present("04BB")`, before the queue/unpause asserts | Nothing is stalled, so idle means B's apply, including pause=false, has completed. `Assert.False(rig.Paused)` and the unpause-after-replace order now observe final state |
| `Stream_error…` (445) | after `Present("04AA")`, before `end-file` | `NextEntryId - 2` is now A's first entry, not a stale id. The Failed status and detail asserts are unchanged |
| `Slow_A_then_valid_B…` (479) | after `releaseA.SetResult()` | A's stalled task can now finish (stale generation → return or cancelled), so idle cannot deadlock. The pre-release `sw < 500` and `AlbumRequests == 1` responsiveness asserts are unchanged and precede the wait. `AppliedRelease == B` now observes the settled state |
| `Failing_streams…` (676) | after `Present("04AA")` | Same entry-id settling as Stream_error. Redaction asserts are unchanged |

None of the four waits can deadlock: no stall is active when they run, and `PrepareAndCommitAsync` never faults. A hang still fails loudly through the 10 s `TimeoutException`. No assertion was removed or loosened. No sleeps, speed requirements, `Rig.Present` or timeout changes were added. The existing `Task.Delay(200)` + `Ticks(3)` in Slow_A are pre-existing, and they only make the "late A cannot replace B" check observe more time, so they are not a pass condition.

## B2 (blocking, my v1 omission): two queue asserts still within 100 ms

**Location:** `AlbumPlaybackTests.cs:236-238` (`Replacing_A_with_B…`) and `:475-476` (`Slow_A_then_valid_B…`, before the release).

**Problem:** Each test calls `rig.Present("04BB")`, then asserts `["tB1","tB2"]`. `Present`'s idle wait is capped at 100 ms and its timeout is swallowed. In Slow_A the wait always times out, because A is stalled. The assert therefore needs B's preparation, the replace round trip and the send of the tB2 append to finish within roughly 100 ms of wall-clock time.

**Why it matters:** This is the same class and the same reasoning as B1. The 100 ms window is shorter than the 300-500 ms stalls already seen on the CI runner. In v1 I described these tests as passing "only because the fake peer records queue changes when a request is received". I did not flag that they still depend on the replace round trip finishing within the window. That was an error in v1, not new scope.

**Evidence:** In a scratch copy of 67426dd, I moved the 700 ms delay from the append to the replace reply, did a fresh build, and ran once:

- `Replacing_A_with_B` failed at line 238.
- `Slow_A_then_valid_B` failed at line 476: Expected `[tB1,tB2]`, collections differ at position 1.
- `Failed_preparation_of_B…` (line 361-362 has the same construction) passed under this probe. I did not investigate further, per the assignment's no-adjacent-audit bound.

**Suggested direction (test-only):**

- `Replacing_A_with_B`: add the same `WhenIdleAsync().WaitAsync(10 s)` after `Present("04BB")`. Nothing is stalled there.
- `Slow_A`'s pre-release assert: full idle is impossible while A is stalled. Use a bounded wait for B's queue, such as `Eventually` with `q.Length == 2` and a 10 s bound, followed by the unchanged exact assert. Or move that exact assert after the new post-release wait, if checking the queue before the release is not essential.
- Verify with the replace-delay probe before and after.

Alternatively, the owner can accept the residual risk explicitly.

## Optional

O1-O3 from v1 remain deferred, as assigned. There are no new optional findings.

## Delivery acceptance

Withheld for `ruach/lucifer-install-update` and GitHub master, pending disposition of B2. If B2 is fixed as above, or explicitly accepted as a risk, I have no other objection to the package as reviewed in v1 and v2.

## Commands (worktree root unless noted; `timeout 300` on each build or test)

- `git merge --ff-only 3fbc4cac…` → fast-forward
- `dotnet build` → 0
- `dotnet test --no-build` → 0 (409/409)
- In a scratch `git archive 67426dd` copy with the append probe:
  - `dotnet build` → 0
  - B1 filter (`Stream_error_on_the_selected_album|Failing_streams_never_put|Replacing_a_paused_A|Slow_A_then_valid_B`) → 0 (4/4)
- In the same copy with the delay moved to replace:
  - `dotnet build` → 0
  - `Replacing_A_with_B|Failed_preparation_of_B|Slow_A_then_valid_B` → 1 (2 failed, 1 passed, as above)
- The copy was deleted. Logs were disposable scratch files; the results are recorded here.

## Handoff validation

`bun <ruach-handoff>/scripts/validate.ts <this report> --repo .` → exit 0, ok true, all four revisions resolved.

## State

No tracked source or test changed. The only commits are this report and the unchanged assignment. No processes were left running, and no ports, profiles or remote writes were used.
