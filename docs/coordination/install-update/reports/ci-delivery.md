task: WORK-19-ci-delivery
status: blocked
outcome: "Reviewed fix delivered: local merge and non-force push to GitHub master done. The new master CI run 39485124360 FAILED on one different test (TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(played: 3)); the two original CI failures did not recur. No retry or fix attempted per assignment."
artifacts:
  - docs/coordination/install-update/reports/ci-delivery.md
  - docs/coordination/install-update/assignments/ci-delivery-v1.md
  - https://github.com/rothzeta/lucifer/actions/runs/39485124360
  - "local branch ruach/lucifer-install-update merge e8181b8e1effc3cf003c11f89e62054f8fbe64c5"
verification:
  - "Preconditions before mutation: target clean at 7eb85a3, branch ruach/lucifer-install-update; origin master af61d1a; tags v0.1.0 and v0.1.1 as expected; /opt/dev/lucifer pr-1 3f09c09, local master 38c01fe; d665364 descends from reviewed 8cca117; af61d1a ancestor of target"
  - "git diff 04d88c5 HEAD outside docs/coordination/install-update is empty at the merge: product byte-identical to tested revision"
  - "git push origin e8181b8e1effc3cf003c11f89e62054f8fbe64c5:refs/heads/master (no force) exit 0, af61d1a..e8181b8; live ref equals merge; tags unchanged; local master and /opt/dev/lucifer unchanged"
  - "CI run 39485124360 (workflow ci, event push, branch master, headSha e8181b8e1effc3cf003c11f89e62054f8fbe64c5, run_attempt 1): build-test failure; steps Set up, checkout, setup-dotnet, Build success; Test failure; maint, command, install, bootstrap and Package steps skipped; Passed 408, Failed 1, Total 409"
  - "No local tests rerun (exact-code checks reused from review)"
review:
  - "Independent re-review accepted 8cca117 (reports/ci-review-v2.md); the delivery itself is not reviewed"
discoveries:
  - "Failing test message: Amp.Nfc.MpvException : album load incomplete (mpv did not reply to loadfile within 0.5s.); playback stopped, from Rig.Start (TransportControlTests.cs:66) via Mpv.LoadAlbumAsync (Mpv.cs:89) / ReplaceQueueAsync (Mpv.cs:109,117), inner SendAsync Mpv.Ipc.cs:61. It passed in the earlier tag CI and in all local runs here, so it looks like a separate load-sensitive 0.5s reply deadline in test setup, not one of the fixed causes; this is unverified"
  - "The earlier two failures (AlbumPlayback Invalid_or_unmapped_tag, PlaySession Pending_work) passed in this run"
  - "Historical immutable tag runs 39350436694 and 39350436703 stay failed; no tag or release created"
blockers:
  - "Master CI is red on TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(played: 3). Needs a Coordinator decision on a further bounded fix (new authority/assignment); no retry or fix done here"
destination_before: 7eb85a3e8590294f8420e441c7655e3922c30445
candidate_revision: 8cca117cdf30a7a7e34a74c4c10708da35cb164b
tested_revision: 04d88c54d0cf2c2afbc3c6010490dd0375886b66
reviewed_revision: d665364c1466ce0650286082403953d4db5b09c3
delivered_revision: e8181b8e1effc3cf003c11f89e62054f8fbe64c5

# CI delivery report

## Revisions and states

- Reviewed pin 8cca117 (tested product 04d88c5); evidence successor d665364 merged unchanged (non-fast-forward merge, no conflicts, neutral identity, no trailers) into the frozen local target. Delivered merge: `e8181b8e1effc3cf003c11f89e62054f8fbe64c5` (parents 7eb85a3, d665364). The worker branch ruach/lucifer-ci-fix was fast-forwarded to d665364 first.
- Published: `e8181b8e1effc3cf003c11f89e62054f8fbe64c5` pushed to origin refs/heads/master as a fast-forward. Remote before af61d1a609533c7ed1bfdd3d66bedf7a3d9ed6a2, after e8181b8e1effc3cf003c11f89e62054f8fbe64c5. No other refs pushed.
- Unchanged and verified after push: origin tags v0.1.0 (2bd917a, ^{} 91c264d) and v0.1.1 (d7e45fa, ^{} af61d1a); /opt/dev/lucifer pr-1 3f09c09; local master 38c01fe.
- Conflicts: none. Equivalence: product outside docs/coordination/install-update is identical to 04d88c5.

## CI observation

Run https://github.com/rothzeta/lucifer/actions/runs/39485124360, attempt 1, workflow ci, push to master at e8181b8e1effc3cf003c11f89e62054f8fbe64c5. Job build-test (ID 118843434563) concluded failure at the Test step; Build succeeded; the four shell-suite steps and the Package step were skipped because Test failed. Total 409, passed 408, failed 1, duration 12 s.

Complete failing output (from `gh run view --log-failed`, unfiltered 29 lines, preserved outside the repository in the session scratchpad):

```
[xUnit.net 00:00:02.02]     Amp.Nfc.Tests.TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(played: 3) [FAIL]
  Failed Amp.Nfc.Tests.TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(played: 3) [1 s]
  Error Message:
   Amp.Nfc.MpvException : album load incomplete (mpv did not reply to loadfile within 0.5s.); playback stopped
---- Amp.Nfc.MpvException : mpv did not reply to loadfile within 0.5s.
  Stack Trace:
     at Amp.Nfc.Mpv.ReplaceQueueAsync(IReadOnlyList`1 urls) in Amp.Nfc/Mpv.cs:line 117
   at Amp.Nfc.Mpv.LoadAlbumAsync(IReadOnlyList`1 urls, CancellationToken generation) in Amp.Nfc/Mpv.cs:line 89
   at Amp.Nfc.Tests.TransportControlTests.Rig.Start() in Amp.Nfc.Tests/TransportControlTests.cs:line 66
   at Amp.Nfc.Tests.TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(Double played) in Amp.Nfc.Tests/TransportControlTests.cs:line 252
----- Inner Stack Trace -----
   at Amp.Nfc.Mpv.SendAsync(Object[] command, Action`1 onReply, CancellationToken cancellationToken) in Amp.Nfc/Mpv.Ipc.cs:line 61
   at Amp.Nfc.Mpv.ReplaceQueueAsync(IReadOnlyList`1 urls) in Amp.Nfc/Mpv.cs:line 109
Failed!  - Failed:     1, Passed:   408, Skipped:     0, Total:   409, Duration: 12 s - Amp.Nfc.Tests.dll (net10.0)
```

Interpretation (unverified): the failing line is test rig setup that uses a 0.5 s reply deadline; it is outside the code and tests changed by this work. Whether the Dispose or ownership changes influence it was not investigated.

## Limits

No retry, rerun, workflow dispatch, tag, release, or app edit was made. The later evidence commit (this report) is not pushed, so no second CI run is triggered. No host, Pi or physical claims.
