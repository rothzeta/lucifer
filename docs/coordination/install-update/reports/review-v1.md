---
task: install-update-work-03-review-v1
role: reviewer
assignment: docs/coordination/install-update/assignments/review-v1.md
status: complete
outcome: Independent review of the full install/update change found one blocking defect (B1, a failed `install --start` leaves the service enabled and reports otherwise); O1-O3 adjudicated as non-blocking, with O2 and O3 raised above their reported severity; review acceptance not given; physical acceptance unverified
reviewed_revision: cc3eb692dbcdffe833f8295cba2643a6cdfa1ad8
baseline: 452637b20863d6db8f993bc8b1674aaa44534ad6
artifacts:
  - docs/coordination/install-update/reports/review-v1.md
  - docs/coordination/install-update/assignments/review-v1.md
  - branch ruach/lucifer-iu-review
verification:
  - "Full diff 452637b..cc3eb69 inspected (installer, test suite, publish.sh, CI, README, INSTALL.md, coordination records); `git diff c96dc4a cc3eb69` outside docs/coordination is empty, so the product tree reviewed equals product commit c96dc4a"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0. shellcheck not installed: not run"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok'"
  - "deploy/test-lucifer-install.sh in this checkout: exit 0, 16 ok groups, 0 FAIL, 'lucifer-install: ok'"
  - "deploy/publish.sh --rid linux-arm64 --version rv into OS temp: rc 0, sha256sum -c OK, 218 regular files + 9 dirs only, bin/Amp.Nfc, bin/lucifer-maint, bin/lucifer-install 0755 root-owned; packaged installer byte-identical to deploy/lucifer-install; packaged --help identical to source --help"
  - "Real arm64 archive installed into a stub root with the harness stubs: rc 0, env and catalogue 0640, no setuid/setgid/world-writable files, no enable/start, snapshot directory removed"
  - "Independent stub probes (harness prelude reused, isolated TMPDIR and roots, real SIGKILL via the hanging daemon-reload stub): B1, O1, O2, O3 and N1 reproduced as described below"
  - "Reused, not rerun: dotnet build (0 warnings) and dotnet test (408 passed) from reverification-v3 at 0329859; justified because no application or test project file differs between 0329859 and cc3eb69 and none in the whole task diff; linux-arm package evidence also reused from that report"
  - "Not run: real installation, accounts, systemd, Pi, audio/mpv, real lucifer-maint check on target binaries, systemd-analyze verify (unit file unchanged), disk-full behaviour"
discoveries:
  - "The review assignment file was untracked in the review worktree at start; it is committed unchanged with this report"
  - "O3 recovery in practice needs `--adopt` (or a same-version retry) plus a manual `systemctl start`; INSTALL.md section 8 does not say so"
blockers:
  - "B1 (blocking): deploy/lucifer-install txn_begin/txn_restore do not record or restore the enable state or stop a service the transaction started, so `install --start` whose start fails leaves lucifer enabled while printing that the host was restored; a later plain install then claims 'NOT started or enabled'. Fix and regression required before acceptance"
---

# WORK-03 independent review v1: install and update

Reviewed revision `cc3eb692dbcdffe833f8295cba2643a6cdfa1ad8`. Its product tree is identical to product commit `c96dc4a`. The scope is the whole task diff from `452637b`. The reports implementation.md, verification.md, fixes-v2.md and reverification-v3.md were read as context only. None of my findings depend on their conclusions. All probes ran against temporary roots with the suite's command stubs. Stubs are not physical evidence.

**Result: one blocking finding (B1). Review acceptance is withheld until B1 is fixed and reverified.** Everything else in the acceptance list held up under inspection and probing. That covers argument and RID validation before host changes, unsafe-entry rejection before extraction, the snapshot, root ownership and 0640 secrets, the env file never being sourced or printed, idempotent account and directories, preservation of config, catalogue and input map, placeholder instructions, the manual-install refusals, the staged check before stop, atomic link switching, in-process failure restoration, rollback and adopt, and help/docs parity. This is the first fix/review cycle for the review stage.

## Blocking finding

### B1: a failed `install --start` leaves the service enabled (and possibly cycling) while reporting restoration

- **Location:** `deploy/lucifer-install:292-302` (`txn_begin` saves `was_active` but not the enable state), `:310-324` (`txn_restore` never disables, and never stops when `was_active=0`), `:364` (`systemctl enable` inside the transaction), `:436` (message); `docs/install/INSTALL.md:58` ("…fails with the host restored (exit 1) and nothing is enabled or started").
- **Problem:** with `--start`, the step order is check, then `enable`, then `start`, then the settle check. If `start` fails, or the service is not active after the settle time, the restore removes the unit and the current link and deletes the release. It does not undo `enable` and does not stop the unit it started. The installer then prints "installation failed; the host was restored (no enable or start follows a failed check)".
- **Why it matters:** this breaks the requirements "exact restoration on failure" and "intended enable state respected", and contradicts INSTALL.md:58. On real systemd, the `multi-user.target.wants` symlink stays behind. After the unit is removed it dangles, and with `Restart=on-failure`/`RestartSec=5` a just-failed unit may still be scheduled to restart. When the owner retries with plain `install`, the unit file comes back and the dangling link becomes live, so the service starts at the next boot. That includes the case where the env file holds the placeholder password, which is exactly the restart loop the task was asked to avoid. Meanwhile the installer prints "the service was NOT started or enabled".
- **Evidence (stub probe):** a fresh root, a real password, `STUB_FAIL_START_FOR=lucifer-badstart-1-linux-arm64`, then `install --start badstart-1` gave rc 1. The calls log shows `systemctl enable lucifer` and `systemctl start lucifer`, with no `disable` and no `stop`. The stub's enabled marker is still present. I then reset the env to the placeholder and ran plain `install 1.0.0`: rc 0, output "the service was NOT started or enabled", stub state still enabled, 0 disable calls. The suite only covers a failing *check* under `--start` (`test-lucifer-install.sh` F3 group), never a failing *start*.
- **Direction:** record `was_enabled` in `txn_begin`. In `txn_restore`, stop the unit when it was not active before, disable it when it was not enabled before (or `reset-failed` as appropriate), and do both before the unit file is removed. Add a regression to the suite for `install --start` with a failing start that asserts the enabled and active state after the failure and after a following plain install.

