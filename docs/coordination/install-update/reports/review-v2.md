---
task: install-update-work-03-review-v2
role: reviewer
assignment: docs/coordination/install-update/assignments/review-v2.md
status: complete
outcome: Re-review of the combined revision finds B1 fixed and O2, O3, N1, N2, N3 and N4 addressed, with no blocking findings; R1 and R2 adjudicated optional; the combined revision is accepted for authorized local delivery; physical acceptance remains unverified
reviewed_revision: 8699215e3fb9093bb79e522fb62ac35e1a95fe99
baseline: c11d21f403959e8a0ef977a81617e2b2b9cfb60a
artifacts:
  - docs/coordination/install-update/reports/review-v2.md
  - docs/coordination/install-update/assignments/review-v2.md
  - docs/coordination/install-update/reports/review-v1.md
  - branch ruach/lucifer-iu-review
verification:
  - "git merge --ff-only 8699215: fast-forward from c11d21f; HEAD equals reviewed_revision. The fix delta c11d21f..8699215 changes only deploy/lucifer-install, deploy/test-lucifer-install.sh, docs/install/INSTALL.md and coordination records; 43a05f7..8699215 changes coordination records only"
  - "Full fix delta inspected in context (txn_begin/txn_restore, activate_steps, clear_interrupted, guard message, update/rollback preflight, INSTALL.md install --start and After a crash sections); review-fixes-v3.md and verification-v4.md read as context only"
  - "bash -n on deploy/lucifer-install, lucifer-maint, publish.sh, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0. shellcheck not installed: not run"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok'. deploy/test-lucifer-install.sh (timeout 300): exit 0, 20 ok groups, 0 FAIL"
  - "Test delta: the only removed lines are the old systemctl stub start case and the old unset list, both replaced by supersets (disable, reset-failed, hang-start, no-active hooks); no assertion was removed or weakened"
  - "Independent stub probes (suite prelude reused, isolated TMPDIR and roots, real SIGKILL/SIGTERM through the hanging stubs, timeout 300): B1a-e, O2, O2b, O3 (refusal, --adopt, rollback), N1, N2 all as expected; detailed results in the body"
  - "Reused, not rerun: dotnet build (0 warnings) and dotnet test (408 passed), because no application or test-project file differs from 452637b at this pin; both-RID publish and packaged-help parity from verification-v4 at this product tree, because publish.sh is unchanged since my v1 publish check and copies deploy/lucifer-install verbatim"
  - "Not run: real installation, accounts, systemd, Pi, audio/mpv, real lucifer-maint check on target binaries, systemd-analyze verify (unit unchanged), disk-full behaviour"
discoveries:
  - "R2 also applies to the rollback recovery path: `rollback --adopt` after a kill leaves the saved pre-run files in /opt/lucifer/.txn.* and the marked interrupted release in releases/ until the next install or update, because rollback does not run clear_interrupted. Nothing is lost"
  - "The review-v2 assignment was untracked in the review worktree at start; it is committed unchanged with this report"
blockers: []
---

# WORK-03 independent re-review v2: install and update

The reviewed revision is `8699215e3fb9093bb79e522fb62ac35e1a95fe99`. Its product commit is `7fe6ddf`, and `43a05f7` is the independently tested candidate. My v1 review was at `c11d21f`. All probes used temporary roots and the suite's stubs. Stubs are not physical evidence.

**Verdict: no blocking findings remain. I accept the combined revision `8699215` for authorized local delivery.** The fix delta only changes things inside the original acceptance list, and the rest of that list still holds unchanged. Physical acceptance on a Pi is still unverified.

## Disposition of v1 findings

| Item | Result | Evidence (independent probes) |
|---|---|---|
| B1 failed `install --start` | **fixed** | Failing start: rc 1, then stop, reset-failed, disable, daemon-reload, in that order, while the unit still exists. The service ends disabled and inactive with no unit and no current link. A following default `install` reports "NOT started or enabled", which is now true (stub disabled). Settle failure (start ok, not active): same restoration. Service already enabled before a failed `--start`: it stays enabled, with 0 disable calls. SIGTERM during the `--start` start: rc 1, disabled, inactive, nothing installed. An update of an active and enabled service whose start fails: the service is active and enabled again, the old release is current, and nothing is disabled. |
| O2 completed release deleted | **fixed** | `activate_steps` now removes the marker right after the atomic switch. Real SIGKILL during start, after the switch: no marker left. Then `rollback 1.0.0`, `rollback 1.1.0` and `update 1.3.0` all rc 0, and all of 1.0.0, 1.1.0, 1.2.0 and 1.3.0 are present. A kill before the switch still has its marked release cleaned by a same-version retry (rc 0, with a warning). |
| O3 crash recovery | **addressed (documented and recoverable)** | A kill during daemon-reload, followed by a different-version update, is refused with rc 3. The message now points to "left by a killed run: see INSTALL.md, After a crash", and `.txn.*` is kept. `update --adopt` then succeeds, moves `.txn` to `backups/interrupted-*` (holding the pre-run 1.0.0 unit), and keeps backups of the replaced files. A plain `rollback` after a kill is refused with rc 3, and `rollback --adopt` succeeds. The new INSTALL.md "After a crash" section covers both paths, including the manual `systemctl start`. |
| N1 preflight effects | **fixed** | `update` and `rollback` on an uninstalled root give rc 3 and create 0 files. The link is checked again under the lock. |
| N2 tmpfiles restore | **fixed** | On a failed update, `systemd-tmpfiles --create` runs again after the restore, and the snippet is back to the 1.0.0 content. |
| N3 / N4 | **fixed** | `CONFIG_CREATED` is removed. The settle-wait limit is documented in INSTALL.md. |

## Adjudication of R1 and R2 (verification-v4)

- **R1 (optional, low): the snapshot is left behind after a SIGKILL.** Confirmed: my probe TMPDIR held 4 `lucifer-install.*` directories after 4 kills. Each is mode 0700, root-only, and holds the public package only, so no secret is exposed. The default `/var/tmp` is aged out by systemd's standard tmp.conf. It is disk hygiene only and is not a preservation or failure-contract issue. Optional direction: have `clear_interrupted` remove stale `lucifer-install.*` snapshots while holding the lock, and mention the leftover in the "After a crash" section.
- **R2 (optional, low): wording of "After a crash" step 3.** Confirmed, and it is slightly wider than reported. The pre-run unit and tmpfiles copies are only in `backups/interrupted-*` after an install or update has passed the guard and run `clear_interrupted`. After a refused run, or after the `rollback --adopt` recovery path, they are still in `/opt/lucifer/.txn.*`, and with rollback the marked interrupted release also stays until the next install or update. The files are always kept, so the only effect is that an operator may look in the wrong directory first. Optional direction: have step 3 name both locations, or call `clear_interrupted` from rollback.

## Optional observation

- When a kill happens after the switch but before the `previous` link is updated, `previous` still names the older release, so a plain `rollback` goes one step further back than expected. Nothing is deleted, and `list` together with a named rollback covers it. Not blocking.

## Remaining limitations

There is no evidence from a real Pi or real systemd, so the following are unverified: actual enable/disable/reset-failed behaviour, restart timing against the 3 s settle wait, boot start, and running the real `lucifer-maint check` on target binaries. Crash atomicity is still explicitly not claimed. The checksum detects corruption only and does not prove who published the archive.
