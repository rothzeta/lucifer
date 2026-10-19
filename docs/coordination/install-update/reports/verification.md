---
task: install-update-work-02
role: implementer
assignment: docs/coordination/install-update/assignments/verify-v1.md
amendment: docs/coordination/install-update/assignments/verify-pin-v2.md
status: needs-decision
outcome: Combined candidate passes its own suite, build, tests and package checks and most independent probes; one behavioural defect (rollback overwrites a hand-edited unit without refusal or backup) and three lesser findings need a Coordinator decision; physical acceptance unverified
candidate_revision: e2790b6fb295818d2de604760b0ef1a94a825dc8
tested_revision: e2790b6fb295818d2de604760b0ef1a94a825dc8
artifacts:
  - docs/coordination/install-update/reports/verification.md
  - docs/coordination/install-update/assignments/verify-v1.md
  - docs/coordination/install-update/assignments/verify-pin-v2.md
  - branch ruach/lucifer-iu-verify
verification:
  - "git merge --ff-only e2790b6: fast-forwarded from ac9e415; no source, test or report edited; HEAD = tested_revision for every run below"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0 (shellcheck not installed: not run)"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok"
  - "deploy/test-lucifer-install.sh: exit 0, 'lucifer-install: ok', 12 ok groups, 0 FAIL (reviewed its source: asserts effects, call ordering via stub call log, tree hashes and modes, not function mirrors)"
  - "dotnet build: 0 warnings, 0 errors; dotnet test: 408 passed, 0 failed (run fresh at this revision)"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version ci: both rc 0; sha256sum -c OK for both; both archives hold executable root-owned bin/lucifer-install, only plain files and directories (218 and 217 files, 9 dirs); extracted --help identical to deploy/lucifer-install --help; packaged script byte-identical to deploy/lucifer-install"
  - "Real archives, both RIDs, installed into a stub root: rc 0, env and catalogue 0640, no start or enable, no setuid or world-writable member, repeat install rc 3; arm64 archive on a stubbed armhf host rejected (rc 1)"
  - "Independent probes (disposable script, stubbed host commands, temp roots): PASS setuid/setgid/world-writable members normalised; hostile env lines ($(...), backticks) never executed or printed; newline-in-name, FIFO, block device, symlinked directory, absolute hardlink and duplicate VERSION archives rejected with zero host effects; update failure after activation restores link, exact unit and tmpfiles, active state, removes failed release and leaves no .txn/.new files; concurrent run refused while lock held"
  - "Probe FAIL (finding F1): rollback over a hand-edited unit"
  - "Probe FAIL (finding F2): SIGTERM during update"
  - "Probe FAIL (finding F3): placeholder detection"
  - "Probe FAIL (finding F4): extraction failure after host effects"
  - "Not run: any real installation, real systemd, accounts, Pi, audio or mpv; real lucifer-maint check (arm64/arm binaries are not executable on this host, the stub is used); systemd-analyze verify (unit unchanged); shellcheck"
