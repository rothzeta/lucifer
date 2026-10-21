task: WORK-21-ci-fixture-v4
status: complete
outcome: Review finding B1 resolved. The four named normal-path Album tests now wait for the completed apply (WhenIdleAsync with the 10 s hang guard) before relying on settled state; the deliberately stalled A in Slow_A_then_valid_B is not waited on. Under the unchanged 700 ms append probe all 9 cases (5 prior + 4 B1) pass. Full suite 409/409 and the six deadline cases pass. Re-review and delivery pending.
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-v4.md
  - docs/coordination/install-update/assignments/ci-fixture-v4.md
  - "branch ruach/lucifer-ci-fix, product commit 67426dd6946d9df81b56052c73829972face7f4e"
verification:
  - "Before (independent review, product 57d343e, same 700 ms probe, reused not rerun): Stream_error Expected Failed / Actual Loading; Failing_streams Expected Failed / Actual Loading; Replacing_a_paused_A Paused False expected / True; Slow_A_then_valid_B AppliedRelease B expected / null (reports/ci-fixture-review-v1.md B1)"
  - "After (four waits + same unchanged probe, fresh build exit 0): dotnet test --no-build --filter of the 5 prior cases plus the 4 B1 tests exit 0, 9/9 passed"
  - "Probe removed (no PROBE text left); fresh build exit 0; deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline exit 0, 6/6"
  - "Final committed product: fresh dotnet build exit 0 then dotnet test --no-build exit 0, 409/409 (once, as assigned)"
  - "Not rerun with rationale: the four deploy shell suites and packaging, since only test fixture completion waits changed (git diff 57d343e..HEAD outside docs is 4 insertions in AlbumPlaybackTests.cs)"
  - "Not run: contention campaign, the two deliberate Fast deadline tests under the probe (their probe failures are expected, not B1), real CI"
review: not-run
discoveries:
  - "Slow_A_then_valid_B: the wait is placed after releaseA.SetResult() and before the existing Delay(200)/Ticks, so every pre-release responsiveness, ordering and queue assertion is untouched and A stays stalled until released"
blockers: []
baseline: d51e2770942daf6e7d1b3226513fb90abfa34943
candidate_revision: 67426dd6946d9df81b56052c73829972face7f4e
tested_revision: 67426dd6946d9df81b56052c73829972face7f4e

# CI fixture v4: B1 sibling completion waits

Product `67426dd6946d9df81b56052c73829972face7f4e` on review report d51e277 (source otherwise identical to 57d343e). Only AlbumPlaybackTests.cs changed, four insertions of `await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));`:

1. `Replacing_a_paused_A_starts_B_unpaused…`: after `Present("04BB")`, before the queue, unpause and order assertions.
2. `Stream_error_on_the_selected_album_is_a_visible_failure`: after `Present("04AA")`, before `NextEntryId - 2` is used.
3. `Slow_A_then_valid_B…`: after `releaseA.SetResult()` and before the final applied-state assertions. No full-idle wait while A is stalled.
4. `Failing_streams_never_put_authenticated_url_parameters…`: after `Present("04AA")`, before the loaded URL and entry id are used.

Unchanged: `Rig.Present` (still bounded), all assertions, the v1/v2/v3 changes, explicit Fast deadline tests, the existing 200 ms delay and tick calls in Slow_A (not added to or removed), all production and other files. No performance requirement, sleep or loosened assertion was added; the 10 s bound only guards a hang.

## Evidence

The before failures are the reviewer's, for the same probe on 57d343e. The after run used the same probe (700 ms delay on append loadfile replies, temporary scratch edit, not committed, removed and checked by grep) with a fresh build exit 0 and passes 9/9. This shows setup completion for these tests under a slow peer; it is not a CI scheduling reproduction. Logs, TRX and exits are in the session scratchpad (v6 directory). No failed or invalid attempts in this unit.

## Limits

Optional O1-O3 from the review remain deferred. Real CI is the final check; independent re-review and separate delivery are pending.
