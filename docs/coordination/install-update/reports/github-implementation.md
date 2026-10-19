---
task: install-update-work-05
role: implementer
assignment: docs/coordination/install-update/assignments/github-implement-v1.md
status: complete
outcome: Stdin-capable install.sh and no-archive install/update from GitHub Releases implemented on the existing transaction, with release workflow, isolated tests and docs; no release is published, so the live one-liner is not claimed to work; physical acceptance unverified
baseline: 1d63868131e310bb3d86396ac1317ad784e371b8
candidate_revision: fe008d1d024740cb92a88cb5c50e901dc2a92c3b
tested_revision: fe008d1d024740cb92a88cb5c50e901dc2a92c3b
artifacts:
  - install.sh
  - deploy/lucifer-install
  - deploy/test-lucifer-bootstrap.sh
  - deploy/test-lucifer-install.sh
  - .github/workflows/release.yml
  - .github/workflows/ci.yml
  - docs/install/INSTALL.md
  - README.md
  - docs/coordination/install-update/assignments/github-implement-v1.md
  - docs/coordination/install-update/reports/github-implementation.md
  - branch ruach/lucifer-gh-impl
verification:
  - "bash -n install.sh deploy/lucifer-install deploy/publish.sh deploy/lucifer-maint deploy/test-*.sh: exit 0"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok"
  - "deploy/test-lucifer-install.sh: lucifer-install ok (all prior assertions kept; 2 superseded, see discoveries)"
  - "deploy/test-lucifer-bootstrap.sh (new): lucifer-bootstrap ok, all sections pass"
  - "Mutation checks on the new suite (checksum check removed in install.sh; RID mapping broken; up-to-date message removed): suite failed each time (3, 23, 1 failures); sources restored"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version v0.0.0-test: exit 0 both; sha256sum -c OK; packaged bin/lucifer-install byte-identical to deploy/lucifer-install in both archives"
  - "YAML parse of ci.yml and release.yml OK; release.yml checked by static contract assertions in the bootstrap suite. actionlint not available; the workflow was NOT run and no tag or release was created"
  - "dotnet build/408 tests not re-run: git diff 1d63868..HEAD touches no Amp.Nfc, Amp.Nfc.Tests or Lucifer.slnx file, so the application evidence of the baseline applies unchanged"
review: not-run
discoveries:
  - "Release availability, read-only and unauthenticated on 2026-10-19: GET api.github.com/repos/rothzeta/lucifer returns 200 with private=false; /releases returns 200 with an empty list; /tags returns 200 with an empty list; /releases/latest returns 404 (the 404 is 'no release', not an inaccessible repository); github.com/rothzeta/lucifer, /releases and /tags return 200; git ls-remote lists only refs/heads/master at 452637b; raw.githubusercontent.com/rothzeta/lucifer/master/install.sh returns 404 and README.md returns 200. So no release exists and install.sh is not yet on the remote default branch; the one-liner cannot work until both are published"
  - "Setup needed: push install.sh to the default branch, push a tag v<version> (runs release.yml) or run it manually for an existing tag; the repository must keep the default GITHUB_TOKEN write permission for contents. Prerelease tags (containing '-') are not 'latest'"
  - "Design: install.sh downloads the release asset lucifer-install (+.sha256, via releases/latest/download or releases/download/TAG, no API call), verifies it, and runs 'lucifer-install install'. The backend resolves the tag through api.github.com releases/latest or releases/tags/TAG (text parse, no eval), confirms asset names, downloads archive + .sha256 to a private mode-0700 directory under TMPDIR or /var/tmp, then runs the unchanged validate_archive and transaction. No second transaction engine; the archive's bin/lucifer-install is the same file as the release asset"
  - "Deviation to review: the validator lives in lucifer-install, so that script (checksum-verified against the release's own checksum file, not against an independent source) is executed before the archive is validated. Everything else is validated before host changes. Embedding validation in install.sh instead would duplicate the engine"
  - "Two prior assertions of test-lucifer-install.sh ('install' and 'install --start' without ARCHIVE exit 2) encoded the superseded archive-required behaviour; they now assert that the no-archive form attempts the download (closed local port, no real network) and fails with exit 1 and no host effect. No other assertion changed"
  - "Tests use a local HTTP server on 127.0.0.1 and the hook LUCIFER_INSTALL_TEST_INSECURE=1 to allow http; without it non-HTTPS endpoints are refused (tested). Fixtures and logs are in a temporary directory; no real host account, systemd, installation, Pi or network to GitHub (other than the read-only observations above)"
  - "The assignment file docs/coordination/install-update/assignments/github-implement-v1.md was untracked in the worktree and is committed unchanged with this report"
blockers: []
---

# WORK-05 GitHub Releases implementation

Baseline 1d63868, code commit fe008d1 (tested). No push, publication, tag, remote workflow, host install, delivery or cleanup was done. Integration conflicts: none (single branch from the baseline).

## What changed
- `install.sh` (new, top level): self-contained, no BASH_SOURCE use. Works through `curl ... | sudo bash [-s -- ARGS]`. `--help` exit 0; invalid argument, bad `--version`, non-HTTPS endpoint exit 2; non-root exit 1 with guidance. Options: `--version TAG --start --adopt --target-rid RID`; `LUCIFER_REPO` (default rothzeta/lucifer).
- `deploy/lucifer-install`: `install`/`update` without ARCHIVE download the latest stable release or `--version TAG` for the userland RID (`dpkg --print-architecture`, not the kernel; `--target-rid` overrides). Local ARCHIVE mode is unchanged and rejects `--version`. `update` is a no-op (exit 0) when the release is already current. Rollback, inactive/enable/start intent, staging, atomic activation and restoration are the existing code paths.
- `.github/workflows/release.yml` (new): on `v*` tag or manual run for an existing tag, runs the tests, builds both RIDs, attaches `lucifer-<tag>-<rid>.tar.gz(+.sha256)`, `lucifer-install(+.sha256)`; `-` in the tag marks a prerelease. `ci.yml` runs the new suite.
- Docs: INSTALL.md section 0 and "Publishing a release", README, help text.

## Usage
- Install: `curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash` (optionally `| sudo bash -s -- --version v1.2.3`). Update: `sudo lucifer-install update [--version TAG]`. Rollback: `sudo lucifer-install rollback [RELEASE]`. Offline: pass a local archive.
- Defaults: service not enabled/started, placeholder config created, next steps printed, no restart loop; existing config/catalogue/releases preserved.

## Evidence (bootstrap suite, isolated)
Stdin help/invalid args/non-root; latest vs explicit tag; arm64 and armhf userland with an aarch64-kernel stub; failures with no host effects and no temp left: no release, missing tag, API 429/403/500, installer asset missing, installer checksum malformed/mismatch, asset for the userland missing, truncated archive, archive 429, archive checksum mismatch/malformed, RID mismatch inside package, path-traversal member; installed no-archive update (inactive stays inactive, active restarted, config and catalogue byte-identical, previous release kept, no-op repeat), config-check rejection leaving the host byte-identical, activation start failure restoring the previous release and service, explicit rollback, offline archive update with no network; static workflow/naming contract (publish.sh name template, installer expectation, bootstrap assets, workflow asset globs).

## Not verified / limits
- Remote GitHub Actions never ran; no real download from GitHub of a release (none exists); actionlint unavailable; physical Pi acceptance unverified; stubs are not physical evidence.
- Checksums detect corruption, not publisher authenticity. Rate limits for unauthenticated API use (60/hour) can block resolution; error is reported, no token is used.
- A killed run is handled as before (INSTALL.md, After a crash).
