task: WORK-18-ci-review-v2
role: reviewer
assignment: docs/coordination/install-update/assignments/ci-review-v2.md
status: complete
outcome: "Accepted for local and master delivery: B1 is resolved by the 5-line Mpv.Launch.cs change; the Dispose and PlayA fixes and all required test contracts hold on a fresh build of 04d88c5, idle and under bounded owned load; no blocking findings. The documented path-replacement residual predates this change and is not widened by it"
baseline: f2faeb0df2fff7a6c1bdd994dda6513d45cf3c28
prior_review_revision: a05055950b345e163124faf84a5d48ed6894c4f0
candidate_revision: 8cca117cdf30a7a7e34a74c4c10708da35cb164b
reviewed_revision: 8cca117cdf30a7a7e34a74c4c10708da35cb164b
tested_revision: 04d88c54d0cf2c2afbc3c6010490dd0375886b66
artifacts:
  - docs/coordination/install-update/reports/ci-review-v2.md
  - docs/coordination/install-update/assignments/ci-review-v2.md
  - docs/coordination/install-update/reports/ci-review-v1.md
  - docs/coordination/install-update/reports/ci-fixes-v2.md
  - branch ruach/lucifer-ci-review
verification:
  - "git merge --ff-only 8cca117 from a050559: fast-forward; product delta a050559..04d88c5 is Amp.Nfc/Mpv.Launch.cs +5 only; git diff ec924cb 04d88c5 -- Amp.Nfc.Tests .github deploy is empty"
  - "Fresh build: git archive 04d88c5 into empty scratch dir (no bin/obj), Mpv.Launch.cs byte-identical to the commit; dotnet build exit 0; dotnet test --no-build exit 0, Passed 409 Failed 0 (TRX saved)"
  - "TRX outcomes Passed: Concurrent_dispose, Start_adopts, Pending_work_that_ignores, Invalid_or_unmapped_tag (both cases), Socket_served_by_another_live_instance, Stale_socket_file, Cancelling_startup, Child_that_exits_early, Connecting_to_a_socket_nobody_serves"
  - "deploy/test-lucifer-{maint,command,install,bootstrap}.sh on the fresh copy: each exit 0"
  - "Owned load (24 busy loops, PIDs created/killed/waited by script), fresh 04d88c5 binaries, filter Concurrent_dispose|Start_adopts|Pending_work|Invalid_or_unmapped_tag|Socket_served_by_another|Stale_socket_file: 10 of 10 runs exit 0, 7/7 passed each; same condition failed 2 of 4 and 2 of 6 runs in review v1"
  - "Reused, not rerun: v1 baseline f2faeb0 load failures, v1 old-Dispose regression proof (3/3 fail), v1 CI package step exit 0 (packaging inputs unchanged since ec924cb)"
  - "Handoff validator: see Handoff validation section"
review:
  - "Independent re-review by Reviewer; B1 resolved; accepted"
discoveries:
  - "Implementer's invalid stale-binary attempts were excluded from consideration; acceptance rests on this review's own fresh-archive build and runs"
  - "Historical tag CI 39350436694 and release 39350436703 ran immutable v0.1.1 source and stay failed; only a new master CI on the corrected source can show the fix"
blockers: []

# CI fix re-review (WORK-18 amendment v2)

Fast-forwarded `ruach/lucifer-ci-review` from `a050559` (v1 blocking report, preserved unchanged) to `8cca117`. Combined product revision `04d88c5` = `ec924cb` (Dispose serialization, PlayA wait) + B1 fix. Review cycle 2 of the default 2; it ends in acceptance, so no escalation.

## B1 disposition: resolved

`Amp.Nfc/Mpv.Launch.cs:149-153` (after the connect at line 148): after `ConnectAndVerifyAsync` succeeds and before `Adopt`, a launched child (`ownership` not null) whose path now exists is marked owned. This closes the window shown in v1 (`ownsSocket=False` with child bound between the pre-connect sample and connect).

Contracts checked in code:

- `ConnectAsync` passes `ownership: null` (Mpv.Launch.cs:15); both ownership checks are guarded by `ownership is not null`, so a connection without a child never owns or deletes the socket.
- Live foreign socket: `RefuseLiveSocketOrRemoveStale` (line 31) still throws before any child is launched, so the new line is never reached for a live foreign socket (`Socket_served_by_another_live_instance_is_neither_taken_nor_deleted` passes).
- Failure cleanup: on a failed attempt the new line is not reached; `StartAsync` catch (line 67) still deletes only when `Owned`, which is set only by the unchanged pre-connect sample.
- The Dispose `_gate`/`_teardown` ordering and the PlayA `Eventually` wait are unchanged from v1, where both were assessed correct.

## Residual risk

Another process replacing the socket path after launch but before connect would be recorded as owned and deleted at Dispose. This is a baseline-known risk, not a new regression: the existing pre-connect sample already marked any file that appeared after launch as owned, so the new post-connect check covers the same class of window, a few instructions later. Closing it would need a design change (for example, socket identity by inode, or mpv-side handshake), which is outside the bounded CI scope. No finding.

## Findings

None blocking. None optional beyond v1's recorded out-of-scope note (100 ms `Rig.Tick` idle-wait flake class in other AlbumPlayback tests).

## Acceptance

Accepted for local and master delivery of `04d88c5` (via `8cca117`), under the separate delivery assignment. No tag, version, workflow rerun or remote write was done here. Existing tag runs remain historical failures; the remote target is a corrected master CI. Not verified: GitHub runner, real mpv, hardware, release workflow.

## Evidence location

Session scratch `v2/` (fresh archive `src`, `build.*`, `test.*`, `full.trx`, `sh-*.{out,err}`, `l2-fx-*.{out,err,trx}`); separate files per command, exit codes taken directly with no pipes. Scratch is disposable; results are recorded above.

## Handoff validation

Validator command and result are returned in the terminal handoff (embedding would be circular).
