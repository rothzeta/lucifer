task: WORK-21-ci-fixture
status: complete
outcome: Recipe steps 1-4 implemented test-only in the two fixtures. The TransportControl delayed-peer probe fails before with the CI message and passes after. The AlbumPlayback probe fails before but still fails after for a different reason (see discoveries), so it is not a pass-after proof. Full suite 409/409 twice and four shell suites exit 0. Review and delivery pending.
artifacts:
  - docs/coordination/install-update/reports/ci-fixture.md
  - docs/coordination/install-update/assignments/ci-fixture-v1.md
  - "branch ruach/lucifer-ci-fix, product commit ab203edededeb5829a065a799d6c895d67962f02"
verification:
  - "Probe before (original fixtures + 700 ms append delay, fresh build exit 0): TransportControlTests.Previous_within_three_seconds x3 failed with 'album load incomplete (mpv did not reply to loadfile within 0.5s.)' from Rig.Start (the CI message); Album Transport_is_serialized and Last_track_ending also failed (Collections differ at PlayA, queue empty after the 0.3 s reply deadline)"
  - "Probe after (recipe applied + same probe, fresh build exit 0): TransportControl x3 passed; Album Transport_is_serialized failed (Expected Applied, Actual Ignored) and Last_track_ending failed (Expected 5, Actual 6): 3 passed 2 failed"
  - "Probe removed (no PROBE text left); fresh build exit 0; explicit deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline: exit 0, 6/6, Uncertain_outcome 0.91 s"
  - "Fresh dotnet build exit 0 then dotnet test --no-build exit 0, 409/409, run twice on committed product commit; deploy/test-lucifer-{maint,command,install,bootstrap}.sh each exit 0"
  - "Not run: optional bounded contention step, stress, real CI; no remote actions"
review: not-run
discoveries:
  - "Album probe after the recipe fails with Ignored/6 requests, not the reply-deadline message. Likely cause (unverified): the probe's 700 ms per append over three loads exceeds the 400 ms StreamStart timer (plan residual R2), a separate mechanism from the reply margin; the 700 ms probe is larger than any normal delay. No assertion was weakened and no D1 StreamStart change was made; Coordinator decides whether R2 needs evidence"
  - "Probe-before Album failure shows the queue emptied by the 0.3 s reply deadline, matching original CI failure 1 shape, not the 0.3 s message text the plan predicted"
blockers: []
baseline: f350eea516b5e40a2c6d985bede3d130dc359dc3
candidate_revision: ab203edededeb5829a065a799d6c895d67962f02
tested_revision: ab203edededeb5829a065a799d6c895d67962f02

# CI fixture margins report

Source: plan pin f350eea (ci-fixture-plan.md). Product commit `ab203edededeb5829a065a799d6c895d67962f02` on ruach/lucifer-ci-fix, base f27958c plus plan docs. Only Amp.Nfc.Tests/TransportControlTests.cs and AlbumPlaybackTests.cs changed; `git diff HEAD~1 HEAD` is 10 insertions/4 deletions across those two files. No production, MpvTests, PlaySessionTests, FakeMpvPeer, workflow or deploy change.

## Recipe outcomes

1. TransportControlTests `Fast.Reply` 500 ms -> 5 s with a one-line comment; Connect/Send/StreamStart unchanged.
2. AlbumPlaybackTests: `Fast` unchanged (300 ms); added `Normal = Fast with { Reply = 5 s }`; `BuildAsync` default `timeouts ?? Normal`.
3. `Uncertain_outcome…` now `BuildAsync(timeouts: Fast)`: still 300 ms (0.91 s runtime, not about 15 s).
4. `Transport_is_serialized…`: `await transport;` -> `Assert.Equal(TransportOutcome.Applied, await transport);` (strengthened).

Unchanged explicit deadlines: Uncertain_outcome 300 ms, all MpvTests deadline tests, Accepted_playlist StreamStart 400 ms, the three-second boundary assertions (3.0, 2.9, 0.0, 3.5 s) and the stall/serialization assertions.

## Probe procedure (temporary, not committed)

Line `if (req.Command[2].GetString() == "append") await Task.Delay(700);` added to the loadfile case of both rig handlers (one script adds and removes it; removal verified by grep), each step preceded by a fresh `dotnet build` with exit 0 before any `--no-build` test. Logs/TRX/exit codes are in the session scratchpad (v3 directory); exit statuses are from files.

Results are in the leading block. Summary: TransportControl fail-before (CI message) / pass-after is demonstrated for all three cases. The Album probe does not show pass-after: after the recipe it fails with a different symptom, reported rather than weakened. The 700 ms delay is only a fixture instrument; it proves rig margin sensitivity for the Transport rig, not that CI scheduling caused the failure.

## Invalid attempts

None in this unit.

## Residuals

- R1 (MpvTests share 300 ms Fast with deadline tests) and R2 (AlbumPlayback StreamStart 400 ms real-time timer; likely cause of the Album probe-after failures, unverified) remain; D1 was not adopted.
- Optional bounded contention step 4 was not run. Real CI is the remaining proof; a further different-mechanism CI failure would be a new escalation, not another margin patch.

## Handoff validation

Validator result is returned in the terminal handoff.
