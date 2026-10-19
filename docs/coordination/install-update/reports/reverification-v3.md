---
task: install-update-work-02-reverification-v3
role: implementer
assignment: docs/coordination/install-update/assignments/reverify-v3.md
status: complete
outcome: F1-F4 independently verified resolved and the combined feature re-verified at the pinned fix candidate with no blocking findings; three low observations recorded; physical acceptance unverified; no review acceptance asserted
candidate_revision: 0329859ad7326a6a214def0ca0cc7f9e92ea95ec
tested_revision: 0329859ad7326a6a214def0ca0cc7f9e92ea95ec
artifacts:
  - docs/coordination/install-update/reports/reverification-v3.md
  - docs/coordination/install-update/reports/verification.md
  - docs/coordination/install-update/assignments/reverify-v3.md
  - branch ruach/lucifer-iu-verify
verification:
  - "git merge --ff-only 0329859: fast-forward from the first verification report commit; HEAD equals tested_revision for all runs; verification.md and all product/test files untouched. Product revision c96dc4a is the only product change in the pin (deploy/lucifer-install, deploy/test-lucifer-install.sh, docs/install/INSTALL.md); the pin adds reports and assignments only"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0 (shellcheck not installed: not run)"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok"
  - "deploy/test-lucifer-install.sh: exit 0, 'lucifer-install: ok', 16 ok groups, 0 FAIL"
  - "dotnet build: 0 warnings, 0 errors; dotnet test: 408 passed, 0 failed. Rerun fresh at this pin although no Amp.Nfc or test project file changed, because it was cheap"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version ci rerun at this pin: rc 0; sha256sum -c OK for both; archives hold only plain files and directories (218 and 217 files, 9 dirs); bin/lucifer-install executable root-owned; extracted --help identical to deploy/lucifer-install --help; packaged script byte-identical. INSTALL.md rollback usage line and help both list `rollback [--adopt] [RELEASE]`"
  - "Real archives for both RIDs into a stub root with the current script: install rc 0, env and catalogue 0640, no setuid/world-writable member, no enable or start, repeat install rc 3, arm64 archive on armhf host rejected rc 1, private snapshot directory empty afterwards"
  - "Independent reprobe of changed contracts (disposable script, stubbed host commands, isolated TMPDIR and temp roots): 29 PASS, 0 FAIL. Regression run of my first-round probes: all pass except my own malformed 'emptytop' fixture (a valid package plus a duplicate directory entry, not a defect)"
  - "F1 resolved: rollback over an edited unit or tmpfiles snippet exits 3, tree hash unchanged, no systemctl stop, edit intact; `rollback --adopt` succeeds, keeps the edit in backups, restores the target release's unit"
  - "F2 resolved: SIGTERM during daemon-reload exits 1, tree hash identical to before, service active, no new release or marker left, retry succeeds. SIGTERM during the staged config check leaves no stage directory and no snapshot. SIGKILL before the switch: old current usable, marker left, rerun of the same version cleans the leftover and succeeds with the old release kept as previous. SIGKILL after the switch: both releases executable, rerun of the same version exits 3 without damage, a later update keeps the switched release and the prior one. An unmarked existing release directory is never deleted (exit 3)"
  - "F3 resolved: install --start refused with exit 3 and zero host effects (no /opt, no account, no systemctl) for password change-me with CR, change-me with trailing blanks, empty, blank, absent password line, and absent env file; a failing lucifer-maint check with a real password exits 1, no enable or start, current/unit/release removed, password not printed; a good config with CRLF runs the check, then enable, then start; default install starts nothing and prints the NOT started instructions"
  - "F4 resolved: a 300-character member and a truncated gzip are rejected with exit 1 and no /opt, /etc, /var or systemctl effects and an empty TMPDIR; replacing the source archive and checksum after the installer's verification step (sha256sum wrapper swap) still installs the verified 1.0.0 bytes; snapshot removed after install and update; unusable TMPDIR gives no host effects"
  - "Not run: any real installation, real systemd, accounts, Pi, audio or mpv; real lucifer-maint check (target-architecture binaries are not executed here, the stub is used); systemd-analyze verify (unit unchanged); shellcheck; disk-full behaviour of the snapshot"
