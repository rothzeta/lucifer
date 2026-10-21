task: WORK-21-ci-fixture-v2
status: complete
outcome: "D1 adopted: the 400 ms StreamStart timer is confirmed as the cause of the Album probe failure in Transport_is_serialized (Ignored -> Applied with only that change). The other probe failure, Last_track_ending (Expected 5, Actual 6), has a different cause and is NOT resolved or treated as acceptance evidence; no scope broadening. Full suite 409/409 twice, four shell suites exit 0. Review and delivery pending."
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-v2.md
  - docs/coordination/install-update/assignments/ci-fixture-v2.md
  - "branch ruach/lucifer-ci-fix, product commit 3656101114f7b0be5ac4bedbe9af79b5f7268663 (v1 recipe ab203ed unchanged beneath it)"
verification:
  - "Contrast, same 700 ms append probe, fresh build exit 0 before each: recipe only (ab203ed, v1 evidence): Transport_is_serialized failed Expected Applied / Actual Ignored, Last_track_ending failed Expected 5 / Actual 6 (3 passed 2 failed). Recipe + D1 only: Transport_is_serialized passed, Last_track_ending still failed Expected 5 / Actual 6 (4 passed 1 failed), TransportControl x3 passed"
  - "Probe removed (no PROBE text left); fresh build exit 0; deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline exit 0, 6/6; Uncertain_outcome 0.91 s; AlbumPlayback Accepted_playlist 0.52 s (400 ms StreamStart still enforced)"
  - "Final committed product: fresh dotnet build exit 0 then dotnet test --no-build exit 0, 409/409, twice; deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0"
  - "Not run: contention campaign, real CI; no remote actions"
review: not-run
discoveries:
  - "Last_track_ending probe failure is a likely probe artifact (inference from code reading, not verified): PlayA returns once the queue has three entries, not when the apply has finished, so with an artificially slow peer the apply's trailing request lands after the test samples its request count"
  - "That 700 ms delay is far beyond any normal scheduling delay; the probe is not appropriate acceptance evidence for Last_track_ending, see the report body"
blockers: []
baseline: 99996a42bf271991e25fa7b45d9dba02514330ee
candidate_revision: 3656101114f7b0be5ac4bedbe9af79b5f7268663
tested_revision: 3656101114f7b0be5ac4bedbe9af79b5f7268663

# CI fixture v2: Album probe evidence and D1

Product commit `3656101114f7b0be5ac4bedbe9af79b5f7268663` on ruach/lucifer-ci-fix (base 99996a4, v1 recipe ab203ed retained unchanged). Only AlbumPlaybackTests.cs changed: `Normal` gains `StreamStart = 20 s` (production allowance), `Accepted_playlist…` uses `BuildAsync(timeouts: Fast)`, and the `Normal` comment is updated. No assertion changed, no production code, no other file.

## Causal contrast

The probe (temporary, not committed, removed and verified by grep) delays each `append` loadfile reply by 700 ms. With the v1 recipe alone, `Transport_is_serialized` failed with Expected Applied / Actual Ignored. Adding only `StreamStart = 20 s` to `Normal` made it pass, with the same probe and a fresh build. So the 400 ms StreamStart timer, not the reply deadline, was what failed that test once replies were no longer the limit. `Accepted_playlist…` explicitly keeps `Fast` (400 ms) and still passes in 0.52 s, so its intentional stream-deadline contract is preserved.

## Is the probe appropriate acceptance evidence?

- For the reply and StreamStart margins in the setup and `Transport_is_serialized`: yes, as a diagnostic that isolates which fixture limit fails when the peer is slow. It is not a performance requirement and no test asserts anything about response time because of it; it proves rig sensitivity only, not that CI scheduling caused the CI failure.
- For `Last_track_ending_stops_and_issues_no_repeat`: no. That test asserts that no request is sent after `PlayA` returns. `PlayA` (changed in v1 to wait on the queue) returns when three entries are queued, not when the apply has finished. A 700 ms-per-append peer pushes the apply's trailing request after the test samples `before`, giving 6 requests instead of 5. That tests the probe's artificial delay against the fixture's sequencing assumption, not the product behaviour (no repeat after the last track). I did not verify this by a further experiment and did not change the test: its assertion is untouched, and I added no wait or timing requirement. The failure is preserved here as evidence. Under normal timing the test passes in all 409/409 runs.
- Residual: if Last_track_ending ever fails in CI with an extra request, the cause would be this sequencing sensitivity and would need a bounded follow-up by the Coordinator. Independent review should judge whether the evidence is adequate.

## Existing short limits

The short limits are pre-existing fixture values (MpvTimeouts `Fast`), not new response-time tests. This work only raises positive-path allowances to the shipped values and keeps the two tests that explicitly test a deadline (`Uncertain_outcome…`, `Accepted_playlist…`), plus all MpvTests, unchanged.

## Failed and invalid attempts

None invalid. The only failing results are the probe failures recorded above (all before the probe was removed).

## Handoff validation

Validator result is returned in the terminal handoff.
