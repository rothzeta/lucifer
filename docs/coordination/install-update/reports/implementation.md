---
task: install-update-work-01
role: implementer
assignment: docs/coordination/install-update/assignments/implement-v1.md
status: complete
outcome: lucifer-install (install, update, rollback, list) implemented, packaged, documented and verified in isolation; physical acceptance unverified
candidate_revision: 697b072c9161dc8ccb7256f89996c8ae30b07872
tested_revision: 697b072c9161dc8ccb7256f89996c8ae30b07872
artifacts:
  - deploy/lucifer-install
  - deploy/test-lucifer-install.sh
  - deploy/publish.sh
  - docs/install/INSTALL.md
  - README.md
  - .github/workflows/ci.yml
  - docs/coordination/install-update/reports/implementation.md
  - branch ruach/lucifer-iu-impl
verification:
  - "bash -n on deploy/lucifer-install, publish.sh, lucifer-maint, test-lucifer-install.sh: exit 0"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok"
  - "deploy/test-lucifer-install.sh (isolated acceptance, temporary root, stubbed host commands): lucifer-install ok, all checks pass; 4 mutations of the installer (allow links, skip restore, stop an inactive service, skip RID check) each made it fail"
  - "dotnet build: 0 warnings, 0 errors; dotnet test: 408 passed, 0 failed"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm with --version ci: both built; sha256sum -c OK for both; both archives contain executable bin/lucifer-install; extracted --help is identical to deploy/lucifer-install --help"
  - "Real archives passed lucifer-install validation (checksum, RID, member checks) up to the root check on both RIDs; a linux-arm64 archive with --target-rid linux-arm was rejected"
  - "Not run: any real installation, systemd, account or Pi activity; systemd-analyze verify was not re-run because the unit is unchanged"
discoveries:
  - "Under `{ ...; } || x` errexit is disabled, so activation steps are a function with explicit `|| return 1`; a test mutation confirmed restore coverage"
  - "The script runs from /opt/lucifer/current, which update replaces; main is called once and followed by exit so bash never reads a replaced file mid-run"
  - "The assignment forbids Claude co-author trailers; the commit carries none, overriding the default attribution reminder"
  - "Existing env file mode is tightened to 0640 root:lucifer on every install (deliberate secret-permission policy); other config files are untouched"
blockers: []
---

# Implementation report: install and update (WORK-01)

## Changes

- `deploy/lucifer-install`: single CLI `install|update|rollback|list`. Packaged as `bin/lucifer-install` by `deploy/publish.sh` and linked at `/usr/local/sbin/lucifer-install`.
- `deploy/test-lucifer-install.sh`: isolated acceptance test, wired into CI together with a packaging check for the entry point.
- `docs/install/INSTALL.md` sections 3 and 8 rewritten for the CLI; README packaging paragraph updated.

## CLI

```
sudo lucifer-install install  [--start] [--adopt] [--checksum FILE] [--target-rid RID] ARCHIVE
sudo lucifer-install update   [--adopt] [--checksum FILE] [--target-rid RID] ARCHIVE
sudo lucifer-install rollback [RELEASE]
lucifer-install list
```

Exit codes: 0 ok, 1 failure with host restored, 2 usage, 3 refused by existing state. `LUCIFER_INSTALL_ROOT` prefixes host paths (test hook); `LUCIFER_INSTALL_SETTLE_SECONDS` (default 3) is the wait before the restarted service must report active.

## Failure semantics

- Before any host effect: checksum (corruption only, documented as not publisher authentication), archive name, host userland RID (dpkg, or `--target-rid`), root, member names and types (no links, devices, absolute, `..`, odd characters, members outside the single top directory), required entry points, VERSION and RID file agreement.
- Install never starts the service unless `--start` and the configuration is no longer the placeholder; a fresh env file leads to printed instructions.
- Update: stage, run the staged release's `lucifer-maint check` against live configuration before stopping; then under a lock stop only if active, replace unit and tmpfiles atomically, `daemon-reload`, switch `current` atomically, re-check, start only if previously active and require active. Any failure restores the previous link, `previous` link, exact unit and tmpfiles content or absence, reloads systemd, restarts a previously active service, and removes the failed release directory.
- Rollback uses the same transaction. Existing manual `current` directory, existing release, hand-edited unit (without `--adopt`) are refused with exit 3.
- Config, catalogue, input map and prior releases are never modified; the env file mode is tightened to 0640 root:lucifer.

## Integration

Single branch `ruach/lucifer-iu-impl` from prerequisite `ac9e415` (descended from `452637b`). No other sources, no conflicts. The combined scope is confirmed at `697b072`. The assignment file is committed unchanged with this report; the report commit differs from the tested revision only by documentation under `docs/coordination/install-update/`.

## Limits

Physical acceptance is unverified: no Pi, no real systemd, accounts, mpv, or audio. Stub tests are not physical evidence. Automatic downloading is out of scope. Checksum does not authenticate the publisher. The udev rule remains a manual step.
