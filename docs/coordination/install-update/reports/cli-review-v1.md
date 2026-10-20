task: WORK-14 install.sh and lucifer update (cli-review-v1)
role: Reviewer
assignment: docs/coordination/install-update/assignments/cli-review-v1.md
status: complete
outcome: "Accepted for local delivery to ruach/lucifer-install-update: no blocking findings in b6f49d9..9c4cbe3. Two low optional findings (an ineffective L1 test assertion; a doc claim about warnings) and two non-blocking compatibility notes are recorded. Live release assets, remote Actions and physical acceptance remain unverified"
baseline: b6f49d9403d5cb9b55869a3f9127892c0953db15
reviewed_revision: 9c4cbe3e0ab07ef4813f24dbbedbfbd89ac70156
tested_revision: 10de5dc2ef986b26320dc5d78afcf2bc85b80141
artifacts:
  - docs/coordination/install-update/reports/cli-review-v1.md
  - docs/coordination/install-update/assignments/cli-review-v1.md
  - docs/coordination/install-update/reports/cli-implementation.md
  - branch ruach/lucifer-cli-review
verification:
  - "Scope: git diff b6f49d9 9c4cbe3 = 13 files (workflows, README, INSTALL.md, install.sh, deploy/lucifer, deploy/lucifer-install, publish.sh, three test scripts, implementer assignment/report); no Amp.Nfc/Amp.Nfc.Tests change (git diff --quiet exit 0); no playback, shutdown or task-record file"
  - "bash -n deploy/lucifer install.sh deploy/lucifer-install deploy/publish.sh deploy/test-lucifer-command.sh: exit 0; yaml.safe_load ci.yml and release.yml: ok"
  - "deploy/test-lucifer-maint.sh exit 0; deploy/test-lucifer-command.sh exit 0; deploy/test-lucifer-install.sh exit 0 (incl. L1); deploy/test-lucifer-bootstrap.sh exit 0 (piped stdin install, truncated-pipe sweep, HTTPS-only redirects, installed bin/lucifer update)"
  - "Probe A (real engine via a symlink to the installed bin/lucifer in an isolated root): update 1.1.0 exit 0 current switched; list exit 0; rollback exit 0; update to current exit 3; missing archive launcher 2 = engine 2; bad option 2; unknown rollback target 3; non-root 1; update --help 0; no check-as-service call for list/rollback/no-op"
  - "Probe F: update to a package whose check fails exits 1 with current and /usr/local/bin/lucifer unchanged"
  - "Probe D: the b6f49d9 engine against a new-layout archive: update and install exit 1 'archive lacks bin/lucifer-install', isolated tree hash unchanged, zero stubbed host calls"
  - "Probe C/C2: link edge cases (see findings F2, N2)"
  - "Probe R: legacy host with the retired sbin link: real engine removes it on update (link present=no); engine with the retire line mutated leaves it (present=yes)"
  - "Mutations in scratch copies (git archive HEAD): launcher drops args -> command suite exit 1 (3 FAIL); launcher forces exit 0 -> exit 1 (2 FAIL); rollback does not restore legacy link -> install suite exit 1 (1 FAIL); foreign lucifer link clobbered -> exit 1 (2 FAIL); retired sbin link not removed -> install suite exit 0 (0 FAIL), see F1"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version v0.0.0-rev into scratch: exit 0 both; sha256sum -c OK both; bin/lucifer and libexec/lucifer-install 0755 and byte-identical to deploy/; no bin/lucifer-install in either; engine archive validation accepted both (stopped at the root check, exit 1)"
  - "Not run: dotnet build/test (no C# change; prior app evidence applies), shellcheck/actionlint (not used), any remote Actions, tag, release, push, or privileged/host/Pi installation"
review:
  - "Independent review of b6f49d9..9c4cbe3 against cli-review-v1 acceptance: no blocking findings; accepted for local delivery only"
discoveries:
  - "release.yml workflow_dispatch checks out refs/tags/<tag>: re-running it for v0.1.0 would publish a legacy-layout release (bin/lucifer-install, sbin link) whose own engine cannot update to any new-layout release (probe D). Publication decision for the Coordinator/owner, not a defect of this candidate"
  - "test-lucifer-install.sh has the pattern check \"desc\" [ A ] && [ B ] at lines 183, 194, 541, 548 (and pre-existing 404): only [ A ] is asserted"
blockers: []

# Review: install.sh and `lucifer update` (WORK-14 v1)

Reviewed revision: 9c4cbe3 (pin), product/tested revision 10de5dc, full delta from b6f49d9. Worktree `ruach/lucifer-cli-review`, clean apart from the assignment before this commit. Scratch probes, logs, mutated copies and packages were kept in the session scratch directory, outside the repository. Only fixture processes were run. No global kills, no host changes and no remote actions.

## Acceptance assessment

