---
task: install-update-work-02-verification-v4
role: implementer
assignment: docs/coordination/install-update/assignments/verify-v4.md
status: complete
outcome: Review-fix candidate verified; B1, O2, O3 recovery, N1 and N2 independently confirmed fixed with no blocking findings; two low residual observations; physical acceptance unverified; no review acceptance asserted
candidate_revision: 43a05f71073c12d46817a78611ff7fe1dbf2a531
tested_revision: 43a05f71073c12d46817a78611ff7fe1dbf2a531
artifacts:
  - docs/coordination/install-update/reports/verification-v4.md
  - docs/coordination/install-update/reports/verification.md
  - docs/coordination/install-update/reports/reverification-v3.md
  - docs/coordination/install-update/assignments/verify-v4.md
  - branch ruach/lucifer-iu-verify
verification:
  - "git merge --ff-only 43a05f7 from the v3 reverification commit: fast-forward; HEAD equals tested_revision for every run; no product, test or earlier report edited. Product change in this step is 7fe6ddf (deploy/lucifer-install, deploy/test-lucifer-install.sh, docs/install/INSTALL.md); the pin otherwise adds review/fix reports and assignments"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0 (shellcheck not installed: not run)"
  - "timeout 120 deploy/test-lucifer-maint.sh: rc 0, 'lucifer-maint: ok'"
  - "timeout 600 deploy/test-lucifer-install.sh: rc 0, 'lucifer-install: ok', 20 ok groups, 0 FAIL"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version ci rerun at this pin (cheap, and the installer is packaged by copy): rc 0 each; sha256sum -c rc 0; 218 and 217 plain files, 9 dirs each; bin/lucifer-install 0755 root-owned; packaged script byte-identical to deploy/lucifer-install; extracted --help identical to source --help"
  - "dotnet build / dotnet test not rerun: git diff 0329859..43a05f7 outside deploy/lucifer-install, deploy/test-lucifer-install.sh, docs/install/INSTALL.md and docs/coordination is empty, and no Amp.Nfc or Amp.Nfc.Tests file differs anywhere in the task diff; reused exact-code evidence from verification reports v2/v3: build 0 warnings 0 errors, 408 passed"
  - "Real archives, both RIDs, current installer, stub root: install rc 0, no enable or start, repeat install rc 3, arm64 archive on armhf host rc 1, TMPDIR empty afterwards"
  - "Independent probes of changed contracts (disposable script, stubbed host commands, isolated TMPDIR and temp roots, real SIGKILL via hanging stubs, every wait bounded at 8 s and whole script under timeout 300): 30 PASS, 0 FAIL, script rc 0. First-round and v3 probe scripts rerun as regression: 29 PASS, 0 FAIL, rc 0"
  - "B1 confirmed fixed: install --start with a failing start and with a start that returns but leaves the unit inactive (settle failure), each from initially disabled and initially enabled state: rc 1, service not active, enable state equals the initial one (disabled stays disabled; enabled stays enabled and no disable is issued), unit, current link and release removed, password not printed. From the disabled state the stop and disable calls precede the final daemon-reload. A following plain install with a placeholder password exits 0, issues no start, enable or disable, leaves the enable state as it was and prints the NOT started instructions. Successful --start still enables and starts. An update with an active service and a failing first start rolls back, keeps the service enabled, restarts it and issues no disable"
  - "O2 confirmed fixed: after a SIGKILL following the switch the pending marker is already gone; two manual rollbacks plus an update to a new version retain 1.0.0, 1.1.0, 1.2.0 and 1.3.0. The earlier probe that deleted the once-switched release now retains it. Incomplete retries still work: SIGKILL before the switch leaves marker and old current; retrying the same version succeeds, keeps the old release as previous and leaves no hidden leftovers. An unmarked existing release directory is never deleted (exit 3)"
  - "O3 documented recovery confirmed: after SIGKILL during daemon-reload the old release is current and usable, the new unit is installed and the service is stopped; a different-version update is refused with exit 3 and the message names INSTALL.md After a crash; rollback is refused exit 3; the pre-run unit and tmpfiles survive in the .txn directory after a refused run; the same-version retry succeeds, moves them to backups/interrupted-* with exact pre-run content, and does not restart the service (documented); the --adopt path to a different version succeeds and backs up the interrupted package's unit; backups/interrupted-* and the adopt backup survive a later update"
  - "N1 confirmed fixed: update and rollback on an empty root exit 3 with an empty root (find reports nothing), no host command and no snapshot left. N2 confirmed fixed: a failed update restores tmpfiles byte for byte and runs systemd-tmpfiles --create twice (activation, then restore), the restore run before the final daemon-reload"
  - "Not run: any real installation, real systemd, accounts, Pi, audio or mpv; real lucifer-maint check on target binaries; systemd-analyze verify (unit file unchanged); shellcheck; disk-full behaviour"
discoveries:
  - "R1 (low, residual O1): a SIGKILL still leaves the private snapshot lucifer-install.XXXXXX in TMPDIR (mode 0700, public package only); later runs do not remove it. Acknowledged as a documented limit by the implementer"
  - "R2 (low, documentation precision): INSTALL.md After a crash step 3 says the pre-run unit and tmpfiles are kept in backups/interrupted-*. That holds only after a successful retry; after a refused different-version run they are still in /opt/lucifer/.txn.* (preserved, not deleted). No data is lost either way"
  - "Probe-harness notes: my first draft of two probes was wrong (a persistent start failure also failed the restore restart; an O3 expectation assumed the move to backups happens on a refused run; a duplicate update). I corrected the probes, not the product, and the results above are from the corrected runs"
  - "Exact-code reuse is justified: the application and test projects are unchanged since the earlier full dotnet runs; packaging and help were executed at this pin"
blockers: []
---

# Verification report v4: install and update (WORK-02)

Verification only at pin `43a05f7` (product `7fe6ddf`). Earlier reports `verification.md` and `reverification-v3.md` are preserved unchanged. All installer runs used temporary roots, an isolated `TMPDIR` and stubbed host commands, so no host account, unit or installation was touched. Stubs are not physical evidence. This report does not assert independent review acceptance.

## Acceptance matrix

| Item | Result | Evidence |
|---|---|---|
| Syntax, help, invalid arguments | pass | bash -n; suite; help parity in both packages |
| First and repeat install, existing installation | pass | suite; real archives both RIDs; repeat rc 3 |
| Update success, ordering, inactive service | pass | suite (staged check before stop; no start for an inactive service) |
| Checksum, RID, unsafe or malformed archives, snapshot | pass, no host effects | suite; regression probes |
| Config, catalogue, input map, prior releases preserved | pass | suite hashes; completed releases retained after kill and rollbacks |
| Placeholder config leaves service stopped, restrictive secrets | pass | suite; probes; failed --start no longer leaves the unit enabled |
| Preflight before playback stop; atomic current | pass | suite ordering; stubs |
| Failure restore of unit, support files, enable and active intent | pass | B1 four-way matrix; N2 tmpfiles re-apply; update failure keeps service enabled |
| Rollback, hand-edit refusal, adopt | pass | suite; probes |
| Crash handling | documented recovery works; atomic crash recovery not claimed | O2/O3 probes |
| No-install update/rollback | zero host effects | N1 |
| Package includes new commands; help matches docs | pass | both RIDs |
| Build and tests | pass by exact-code reuse | application unchanged since the 408-test runs |
| Physical acceptance | not verified | no Pi or real systemd |

## Remaining issues

No blockers. R1 and R2 are low and non-blocking. No known violation of the owner acceptance is being accepted: the only open gap is physical acceptance, which remains explicitly unverified.
