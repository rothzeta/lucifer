---
task: install-update-work-01-fixes-v2
role: implementer
assignment: docs/coordination/install-update/assignments/fix-v2.md
status: complete
outcome: F1-F4 fixed in deploy/lucifer-install with regressions; isolated suites, syntax and packaging pass; physical acceptance unverified; independent reverification and review not yet done
candidate_revision: c96dc4a2f3c7331a4f97a022f4b049e26add8f70
tested_revision: c96dc4a2f3c7331a4f97a022f4b049e26add8f70
artifacts:
  - deploy/lucifer-install
  - deploy/test-lucifer-install.sh
  - docs/install/INSTALL.md
  - docs/coordination/install-update/reports/fixes-v2.md
  - branch ruach/lucifer-iu-impl
verification:
  - "git merge --ff-only 8e87033400e116a3a1371f9599f5412d3698ab37: fast-forward from e2790b6; existing implementation.md and verification.md unchanged"
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh, test-lucifer-maint.sh: exit 0"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok"
  - "deploy/test-lucifer-install.sh at c96dc4a: exit 0, 'lucifer-install: ok', no FAIL (previous cases retained plus F1-F4 groups)"
  - "Mutation check: reverting each fix (rollback guard, signal discard, marker cleanup, blank-password detection, check before start, snapshot use, extraction failure) made the suite fail (1 to 8 failed checks each); original restored and passing"
  - "publish.sh --rid linux-arm64 and --rid linux-arm --version ci: rc 0, sha256sum -c OK; packaged bin/lucifer-install byte-identical to deploy/lucifer-install; extracted --help identical; arm archive lists bin/lucifer-install; real arm64 archive passes validation and extraction up to the root check; no snapshot left in the temp directory"
  - "dotnet build / dotnet test not rerun: git diff 8e87033..c96dc4a touches only deploy/lucifer-install, deploy/test-lucifer-install.sh and docs/install/INSTALL.md (no Amp.Nfc or test project change); earlier evidence stands: build 0 warnings 0 errors, 408 tests passed at 697b072 and independently at e2790b6"
  - "Not run: real host installation, accounts, systemd, Pi, real lucifer-maint check, shellcheck"
discoveries:
  - "Integration: single branch ruach/lucifer-iu-impl, sources 8e87033 (fast-forward) plus product commit c96dc4a, no conflicts"
  - "Extracting once into the private snapshot also detects truncated or damaged streams before host effects; disk need is about twice the unpacked size in TMPDIR or /var/tmp"
  - "require_root no longer creates the opt directory, and the --start config refusal runs before the lock, so refused starts leave no files"
  - "The commit carries no co-author trailer, per the assignment"
blockers: []
---

# Fixes v2 report

## Dispositions

- **F1 fixed.** `rollback` calls the same hand-edit guard as update and install and accepts `--adopt`. Without it, an edited unit or tmpfiles file gives exit 3, nothing stopped or changed; with it, originals are kept in `/opt/lucifer/backups`. Help and INSTALL.md aligned. Tests: refusal with unchanged tree hash and no stop, adoption with backup, tmpfiles edit, help text.
- **F2 fixed.** INT, TERM and HUP now restore the prior state and remove the new release and its marker. New releases are published with a `.pending-<release>` marker; the next install or update removes marked leftovers and stale `.stage.*`, so a retry works after a simulated SIGKILL. An unmarked existing release directory is never deleted: exit 3 with a message to remove it by hand. Tests: real SIGTERM during `daemon-reload` (tree identical, service active, retry succeeds), simulated SIGKILL leftover, unmarked directory untouched. Limit: SIGKILL, crash or power loss cannot be undone in-process (documented); crash atomicity is not claimed or tested.
- **F3 fixed.** Placeholder detection ignores CR and padding and treats a blank, whitespace or missing password as placeholder. `install --start` refuses (exit 3) before any host change for those; otherwise `lucifer-maint check` runs as the service user against the installed release before `enable` and `start`, and a failure (for example CRLF with a real password, or a failing check) restores the host with exit 1 and nothing enabled or started. No file is sourced and credentials are not printed. Default install behaviour is unchanged (no start).
- **F4 fixed.** The archive and checksum are copied to a private temporary snapshot, verified, listed and fully extracted there before any account, directory or unit change; staging copies from that tree. Over-long components and truncated streams are rejected with no host effects; swapping the source archive and checksum after verification does not change the installed bytes (tested through a stub that swaps them mid-validation). The snapshot is removed on exit. The checksum remains corruption evidence only.

## Residual limits

Physical acceptance is unverified; stubs are not physical evidence. SIGKILL and power loss are handled only by next-run cleanup, not by restoring the previous unit. Snapshot needs temp space. Independent reverification and review are still required; nothing here asserts acceptance.