| Condition | Result | Evidence |
|---|---|---|
| install.sh works through Bash stdin with release assets in isolation | met | bootstrap suite exit 0 (`piped` = `bash -s < install.sh`; latest/explicit; truncation sweep) |
| Installed `lucifer update` selects latest/explicit/archive, forwards args and status, needs no audio/service | met | bootstrap §4 through `current/bin/lucifer`; command suite; probe A (statuses 0/1/2/3 identical to the engine); `update|rollback|list` exec before any env load (`deploy/lucifer:78-81`) |
| `lucifer rollback` usable | met | probe A, L1, bootstrap rollback |
| Startup/help/unknown/maintenance coherent | met | command suite: help 0 with admin text, unknown → Amp.Nfc status with admin text on stderr, play/check/tag/input → lucifer-maint |
| No separate public installer for new installs; private libexec engine | met | package listing (no `bin/lucifer-install`); bootstrap check that install.sh leaves `/usr/local/bin/lucifer` and no sbin link |
| Packages, workflow and bootstrap asset names/layout consistent | met | reproduced packages; ci.yml/release.yml/publish.sh/engine required-member list agree; asset `lucifer-install` still produced from `deploy/lucifer-install` |
| Preservation, validation before side effects, atomic switch/recovery, operator files/links | met | install suite (unchanged transaction code; diff touches only messages, member list and links); probes D and F; L1 operator file/foreign link cases |
| Truncation, HTTPS-only, checksum, private staging | met, unchanged | bootstrap suite; diff does not touch those paths |
| Launcher resolves through the current release | met | `readlink -f` of `BASH_SOURCE` resolves `/usr/local/bin/lucifer → current → releases/X`, so launcher and engine are always the same release (command suite double symlink; probe A) |
| Rollback to a legacy release restores the old helper and drops the new link | met | L1; mutation m4 caught |
| Old engine cannot consume new layout | confirmed, refuses safely | probe D: exit 1, no host effects |
| `ensure_links` replacing `ensure_sbin_link` | no regression found | links are only touched after `txn_commit`, as before, so the removed `had_sbin` restore was dead code; foreign entries are no longer clobbered (the old `ln -sfn` replaced any symlink) |

## Findings (most severe first)

None are blocking.

**F1 — Low, optional (test coverage).** Location: `deploy/test-lucifer-install.sh:541` (same shape at 183, 194, 548). Problem: `check "L1 retired sbin link removed" [ ! -e … ] && [ ! -L … ]` passes only the first test to `check`. The test link targets the absolute `/opt/lucifer/current/bin/lucifer-install`, which is dangling in the isolated root (and absent on this host), so `! -e` is always true. Why it matters: retiring the legacy public helper (DEC-008) is not actually asserted. Evidence: mutation m3 (`rm -f "$OLD_SBIN_LINK"` → `:`) leaves the suite at exit 0, 0 FAIL. Probe R shows the product code itself does remove the link. For fresh installs, the bootstrap check at `deploy/test-lucifer-bootstrap.sh:351` does assert correctly. Direction: wrap the compound in `bash -c` as the bootstrap suite does, or use `[ ! -e X ] && [ ! -L X ]` inside a single helper.

**F2 — Low, optional (documentation accuracy / operator signal).** Location: `docs/install/INSTALL.md:77`, `deploy/lucifer-install:519-521`; also the implementer report. Problem: the docs say any foreign file or link at `/usr/local/sbin/lucifer-install` or `/usr/local/bin/lucifer` "is left alone with a warning". Only `/usr/local/bin/lucifer` warns. A foreign sbin entry is kept silently in both branches. Why it matters: after a rollback to a legacy release with an operator-owned sbin path, our `lucifer` link is removed and no helper ends up on PATH, with no message (probe C2: rc 0, warning count 0). Direction: add a `warn` in the legacy branch when the sbin path is foreign, or soften the doc wording. Only legacy hosts are affected, and none exist yet.

## Non-blocking compatibility notes

**N1 — legacy layout.** A release built before 10de5dc cannot update itself to a new-layout release: its engine requires `bin/lucifer-install` and refuses before any side effect (probe D). After a rollback to such a release, the restored `lucifer-install` helper has the same limit. The forward path is running the newer release's engine directly (`/opt/lucifer/releases/<new>/libexec/lucifer-install update`), and INSTALL.md does not document it. No such release has been published: v0.1.0 has no assets (publication report). The workflow_dispatch risk is under discoveries. This does not block this task.

**N2 — missing link directories.** `update` and `rollback` do not run `ensure_dirs`. If `/usr/local/bin` or `/usr/local/sbin` is missing, `ln -s` prints a raw error and the command still exits 0, with the switch committed and no link created (probe C). Base `ensure_sbin_link` behaved the same way, and Debian always ships both directories. Not a regression.

## Evidence gaps (not product blockers)

- Stubs only. Arm binaries not executed. No Pi, systemd or real-root run.
- No live GitHub release assets exist, so the real latest/explicit download and the published install.sh were not exercised. The remote v0.1.0 release CI failure stays an external publication blocker.
- Workflows were checked statically (YAML parse, names/paths against publish.sh and the bootstrap static contract) and not run.

## Handoff validation

`bun /opt/dev/ruach/skills/ruach-handoff/scripts/validate.ts docs/coordination/install-update/reports/cli-review-v1.md --repo /opt/dev/lucifer/.worktrees/lucifer-cli-review` was run on this report before committing it. The result is reported in the terminal handoff.
