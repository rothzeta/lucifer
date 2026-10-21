task: WORK-21-ci-fixture-v3
status: complete
outcome: PlayA now waits for the completed apply (Controller.WhenIdleAsync, 10 s hang guard) before asserting the queue. With the unchanged 700 ms append probe, Last_track_ending and Transport_is_serialized both pass after, where Last_track_ending failed (Expected 5, Actual 6) before. Full suite 409/409 twice and four shell suites exit 0. Review and delivery pending.
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-v3.md
  - docs/coordination/install-update/assignments/ci-fixture-v3.md
  - "branch ruach/lucifer-ci-fix, product commit 57d343e84b1d7aa82699bfd446bd08d2c621a35c"
verification:
  - "Probe before (valid v2 evidence, recipe plus D1 at 3656101): Last_track_ending Expected 5 / Actual 6 failed; Transport_is_serialized and TransportControl x3 passed"
  - "Probe after (PlayA completion wait + same unchanged 700 ms append probe, fresh build exit 0): test exit 0, 5/5 passed (TransportControl x3, Transport_is_serialized, Last_track_ending)"
  - "Probe removed (no PROBE text left); fresh build exit 0; deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline exit 0, 6/6; Uncertain_outcome 0.91 s"
  - "Final committed product: fresh dotnet build exit 0 then dotnet test --no-build exit 0, 409/409, twice; deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0"
  - "Not run: contention campaign, real CI; no remote actions"
review: not-run
discoveries:
  - "The v2 report's proposal to disregard the Last_track probe is superseded: the failure was a real PlayA sequencing defect (it returned when the third append was queued, before the trailing pause=false request), fixed by this change per reports/ci-evidence.md"
blockers: []
baseline: b449b0865afe29745fa56f65dd45f02b7cde544c
candidate_revision: 57d343e84b1d7aa82699bfd446bd08d2c621a35c
tested_revision: 57d343e84b1d7aa82699bfd446bd08d2c621a35c

# CI fixture v3: PlayA completion wait

Source: architect disposition a7f2394 (reports/ci-evidence.md), product `57d343e84b1d7aa82699bfd446bd08d2c621a35c` on the v2 product 3656101. The only source change since 3656101 is the PlayA region of Amp.Nfc.Tests/AlbumPlaybackTests.cs (4 insertions, 3 deletions): the `Eventually` queue wait and comment were replaced by `await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));` with a short explanatory comment, followed by the same exact `Assert.Equal(["tA1","tA2","tA3"], rig.QueueIds)`. The rest of PlayA, the v1/v2 recipe, all production allowances and the explicit Fast deadline tests are unchanged. The 10 s bound only guards a hang (it replaces the previous 10 s bound); no sleep, speed requirement or assertion weakening was added.

## Causality

Before: PlayA returned once the fake peer had queued three entries (received, not yet answered), so the apply's trailing pause=false request could land after Last_track_ending sampled its request count; the 700 ms append probe widened that window (5 vs 6). After: PlayA returns only when the controller's apply, including pause=false, has completed, and the same probe passes. The probe is a temporary diagnostic (scratch edits, removed and verified), not a committed or accepted performance requirement.

## Commands and exits (real exit codes from files; full stdout/stderr/TRX in the session scratchpad, v5 directory)

Each step ran a fresh `dotnet build` (exit 0) before any `--no-build` test; see the leading block for results. No failures occurred in this unit and there were no invalid attempts.

## Limits

Proof is deterministic-probe plus full suites, not a CI scheduling reproduction. Optional ordering assertion not added. Real CI remains the final check. Independent review and a separate delivery are pending.
