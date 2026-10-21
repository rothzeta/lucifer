task: WORK-22-ci-fixture-review-v3
role: Reviewer
status: complete
outcome: "B2 RESOLVED by the adopted E1-E3 at 7005bc4. ACCEPTED for ruach/lucifer-install-update and GitHub master as a test-only CI repair, with no blockers. Fresh build 0, full 409/409, replace-delay probe 3/3. E3 stays within the Coordinator-adopted contract. B ready while A is stalled and late A cannot replace B are both preserved. No deadlock or masking found."
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-review-v3.md
  - docs/coordination/install-update/assignments/ci-fixture-review-v3.md
  - docs/coordination/install-update/reports/ci-fixture-review-v2.md
  - docs/coordination/install-update/reports/ci-fixture-review-v1.md
reviewed_revision: 7005bc494635a37efd8ce23fc6cd66b7b89d322b
tested_revision: 7005bc494635a37efd8ce23fc6cd66b7b89d322b
evidence_revision: bc07654321b5ec16bb14caf3dbebd76e91bf794a
baseline: 67426dd6946d9df81b56052c73829972face7f4e
verification:
  - "Branch fast-forwarded from 7b715cd to bc07654; non-docs content of bc07654 equals 7005bc4"
  - "Delta 67426dd..7005bc4 outside docs: Amp.Nfc.Tests/AlbumPlaybackTests.cs only, +8/-4, confined to Replacing_A_with_B_leaves_all_and_only_B_in_order_exactly_once and Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive"
  - "Fresh dotnet build at the worktree root: exit 0. dotnet test --no-build (full): exit 0, 409/409"
  - "Replace probe: a new uniquely named scratch copy (mkdir, fails on collision) of git archive 7005bc4, with a 700 ms delay on the replace loadfile reply in the Album rig; fresh build exit 0. Filter Replacing_A_with_B|Failed_preparation_of_B|Slow_A_then_valid_B exited 0, 3/3 passed. Against v2 at 67426dd, the same probe failed Replacing_A_with_B at line 238 and Slow_A at line 476. The scratch copy was then removed"
  - "Reused with rationale: the M3 mutation evidence (ci-b2.md), not reproduced. It is specific: StepAsync returning WhenIdleAsync() fails at the new E3 message in 96 ms without hanging, which is exactly the regression the assertion must catch. Also reused: deadline 6/6, the append probe, the v1 package conditions, the v2 B1 disposition and the production/install/package evidence. The new delta touches no production code, deadline test, rig, Present or timeout"
  - "Not run: real CI, contention or stress campaigns, reproduction of M3"
review:
  - "Accepted; B2 resolved; no new findings; O1-O3 remain deferred and non-blocking"
discoveries: []
blockers: []

# CI fixture review v3 (WORK-22): E1-E3 / B2

Reviewed: product `7005bc4` (on `67426dd`), at evidence commit `bc07654` on `ruach/lucifer-ci-fixture-review`. Design: `reports/ci-final-plan.md`, adopted by the Coordinator. Implementation report: `reports/ci-b2.md`.

## Assessment of the two changed methods

**E1, `Replacing_A_with_B` (line 237).** The full completion wait follows `Present("04BB")`. Nothing is stalled at that point: A completed in PlayA and B's apply is the only running task, so the wait cannot deadlock. The queue, loads and mode assertions are unchanged and now observe the completed apply.

**E2, `Slow_A` before the release.**

- It waits, with `Eventually` and the existing 10 s hang guard, for `Controller.AppliedRelease == B`. Full idle is correctly never awaited while A is stalled.
- `AppliedRelease` is set only after `CommitAsync` returns (`SelectionController.cs:218-223`), meaning after `ApplyAlbumAsync` has received every reply. The exact `["tB1","tB2"]` assert that follows therefore observes a settled queue.
- A cannot satisfy the condition: it is gated in `PrepareAsync` and never holds `_commit`, so B's commit is not blocked by A.
- `ReleaseId` is a `record struct`, so `==` is value equality.
- If B never commits, the explicit `Assert.Equal` reports `null` versus B rather than masking the failure.
- The new step shows that B becomes ready while A is stalled, which strengthens the old check.
- After the release, the test is unchanged from v2: wait for full idle, then confirm the queue is still B, `AppliedRelease` is B and no tA loadfile was sent. So late A still cannot replace B.

**E3, scan responsiveness.**

- The `sw < 500` wall-clock criterion is replaced by `first.IsCompletedSuccessfully && second.IsCompletedSuccessfully` on the two `StepAsync` tasks captured while A's HTTP is gated. The failure message is the same.
- The fake-clock 150 ms advance and the `AlbumRequests == 1` assertion are retained.
- A step that awaited preparation cannot be complete at that point, and M3 shows the assertion catches exactly that regression without hanging.
- The one tradeoff was adopted explicitly by the Coordinator: a step could do bounded synchronous work and still return a completed task, and this check would not catch it. `StepAsync` (`SelectionController.cs:56-77`) returns `Task.CompletedTask` after lock-bounded state changes, so nothing of that kind exists today.

This removes a real environment-speed contract and directly addresses the owner's timing concern.

**Hidden deadlock or masking:** none. Every new wait is either a full idle wait with nothing stalled, or a condition wait with the existing 10 s bound that ends in an exact assertion. No sleeps were added. The pre-existing `Task.Delay(200)` + `Ticks(3)` after the release only extend the late-A observation; they are not a pass condition. No assertion was removed or loosened. The only replacement is E3's equivalent-or-stronger non-timing criterion.

## Findings

No blocking findings. O1-O3 from v1 remain deferred and non-blocking.

## Delivery acceptance

**Accepted** for `ruach/lucifer-install-update` and non-force GitHub master, under the existing DEC-010 authority and with no new version or tag. The accepted product is `7005bc4`: the cumulative test-only fixture package since `e8181b8`, reviewed across v1-v3. A recording-only successor such as this report commit does not change it. Real CI on master remains the final check, and the CI scheduling cause remains inferred.

## Commands (worktree root unless noted; `timeout 300` on each build or test)

- `git merge --ff-only bc07654…` → fast-forward
- `dotnet build` → 0
- `dotnet test --no-build` → 0 (409/409)
- In a scratch `git archive 7005bc4` copy with the replace-delay probe:
  - `dotnet build` → 0
  - `--filter "FullyQualifiedName~Replacing_A_with_B|FullyQualifiedName~Failed_preparation_of_B|FullyQualifiedName~Slow_A_then_valid_B"` → 0 (3/3)
- The copy was removed by its literal path. Logs were disposable scratch files; the results are recorded here.

## Handoff validation

`bun <ruach-handoff>/scripts/validate.ts <this report> --repo .` → exit 0, ok true, all four revisions resolved.

## State

No tracked source or test changed. The only commits are this report and the unchanged assignment. No processes were left running, and no ports, profiles or remote writes were used.
