---
task: LUC-11-impl
status: complete
outcome: "Packaging, systemd unit, env/catalogue/udev examples, install/update/rollback/uninstall docs and a basic CI workflow are in place for the parts of LUC-11 that do not depend on LUC-07/LUC-09. deploy/publish.sh really produced versioned archives plus SHA-256 files for both linux-arm64 and linux-arm here; both unpack and verify. Nothing was installed or run on a Pi."
artifacts:
  - deploy/publish.sh
  - deploy/lucifer-maint
  - deploy/lucifer.service
  - deploy/lucifer.tmpfiles.conf
  - deploy/lucifer.env.example
  - deploy/99-lucifer-nfc.rules.example
  - docs/install/INSTALL.md
  - .github/workflows/ci.yml
  - README.md
  - docs/coordination/v1/LUC-11/impl-1-report.md
  - branch ruach/luc-11-impl
candidate_revision: 39c7dad6c0b63f27b675c16e425c5d8ae8a031f8
tested_revision: 39c7dad6c0b63f27b675c16e425c5d8ae8a031f8
verification:
  - "dotnet publish -c Release -r linux-arm64 and -r linux-arm --self-contained: both succeeded here (runtime packs microsoft.netcore.app.runtime.linux-arm64 and linux-arm were in the NuGet cache; no network was used). Output ELF aarch64 and ELF 32-bit ARM EABI5, runtime 10.0.10."
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version 0.0.0-test: archives and .sha256 produced; sha256sum -c OK for both; tar -xzf unpacks; contents bin/ config/ systemd/ udev/ INSTALL.md VERSION RID; Amp.Nfc and lucifer-maint executable; no pdb. Second linux-arm64 run gave the identical SHA-256 (9f2ba950...)."
  - "systemd-analyze verify deploy/lucifer.service: exit 1 only because /opt/lucifer/current/bin/Amp.Nfc does not exist here. With ExecStart replaced by /bin/true in a scratchpad copy it exits 0 with no output. It does not check users, groups or EnvironmentFile existence on a target."
  - "bash -n on publish.sh and lucifer-maint: ok. shellcheck is not installed: not run."
  - "lucifer-maint run against a stub Amp.Nfc with the example env file: LUCIFER_IPC_DIR=/run/lucifer reached the child."
  - "ci.yml parsed by PyYAML: valid, one job build-test. Its commands (dotnet build, dotnet test, publish.sh for both RIDs, sha256sum -c) are the ones run here; the workflow itself was not executed on GitHub."
  - "dotnet build: 0 errors, 4 warnings (existing test-project warnings CA1416, CS0184). dotnet test: 259 passed, 0 failed. No source or test changed."
review: not-run
discoveries:
  - "The README's earlier note that the unit would be added later is replaced with a pointer to lucifer-maint and INSTALL.md; README gained a short Packaging section. No application source changed."
  - "RuntimeDirectory alone would delete /run/lucifer on stop and break maintenance runs that need the shared LUCIFER_IPC_DIR; the unit uses RuntimeDirectoryPreserve=yes and a tmpfiles.d snippet creates the directory at boot."
  - "deploy/lucifer-maint sources the env file with a shell, so the env file must be plain KEY=value without quotes; documented in the file."
  - "The app-side defaults still hold: LUCIFER_AUDIO_DEVICE default alsa/default:CARD=Pro is a repository default, not a verified Pi identifier."
  - "The udev example uses the common CH341 IDs 1a86:7523 as an unverified guess; replace after LUC-08."
  - "Restart=on-failure with StartLimitBurst=5 in 120 s: a permanent config error stops after five starts instead of looping."
  - "The LUC-09 input-controller group/device access and unit/doc additions are not included; the unit has SupplementaryGroups=dialout audio only (input group to be added by LUC-09)."
  - "Board proposal (not applied): LUC-11 evidence records the automated parts of A02/A23 (archive, checksum, unit verify, CI definition); install, reboot start, controls, update and rollback stay open for LUC-12."
blockers: []
---

# LUC-11 implementer report (packaging, CI, service, docs)

## Commands run
- `dotnet publish Amp.Nfc/Amp.Nfc.csproj -c Release -r linux-arm64|linux-arm --self-contained true` (probe, then through the script).
- `deploy/publish.sh --rid <rid> --version 0.0.0-test --out <scratch>/dist` for both RIDs; `sha256sum -c`; `tar -xzf`; `file` on the unpacked binaries.
- `systemd-analyze verify` on the unit (real and with a substituted ExecStart); `bash -n`; PyYAML load of ci.yml; `dotnet build`; `dotnet test`.

## Design
- Package: `lucifer-<version>-<rid>.tar.gz` (sorted, numeric owner 0, mtime from the commit, `gzip -n`) with `.sha256`. Install layout `/opt/lucifer/releases/<name>` with a `current` symlink, so update and rollback are a symlink change and config (`/etc/lucifer`, `/var/lib/lucifer`) is untouched.
- Unit: user and group lucifer, `SupplementaryGroups=dialout audio`, `EnvironmentFile=/etc/lucifer/lucifer.env`, RuntimeDirectory with Preserve, `Restart=on-failure`, `KillMode=control-group`, `TimeoutStopSec=30` above the 10 s default shutdown deadline (stated to track `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS`).
- Target RID is a required script argument; the doc tells how to read the Pi userland (`dpkg --print-architecture`) but chooses nothing and claims no compatibility.

## Not proven here
Run on a Pi, boot start, native dependencies (ICU, OpenSSL), group access, udev rule, ALSA name, `KillMode` with a real mpv, update and rollback, the GitHub Actions run itself, shellcheck. Archives were built but the aarch64/ARM binaries cannot execute on this host. CI does not prove hardware.

## Device-pending (UNVERIFIED-ON-DEVICE)
- Determine the userland architecture and choose the RID (LUC-08).
- Install from the archive without a development checkout; `ldd` the app and libcoreclr; start after reboot.
- Service user access to the serial adapter and the DAC; the udev rule against the real adapter; real ALSA identifier.
- Maintenance workflow (stop, `lucifer-maint tag write`, start) and the reader-lock refusal against the installed unit.
- Update to a second package and restore the prior one; uninstall.
- Stop within TimeoutStopSec with real mpv and no orphan; Restart on fatal exit (shared with LUC-10, LUC-12).
- Controls (LUC-09) and the input device group.
