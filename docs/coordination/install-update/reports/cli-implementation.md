task: WORK-13 install.sh and lucifer update (cli-implement-v1)
role: Implementer
status: complete
outcome: install.sh is the install entry point and the installed `lucifer` command exposes update, rollback and list; the public lucifer-install executable and its link are retired; isolated suites pass.
artifacts:
  - docs/coordination/install-update/reports/cli-implementation.md
  - "branch ruach/lucifer-cli-impl, product commit 10de5dc2ef986b26320dc5d78afcf2bc85b80141"
  - deploy/lucifer
  - deploy/test-lucifer-command.sh
candidate_revision: 10de5dc2ef986b26320dc5d78afcf2bc85b80141
tested_revision: 10de5dc2ef986b26320dc5d78afcf2bc85b80141
verification:
  - "deploy/test-lucifer-maint.sh: exit 0"
  - "deploy/test-lucifer-command.sh (new): exit 0"
  - "deploy/test-lucifer-install.sh (extended with section L1): exit 0"
  - "deploy/test-lucifer-bootstrap.sh (extended): exit 0"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version v0.0.0-cli: exit 0; sha256sum -c OK for both"
  - "Package contents checked for both RIDs; engine archive validation accepted both (stopped at the root check, exit 1)"
  - "Not run: dotnet build/test (no C# change); no remote action; no privileged or physical test"
discoveries:
  - "A release built before this change has bin/lucifer-install and no libexec/lucifer-install, so an old engine cannot install a new-layout archive. v0.1.0 has no assets, so nothing live is affected; the new engine handles old-layout hosts."
  - "Release asset `lucifer-install` is kept as the private engine install.sh runs; its name was not changed."
blockers: []

# Report

Base b6f49d9, branch ruach/lucifer-cli-impl. Assignment committed unchanged in the product commit. No C#, playback, shutdown, Mpv, controller, task-record or raw-decision file was touched.

## Changed behavior (DEC-008)
- `deploy/lucifer` (new, packaged `bin/lucifer`, linked `/usr/local/bin/lucifer`): `update|rollback|list` exec the private engine `libexec/lucifer-install` with arguments and exit status unchanged and without reading configuration or touching the service or audio; `play|check|tag|input` exec `lucifer-maint` (env loaded); help and unknown commands go straight to `Amp.Nfc` (exit 2 for unknown), with an admin-commands note appended.
- Engine (`deploy/lucifer-install`, now `libexec/lucifer-install`, not on PATH): messages say `lucifer`; package must contain `bin/lucifer` and `libexec/lucifer-install`; `ensure_sbin_link` replaced by `ensure_links`, which creates, keeps or retires only links with the exact expected target. `/usr/local/sbin/lucifer-install` is removed only if it points at `/opt/lucifer/current/bin/lucifer-install`; foreign files or links are kept with a warning. Rolling back to a release without `bin/lucifer` restores the old link and drops ours. The unused `had_sbin` restore path was removed.
- `install.sh`, `publish.sh`, ci.yml, release.yml (runs the new suite), README and INSTALL.md updated. Preservation, HTTPS-only, checksum/private-staging, transaction and rollback logic are unchanged.

## Evidence
Baseline on b6f49d9 (maint, install, bootstrap): all exit 0. Logs were kept outside the repo. New coverage: command dispatch/args/exit forwarding/help/unknown/no-config (test-lucifer-command.sh); installed `lucifer update` through the fake GitHub (bootstrap suite now calls the installed `bin/lucifer`: latest, `--version`, no-op, rollback, offline archive, preservation); install.sh leaves the `lucifer` link and no sbin link; L1 release-switch tests (legacy to new, rollback to legacy, operator file and foreign link preserved). Real packages: both RIDs have identical launcher and engine bytes to deploy/, modes 0755, archive-engine validation accepted; unpacked `bin/lucifer list`/`update` returned 3/1 as expected.

## Limits
Stubs only; the arm binaries were not executed. C# unchanged, so previous app evidence is reused (Cli.cs and Program.cs are identical to base). No remote CI/release/tag action.

## Mechanical validation
`bun /opt/dev/ruach/skills/ruach-handoff/scripts/validate.ts <this report> --repo <worktree>`: exit 0, ok true, no diagnostics, both revisions resolved. Run separately from the technical tests.
