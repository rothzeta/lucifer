---
task: install-update-work-01-review-fixes-v3
role: implementer
assignment: docs/coordination/install-update/assignments/fix-review-v3.md
status: complete
outcome: B1 fixed; O2, O3, N1, N2, N4 addressed with regressions or documentation; isolated suites, syntax and packaging pass; reverification, re-review and physical acceptance are not claimed
candidate_revision: 7fe6ddfbbf7a9165dcbe2e6c7a938daf19ab5d74
tested_revision: 7fe6ddfbbf7a9165dcbe2e6c7a938daf19ab5d74
artifacts:
  - deploy/lucifer-install
  - deploy/test-lucifer-install.sh
  - docs/install/INSTALL.md
  - docs/coordination/install-update/reports/review-fixes-v3.md
  - branch ruach/lucifer-iu-impl
verification:
  - "git merge --ff-only c11d21f403959e8a0ef977a81617e2b2b9cfb60a: fast-forward from 0329859; prior reports and assignments unchanged"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0"
  - "deploy/test-lucifer-maint.sh: rc 0, 'lucifer-maint: ok'"
  - "deploy/test-lucifer-install.sh at 7fe6ddf under timeout: rc 0, 'lucifer-install: ok', no FAIL lines (earlier groups retained plus B1, O2, O3, N1/N2 groups)"
  - "Mutation checks, each run against the suite: removing the disable undo (6 failures), removing the stop undo (6), keeping the marker past the switch (2), deleting instead of keeping stale txn files (1), taking the lock before the installation check (1), dropping the tmpfiles re-apply (2); original restored (cmp identical) and passing. One first O2 mutant failed to apply and was redone"
  - "publish.sh --rid linux-arm64 and --rid linux-arm --version ci into OS temp: rc 0 each, sha256sum -c OK; packaged bin/lucifer-install byte-identical to deploy/lucifer-install; extracted --help identical; arm package lists executable bin/lucifer-install; no snapshot left in /var/tmp"
  - "dotnet build and dotnet test not rerun: git diff c11d21f..7fe6ddf touches only deploy/lucifer-install, deploy/test-lucifer-install.sh and docs/install/INSTALL.md; no Amp.Nfc or Amp.Nfc.Tests file differs; reused evidence: build 0 warnings 0 errors, 408 tests passed"
  - "Process: one earlier background mutation run was waited on by polling for the success marker; it finished normally (rc 0). Subsequent waits used bounded timeouts and exit statuses"
  - "Not run: real host installation, accounts, systemd, Pi, real lucifer-maint check, shellcheck, disk-full"
discoveries:
  - "Integration: single branch ruach/lucifer-iu-impl; sources c11d21f (fast-forward) plus product commit 7fe6ddf; no conflicts"
  - "The product tree outside deploy and docs/install is unchanged since c96dc4a"
  - "The pending marker is removed right after the atomic current switch, so the release is never treated as a leftover once live; in-process failures still discard it through the transaction"
  - "Stale transaction directories of killed runs are moved to /opt/lucifer/backups/interrupted-* by the next install or update instead of being left in place or deleted"
  - "Commits carry no co-author trailer, per the assignment"
blockers: []
---

# Review fixes v3 report

## Dispositions

- **B1 fixed.** The transaction records whether the service was enabled and active, and whether this run enabled or started it. On failure, while the unit still exists, it stops what it started (and clears failed state), disables only an enable it added, then restores link, unit and tmpfiles, reloads systemd and restarts a previously active service. Regression cases for a failing start and a failed settle check, each from an originally disabled and an originally enabled state, assert: exit 1, not active, enable state equals the initial one, stop and disable calls precede the final daemon-reload, unit, link and release gone, no secret printed; then a plain install with the placeholder configuration neither enables nor starts and leaves the prior enable state alone.
- **O2 fixed.** The `.pending` marker is deleted immediately after `current` switches, so a release that was ever live cannot be removed by cleanup. Regression: real SIGKILL after the switch (hang at `start`), two manual rollbacks, then an update to a new version; 1.0.0, 1.1.0 and 1.2.0 all remain. The earlier retry tests for genuinely incomplete releases (marker present, not current) still pass.
- **O3 addressed.** The guard message now points to INSTALL.md "After a crash". INSTALL.md documents the practical recovery: same-version retry or `--adopt` (with what the backup contains), where the pre-run unit is kept, and that a stopped service must be restarted by hand. Stale `.txn.*` directories are kept under `backups/interrupted-*`. Regression with a real SIGKILL during `daemon-reload`: different version refused with the hint, same-version retry completes, saved unit kept, service not restarted (documented). No crash-atomic restoration is claimed.
- **N1 fixed.** `update` and `rollback` refuse before creating any host path and re-check under the lock. Regression asserts the whole host root stays empty and no host command ran.
- **N2 fixed.** A restored tmpfiles snippet is re-applied with `systemd-tmpfiles --create`; regression counts and orders the two runs after a failed update.
- **N4 documented.** INSTALL.md states the 3 s settle check only catches immediate failures.
- N3 left alone as instructed (the unused variable was removed incidentally with no behaviour change).

## Limits

Physical acceptance is unverified; stubs are not physical evidence. Hard kills and power loss are recovered by documented steps, not automatically. The snapshot directory from a killed run (O1) remains until the temporary-file reaper removes it. Reverification and independent re-review are required; no acceptance is claimed.