## Adjudication of O1-O3 (reverification-v3)

| Item | Reported | My judgment | Basis |
|---|---|---|---|
| O1 snapshot left after SIGKILL | low | **non-blocking, low** | Reproduced: `lucifer-install.XXXXXX` remains in TMPDIR after a kill. The snapshot is mode 0700, root-owned, and holds only the public package, so there is no secret exposure. Default location `/var/tmp` is aged by Debian's systemd `tmp.conf` (30 days), so it is bounded. A related leak: `$OPT/.txn.XXXXXX` (unit copies) also survives SIGKILL and is never cleaned (seen in the O3 probe). Optional: have `clear_interrupted` remove stale `lucifer-install.*` under the lock and stale `.txn.*`. |
| O2 marked, already-activated release deleted | low | **non-blocking, but medium and should be fixed** | Reproduced (rc 0 at every step): install 1.0.0, update 1.1.0, update 1.2.0, then a marker left as a post-switch SIGKILL leaves it, `rollback lucifer-1.0.0…`, `rollback lucifer-1.1.0…`, `update 1.3.0`. Result: "warning: removing release lucifer-1.2.0-linux-arm64 left by an interrupted run", and 1.2.0 is gone. That release was complete, was switched to, and may have run in production, so this violates "preserve all prior releases". The cause is that `cmd_rollback` (`:476-499`) never calls `clear_interrupted`, and the marker outlives the switch. Not blocking: it needs an uncatchable crash after the switch followed by two manual moves, and the release can be reinstalled from its archive. Cheap fix: delete the marker right after `atomic_link "$CURRENT"` succeeds (in-process failure still discards through `new_release`), or call `clear_interrupted` in rollback too. |
| O3 unit mismatch after SIGKILL | documented limit | **non-blocking, but the docs understate it** | Reproduced with a real SIGKILL during `daemon-reload` in an update of an active service. The state after the kill: 1.1.0 unit installed, 1.0.0 current, service **stopped**, marker and `.txn` left. The consequences go beyond what O3 says: (a) a retry recomputes `was_active=0` and leaves playback stopped; (b) an update to any *other* version (1.2.0) and a plain `rollback` are both refused with exit 3 and the misleading "differs from any packaged copy (edited by hand…)", because `guard_manual_files` (`:331-346`) runs before `clear_interrupted`. Recovery needs `--adopt` (which backs up the *packaged* 1.1.0 unit as if hand-edited) or a same-version retry, plus a manual `systemctl start`. (c) The original unit survives only in the stale `.txn` dir. Not blocking: the window is short, crash atomicity is explicitly disclaimed in help and INSTALL.md:144, no release or configuration is lost, and a deliberate recovery path exists. Direction: in INSTALL.md:144, document the recovery (same-version retry or `--adopt`, then `systemctl start lucifer` if it was running). Optionally have `clear_interrupted` restore the unit from a stale `.txn` before the guard runs. |

## Optional findings (non-blocking)

- **N1:** `update`/`rollback` on a host with no installation create `/opt/lucifer/.lock` before refusing with exit 3 (`take_lock` at `:171-175` runs before the `CURRENT` check). Probe: a fresh root, `update` gave rc 3, and the root then contained `opt/lucifer/.lock`. The suite's check (`test-lucifer-install.sh` "update w/o install: no host dirs") only inspects `$R/etc`. Direction: check `[ -L "$CURRENT" ]` before `take_lock` (and re-check under the lock), or don't create `$OPT` in `take_lock` for update/rollback.
- **N2:** `txn_restore` puts the previous tmpfiles snippet back but doesn't rerun `systemd-tmpfiles --create` for it. Harmless while the snippet is unchanged across releases.
- **N3:** `CONFIG_CREATED` (`:224`) is set but never read. Remove it or use it.
- **N4:** the settle check (`sleep 3` and then `is-active`) catches a crash only within about 3 s. A later crash goes unnoticed. This heuristic is acceptable but should be named as a limit in the docs.

## Notes on areas checked without findings

- Extraction safety: names are restricted to `[A-Za-z0-9._+@=,/-]` (GNU tar's escape quoting turns control characters into backslashes, which are rejected), `..`, `.` and `//` are refused, `tar -tv` must report only `-`/`d` types, and after the private extraction (`--no-same-owner --no-same-permissions`) `find` must find nothing but files and directories. All of this runs as part of `validate_archive` before `require_root` and before any host effect.
- The verified bytes are the installed bytes (snapshot), and the checksum is documented as corruption detection only.
- The script safely replaces itself: the opened file is the old release's inode, and `main "$@"` is followed only by `exit $?`.
- CI adds the install suite and checks that `bin/lucifer-install` is present in both packages. No application or release expectation was weakened (the test diff only adds `deploy/test-lucifer-install.sh`).

Remaining unverified: anything on a real Pi or real systemd (enable/disable semantics, restart timing, boot start), real `lucifer-maint check` against target binaries, and disk-full behaviour.