discoveries:
  - "F1 (recommend fix before delivery): `rollback` does not call guard_manual_files, so a hand-edited /etc/systemd/system/lucifer.service or tmpfiles snippet is silently replaced with the target release's copy and no backup is kept. Repro: install 1.0.0, update to 1.1.0, append a line to the installed unit, `rollback` -> rc 0, edit gone, /opt/lucifer/backups empty. `update` refuses the same situation with exit 3 and --adopt keeps a backup. INSTALL.md says rollback uses the same transaction; the guard is part of update only. Proposed regression for Implementer: rollback with edited unit exits 3 unchanged, and with --adopt (needs parsing for rollback) keeps a backup"
  - "F2 (low): on SIGTERM during activation the transaction restores current, unit and active state correctly, but on_signal does not remove the new releases/<release> directory (the EXIT stage cleanup only covers the hidden staging dir). Repro: stub daemon-reload hangs, send SIGTERM to update 1.1.0 -> rc 1, current and unit restored, service active, releases/lucifer-1.1.0-linux-arm64 left behind. A retry of the same version is then refused with exit 3 (already present) until it is deleted by hand. Same leftover is possible for a SIGKILL or power loss"
  - "F3 (low): `placeholder_config` only matches the exact line NAVIDROME_PASSWORD=change-me. With CRLF, trailing space, or no NAVIDROME_PASSWORD line, `install --start` treats the config as real and enables/starts the service; install mode also never runs lucifer-maint check (only update and rollback do). Without --start nothing is started, so default behaviour is safe; a start on an unusable config would fail the settle check and be restored"
  - "F4 (low): the member-name check does not bound component length. A 300-character member passes validation, then tar fails during extraction after account, directories and config dirs were created: exit 2 (documented as usage) with a half-prepared host (no staging leftover, no current). Extraction is also a second read of the archive after the checksum (TOCTOU for a writable archive path); not exploited here"
  - "Process note: my first probe stub exited 0 after the case statement, making is-active always true; I fixed the stub and reran all probes. Results above are from the corrected run. The probe P6 'emptytop' case was my own malformed fixture (a valid package plus a duplicate directory entry) and is not a finding"
  - "The implementation report's claims were rechecked, not accepted: suite, build, tests, packaging and help parity were re-executed at the tested revision"
blockers:
  - "Coordinator decision: whether F1 must be fixed by WORK-01 before review/delivery (recommended; it contradicts the documented rollback safeguards and can discard operator edits) and whether F2-F4 are fixed or recorded as limitations"
---

# Verification report: install and update (WORK-02)

## Scope and method

Verification only, at pinned candidate `e2790b6` (product `697b072`, plus docs-only commit). All installer runs used `LUCIFER_INSTALL_ROOT` temporary roots and stubbed `id`, `getent`, `useradd`, `usermod`, `chown`, `dpkg`, `systemctl`, `systemd-tmpfiles` and `runuser`; no host account, unit or installation was touched. Disposable harness, probes and logs are in the session scratchpad, not in the repository. Stubs are not physical evidence; the checksum only detects corruption, not publisher identity.

## Acceptance matrix

| Item | Result | Evidence |
|---|---|---|
| Syntax, help, invalid arguments | pass | bash -n; candidate suite (exit 2 cases, zero host effects) |
| First and repeat install | pass | candidate suite; real archives both RIDs (repeat rc 3, tree unchanged) |
| Success update; ordering | pass | candidate suite: staged check < stop < daemon-reload < start; no start for inactive service |
| Checksum failure | pass | mismatch, missing file, file named for another archive: rc 1, no effects |
| Wrong userland RID | pass | arm archive on arm64 and arm64 on armhf rejected, no effects |
| Traversal, absolute, symlink, hardlink, device/FIFO, odd names, outside top dir, missing members | pass | candidate suite plus my probes: rejected with zero host effects. Exception F4 (over-long name fails later) |
| Config, catalogue, input map, prior releases preserved | pass | byte hashes unchanged across install over existing config and update; env mode tightened to 0640 root:lucifer |
| Placeholder config leaves service stopped with instructions | pass for default path | F3 for non-exact placeholder with --start |
| Secrets and executables | pass | env/catalogue 0640; hostile env never sourced or printed; no setuid/world-writable survives; releases chowned root |
| Preflight and staging before playback stop | pass | staged `lucifer-maint check` against live env precedes `systemctl stop`; failure leaves tree and service untouched |
| Atomic current activation | pass (static and stub) | symlink created then `mv -T`; link never absent in the stub tree after failure |
| Activation-failure rollback restores unit/support/state | pass | start failure, daemon-reload failure, first-install failure: exact unit and tmpfiles, previous link, active state restored. Signal case: restored but F2 leftover |
| Practical explicit rollback | pass with F1 | rollback, named rollback, toggle, failure restore; hand-edited unit not protected (F1) |
| Package includes new commands; help matches docs | pass | both archives; `--help` identical; README/INSTALL usage lines match |

## Limitations

No real systemd, accounts, mpv, serial reader, audio or Pi were exercised. Real `lucifer-maint check` was not executed against the installer (stub only). TOCTOU between checksum and extraction is documented, not tested. No second verification/fix cycle was needed or used.