discoveries:
  - "O1 (low): SIGKILL leaves the private snapshot lucifer-install.XXXXXX in TMPDIR (default /var/tmp), mode 0700, holding a copy of the archive and its extraction; later install/update runs do not remove it (observed: 1 directory still present after a following successful update). SIGTERM/INT/HUP/normal exits clean it. Disk hygiene only"
  - "O2 (low): clear_interrupted deletes a marked release that is neither current nor previous even if it had been switched to before the crash. Repro: update 1.1.0, SIGKILL an update to 1.2.0 after its switch (marker stays, current=1.2.0), `rollback 1.0.0`, `rollback 1.1.0`, `update 1.3.0` -> 1.2.0 removed with a warning. Requires a crash plus two manual moves away from the release; the surviving current and prior releases were preserved in every tested sequence"
  - "O3 (documented limit, not a regression): SIGKILL between unit installation and the symlink switch leaves the new unit installed with the old release current and the service stopped; the next run does not restore the old unit. Crash recovery is not claimed; simulated markers and kills prove the leftover cleanup and the survivability of current and prior releases, not full recovery"
  - "Snapshot consistency (F4) was shown by an in-test swap through a wrapper, not by a concurrent attacker; the checksum still proves corruption only, not publisher identity"
  - "Rationale for evidence: packaging help, extraction order and snapshot behaviour were tested at this pin; the unchanged-application build and tests were rerun rather than reused"
blockers: []
---

# Reverification report v3: install and update (WORK-02)

Verification only, at pin `0329859` (product change `c96dc4a`). The first report `verification.md` is preserved unchanged. All installer runs used temporary roots with stubbed host commands and an isolated `TMPDIR`; no host account, unit or installation was touched. Stubs are not physical evidence. This report does not assert independent review acceptance.

## Disposition of earlier findings

| Finding | Result | Evidence |
|---|---|---|
| F1 rollback overwrote hand-edited unit | resolved | refusal exit 3 with unchanged tree and no stop; `--adopt` keeps a backup; tmpfiles edit also refused; help and INSTALL.md list `rollback [--adopt]` |
| F2 signal leftover | resolved for signals; crash limits documented | SIGTERM restores and removes the new release and marker, retry works; SIGKILL leftovers are cleaned by the next install/update; current and prior releases stay usable in both kill points; unmarked directories are never deleted |
| F3 placeholder and start intent | resolved | six refusal shapes exit 3 before any host effect; check precedes enable and start; failing check restores with nothing enabled or started; default install starts nothing |
| F4 extraction after host effects, TOCTOU | resolved | long name and truncated stream rejected before effects; source swap after verification installs verified bytes; snapshot removed on success paths |

## Combined acceptance matrix (for review)

| Item | Result |
|---|---|
| Shell syntax, help, invalid arguments | pass (bash -n; suite; help parity with docs) |
| First and repeat install; existing installation | pass (suite; real archives both RIDs; repeat rc 3; manual current/unit refused) |
| Success update, ordering, inactive service | pass (suite: staged check before stop; no start for an inactive service) |
| Checksum failure, wrong RID | pass, no host effects |
| Unsafe archive entries and members | pass, rejected before host effects (suite plus first-round probes rerun) |
| Config, catalogue, input map, prior releases preserved | pass (suite hashes; env mode tightened to 0640) |
| Placeholder config leaves service stopped with instructions | pass, including non-exact placeholders |
| Secrets and executables | pass (0640 env, root-owned releases, no special bits, env never sourced or printed) |
| Atomic current, activation-failure restore, state restore | pass (suite and probes; signal paths restored) |
| Explicit rollback, hand-edit refusal and adopt | pass |
| Package contents and help | pass (both RIDs) |
| Build and tests | pass (0 warnings, 0 errors; 408 passed) |
| Physical acceptance | not verified; no Pi or real systemd involved |

## Remaining issues

No blockers. O1 to O3 are low-severity observations for Coordinator and Reviewer to accept or defer. No fix cycle beyond this one was used.
