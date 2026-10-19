# Installing Lucifer on the Pi

Everything here that needs the Pi is marked **UNVERIFIED-ON-DEVICE**. The package build and the unit syntax are checked on a development machine; no installation on a Pi has been performed or is claimed. Real results belong to LUC-08 (baseline) and LUC-12 (release gate); these steps feed A02 and A23.

Target architecture is not chosen here. Determine it on the Pi (LUC-08) and build for that:

| Pi userland | Command to check | RID |
| --- | --- | --- |
| 64-bit | `dpkg --print-architecture` prints `arm64` (or `uname -m` prints `aarch64`) | `linux-arm64` |
| 32-bit | `dpkg --print-architecture` prints `armhf` | `linux-arm` |

`uname -m` can report a 64-bit kernel on a 32-bit userland, so trust `dpkg --print-architecture`. Compatibility of either package with the Pi is not established until it runs there. Native AOT is not used.

## 0. One-line install from GitHub Releases (recommended)

Prebuilt packages for both userland RIDs are attached to each GitHub Release of the repository (see "Publishing a release" below). Nothing is built or copied by hand. On the Pi:

```sh
curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash
curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash -s -- --version v1.2.3   # a specific release
curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash -s -- --help
```

`install.sh` is self-contained (it works on stdin). If the download is cut off, the partial script refuses to run and changes nothing, so your options are never silently dropped. It downloads the release's `lucifer-install` and `lucifer-install.sha256` over HTTPS into a private temporary directory, checks the checksum, and runs `lucifer-install install`, which resolves the latest stable release (or `--version TAG`; prereleases are never "latest"), picks the archive for this host's userland RID from `dpkg --print-architecture` (not the kernel), downloads it with its `.sha256`, and then validates and installs it with the transaction described in section 3. Options after `--` are `--version TAG`, `--start`, `--adopt`, `--target-rid RID`. `LUCIFER_REPO=owner/name` selects another repository; sudo drops environment variables, so write `curl -fsSL <URL> | sudo env LUCIFER_REPO=owner/name bash` and repeat it for later updates (`sudo env LUCIFER_REPO=owner/name lucifer-install update`). Every download and every redirect must be HTTPS; a redirect to plain http is refused. A missing release or asset, a rate-limit or access error, a failed or truncated download, a checksum or RID mismatch, or an unsafe package stops with a message before the account, directories, unit or service are touched. The checksum detects corruption only; it does not authenticate the publisher, and running a script from the network trusts GitHub and the repository owner. The installer is a bash script that has to run before the archive can be validated, so it is itself fetched from the release and checksum-verified first.

Defaults: the service is neither enabled nor started and the generated `/etc/lucifer/lucifer.env` holds the placeholder password, so there is no restart loop; edit it and follow the printed next steps (section 3).

Later updates need no archive and no new command:

```sh
sudo lucifer-install update                  # latest stable release
sudo lucifer-install update --version v1.3.0
sudo lucifer-install list
sudo lucifer-install rollback                # previous release
```

`update` does nothing when the selected release is already current. Offline use is unchanged: pass a local archive (sections 1 and 8).

## 1. Build the package manually (development machine, offline use)

Needs the .NET 10 SDK and network or a populated NuGet cache for the runtime pack.

```sh
deploy/publish.sh --rid linux-arm64 --version 1.0.0   # or --rid linux-arm
```

Output in `dist/`: `lucifer-<version>-<rid>.tar.gz` and `lucifer-<version>-<rid>.tar.gz.sha256`. The archive contains `bin/` (self-contained app plus `lucifer-maint` and `lucifer-install`), `config/` (env, catalogue and input-map examples), `systemd/`, `udev/`, `VERSION`, `RID` and this file. Without `--version` the name comes from `git describe`.

Copy both files to the Pi (for example with `scp`).

## Publishing a release (maintainer)

A source-only repository cannot satisfy `install.sh`: it needs a published release. Pushing a tag `v<version>` (for example `v1.0.0`) runs `.github/workflows/release.yml`, which tests, builds both RIDs with `deploy/publish.sh --version <tag>` and attaches `lucifer-<tag>-linux-arm64.tar.gz`, `lucifer-<tag>-linux-arm.tar.gz`, their `.sha256` files, `lucifer-install` and `lucifer-install.sha256`. These names are what the downloader expects; a tag containing `-` is a prerelease. The workflow can also be started manually for an existing tag. `install.sh` must also be on the default branch of the repository to be fetchable from `raw.githubusercontent.com`.

## 2. Prerequisites on the Pi (UNVERIFIED-ON-DEVICE)

- `mpv` installed and on the service's `PATH` (or set `LUCIFER_MPV_PATH`).
- Native libraries the self-contained app needs; check after unpacking with `ldd /opt/lucifer/current/bin/Amp.Nfc | grep "not found"` and `ldd .../libcoreclr.so`. Typical Debian packages: `libicu*`, `libssl3`, `zlib1g`. If ICU is absent, set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` in the env file.
- A transport controller visible as a Linux input device (optional; controls stay off without it). See the Transport controls part of section 3.
- Network reachability of Navidrome and a Navidrome account for this device. Credentials go only in `/etc/lucifer/lucifer.env`.

## 3. First install (UNVERIFIED-ON-DEVICE)

The package carries `bin/lucifer-install`. Unpack it once to run the installer, or run it from a checkout (`deploy/lucifer-install`). With an archive argument (offline mode) it needs root, the archive, and the `.sha256` file beside it, and nothing is downloaded; without one it downloads the release as in section 0.

```sh
sudo deploy/lucifer-install install lucifer-1.0.0-linux-arm64.tar.gz
# or, from an unpacked archive:  sudo lucifer-1.0.0-linux-arm64/bin/lucifer-install install lucifer-1.0.0-linux-arm64.tar.gz
```

It first copies the archive and checksum file to a private root-only temporary snapshot (`TMPDIR`, default `/var/tmp`; allow about twice the unpacked size free) and does everything else from that copy, so replacing the files you passed afterwards changes nothing. Before changing the host it verifies the checksum (corruption detection only; it does not prove who published the archive, so obtain archive and checksum over a trusted channel), that the archive's RID matches this host's userland (`dpkg --print-architecture`; override with `--target-rid`), and that every member is a plain file or directory inside the single `lucifer-<version>-<rid>/` directory (no links, devices, absolute or `..` paths) with the expected entry points, and it extracts the snapshot once, so a damaged or over-long-named archive is rejected before any account, directory or unit is touched. Then it:

- creates the `lucifer` system account (groups `dialout audio input` where they exist) and the directories, idempotently;
- extracts to `/opt/lucifer/releases/lucifer-<version>-<rid>` (owned by root) and points `/opt/lucifer/current` at it;
- installs `/etc/systemd/system/lucifer.service` and `/etc/tmpfiles.d/lucifer.conf` and reloads systemd;
- creates `/etc/lucifer/lucifer.env` (root:lucifer 0640) and `/var/lib/lucifer/catalogue.json` only when missing; existing ones are kept byte for byte (the env file mode is tightened to 0640 root:lucifer);
- links `/usr/local/sbin/lucifer-install` to the current release, so later commands are `sudo lucifer-install ...`.

It does **not** start or enable the service. The new env file holds the placeholder password, so edit it first, check it, then enable:

```sh
sudo $EDITOR /etc/lucifer/lucifer.env                    # credentials, LUCIFER_AUDIO_DEVICE, LUCIFER_NFC_DEVICE
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint check
sudo systemctl enable --now lucifer
```

`install --start` enables and starts the service only when the env file exists and its `NAVIDROME_PASSWORD` is present, not blank and not the placeholder (CRLF and padding are ignored when judging this), and then only after `lucifer-maint check` passes against the installed release; otherwise it refuses (exit 3) or fails with the host restored (exit 1). A failure after the service was enabled or started (a failing start, or not active after the settle wait) stops what this run started and disables what this run enabled, then removes the unit, so a service that was already enabled stays enabled and nothing is left behind to start at boot. The settle wait is short (3 s, `LUCIFER_INSTALL_SETTLE_SECONDS`): it only catches a service that fails to start or dies at once, not one that crashes later, so confirm with `systemctl status lucifer` and the journal. Existing situations are handled deliberately: `install` refuses (exit 3) when `/opt/lucifer/current` already exists (use `update`; a manual directory must be moved aside) and when an existing unit or tmpfiles file differs from the package, unless `--adopt` is given (the original is kept in `/opt/lucifer/backups`). An existing configuration is never overwritten. The udev rule stays a manual step (see below).

The catalogue example contains two migration examples whose server availability is unconfirmed. Replace them with verified entries (see README, Catalogue).

### Configuration and the unit

- Env file `/etc/lucifer/lucifer.env`: all settings are listed in the README; the example has no secrets.
- Unit facts: runs as user `lucifer` with groups `dialout` (serial reader), `audio` (DAC) and `input` (transport controller); `EnvironmentFile=/etc/lucifer/lucifer.env`; `RuntimeDirectory=lucifer` (`/run/lucifer`, preserved after stop, created at boot by tmpfiles); `Restart=on-failure` (the app exits 1 on fatal mpv/reader failure and 0 on a requested stop); `KillMode=mixed` (SIGTERM goes to the app, which stops its own mpv; anything left in the control group is SIGKILLed once the app has exited, or when `TimeoutStopSec` runs out if the app hangs); `TimeoutStopSec=30`, which exceeds `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS` (default 10; the app rejects values above 25). Keep `TimeoutStopSec` above the app deadline if you change either.
- Hardening in the unit: `ProtectSystem=strict` (only `/var/lib/lucifer` and `/run/lucifer` are writable), `ProtectHome`, `ProtectKernelTunables/Modules/Logs`, `ProtectControlGroups`, `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK`. `PrivateTmp` is not used: it would make `/tmp` writable and private, so an unset `LUCIFER_IPC_DIR` would quietly fall back to a `/tmp` directory that maintenance runs cannot see (the shared directory is `/run/lucifer`, which `PrivateTmp` does not affect); with `/tmp` read-only under `ProtectSystem=strict`, an unset `LUCIFER_IPC_DIR` fails loudly. `PrivateDevices` is not used (the reader, input and sound devices are needed). **Unverified on a device**: it passes `systemd-analyze verify` only. If the service fails to start or loses audio, input or the reader after this change, comment out these lines one at a time and report which one was responsible.
- Shared IPC directory: `LUCIFER_IPC_DIR=/run/lucifer` in the env file is used by the service and by `lucifer-maint`, so the reader lock stops a maintenance run from competing with the service. Do not set a different value for one of them.
- Env file syntax: plain `KEY=value` lines. `lucifer-maint` refuses (exit 2, value not printed) any line or value that systemd's `EnvironmentFile` would read differently: a backslash, leading or trailing whitespace, or a value starting or ending with a quote. Rewrite the value (for example a password without those characters) so the service and maintenance runs see the same string.
- Five failed starts within 120 s stop automatic restarts (`StartLimit*`); clear with `sudo systemctl reset-failed lucifer` after fixing the cause.

### Transport controls (UNVERIFIED-ON-DEVICE)

Controls are off until `LUCIFER_INPUT_DEVICE` and `LUCIFER_INPUT_MAP` are both set. The unit adds the `input` group so the service can read `/dev/input/*`; confirm with `id lucifer` and `ls -l /dev/input/by-id/`.

1. Find the device (prefer a stable `/dev/input/by-id/...` path) and its key codes while pressing each button:
   `sudo -u lucifer /opt/lucifer/current/bin/Amp.Nfc input inspect --device /dev/input/by-id/<name>`
   It prints `EV_KEY code=<n> value=1 (press)`; it only reads the device and takes no reader lock, so it can run while the service is up.
2. `sudo install -o lucifer -g lucifer -m 0640 /opt/lucifer/current/config/input-map.example.json /var/lib/lucifer/input-map.json`, then replace every `code` with what step 1 printed. The example codes (164, 163, 165) are placeholders and are not verified for any controller.
3. Uncomment `LUCIFER_INPUT_DEVICE` and `LUCIFER_INPUT_MAP` in `/etc/lucifer/lucifer.env`; `lucifer-maint check` reports a `controls:` line and rejects an invalid map. Optionally set `LUCIFER_INPUT_REQUIRED=true` so a missing or lost device is fatal and restarts the service. Restart the service.

Transport policy and repeat handling are in the README (Transport controls).

### Stable serial path (UNVERIFIED-ON-DEVICE)

`udev/99-lucifer-nfc.rules.example` gives a `/dev/lucifer-nfc` symlink matched on the CH341 vendor/product IDs. Its IDs are the common ones for CH341, not read from the actual adapter; record the real identity during LUC-08 and edit before use. Identical adapters without serial numbers need a USB port-path match. Without a rule, `/dev/ttyUSB0` can change when devices are replugged.

### ALSA identifier

`LUCIFER_AUDIO_DEVICE` is configuration only. List what mpv sees as the service user: `sudo -u lucifer mpv --audio-device=help`. Copy the exact identifier into the env file and restart.

## 4. Health check (UNVERIFIED-ON-DEVICE)

```sh
systemctl is-active lucifer                  # active
systemctl status lucifer --no-pager
journalctl -u lucifer -b --no-pager | tail -50
ls -l /run/lucifer                           # mpv socket and reader lock while running
pgrep -a mpv                                 # exactly one mpv, child of the service
```

mpv's own output is discarded by the application (it would contain stream URLs with credentials), so the journal has the application's lines only. The journal shows the PN532 firmware banner when the reader opens. Presenting a programmed object selects its album; the journal reports `not in the catalogue` or a stale mapping otherwise. Reboot check: `sudo reboot`, then repeat the above without logging in as another user.

## 5. Add an album

1. Edit `/var/lib/lucifer/catalogue.json` (format and checks in README, Catalogue).
2. `sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint check` must report no errors. No restart is needed; the player re-reads the file on each tap.
3. Program an object for the release UUID (next section).

## 6. Stop, program, start (maintenance)

Only one process may own the reader.

```sh
sudo systemctl stop lucifer
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint tag write --release <release-uuid>
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint tag read      # optional read-back
sudo systemctl start lucifer
```

If the service is running, the maintenance run is refused with an error naming the reader. UNVERIFIED-ON-DEVICE: that refusal is tested with a lock file only, not against the installed unit.

## 7. Inspect failures

- `journalctl -u lucifer -b` for the current boot; `-f` to follow; `-p err` for errors only.
- `systemctl status lucifer` shows the last exit status; exit 1 after an mpv or reader fault is the designed fail-and-restart behaviour.
- Config errors name the failing variable; run `lucifer-maint check` to list them. Credentials are never printed.
- Reader missing: `ls -l /dev/lucifer-nfc /dev/ttyUSB*`, `id lucifer` (needs `dialout`), `dmesg | tail`.
- No sound: `sudo -u lucifer mpv --audio-device=help`, check `LUCIFER_AUDIO_DEVICE`, `id lucifer` (needs `audio`).
- Restart loop stopped by `StartLimit*`: fix the cause, then `sudo systemctl reset-failed lucifer && sudo systemctl start lucifer`.
- After a stop, no leftovers: `pgrep -a mpv` is empty and `/run/lucifer` has no `amp-mpv.sock`. The `lucifer-reader-*.lock` file stays (it is never deleted) and is harmless while nothing holds it.

## 8. Update and roll back (UNVERIFIED-ON-DEVICE)

```sh
sudo lucifer-install update                                      # download the latest stable release
sudo lucifer-install update --version v1.1.0                     # or a named release
sudo lucifer-install update lucifer-1.1.0-linux-arm64.tar.gz     # offline: local archive, checksum file beside it
sudo lucifer-install list                                        # releases, current marked *
sudo lucifer-install rollback                                    # previous release
sudo lucifer-install rollback lucifer-1.0.0-linux-arm64          # or a named one from list
sudo lucifer-install rollback --adopt                            # only after a refusal for hand-edited unit files
```

`update` accepts the same `--checksum`, `--target-rid` and `--adopt` options as `install`. Order of events: validate the archive; extract to a hidden staging directory; run the staged release's `lucifer-maint check` against the live configuration **while the service keeps playing** (a rejection aborts with nothing changed); then, under a lock, stop the service only if it was active, install the new unit and tmpfiles snippet, `daemon-reload`, switch `/opt/lucifer/current` atomically (new symlink, then rename), run the check again, and start the service only if it was active before. A service that was inactive stays inactive and an enabled/disabled state is not changed. A hand-edited unit or tmpfiles file is refused unless `--adopt`; `rollback` applies the same refusal, so `sudo lucifer-install rollback --adopt [RELEASE]` replaces edited files and keeps the originals in `/opt/lucifer/backups`.

If any step after the stop fails, the previous release link, the exact previous unit and tmpfiles contents (or their absence) are put back, systemd is reloaded, the service is started again if it had been active, and the half-installed release directory is removed (exit 1). The same restoration happens on SIGINT, SIGTERM or SIGHUP, so the same command can be retried at once. A restored tmpfiles snippet is re-applied with `systemd-tmpfiles --create`.

### After a crash (SIGKILL, power loss)

The script cannot undo a hard kill, and crash atomicity is not claimed or tested. What a kill before the switch leaves: the old release still current, possibly the new release's unit and tmpfiles files installed, the service possibly stopped, and a `.pending-*` marker. A kill after the switch leaves the new release current and complete (its marker is removed at the switch, so it is never cleaned up as a leftover). Recover like this:

1. `sudo lucifer-install list` shows the current release; `systemctl status lucifer` and `systemctl cat lucifer` show the service and unit.
2. Retry the **same** `update` (or `install`) archive: the interrupted release directory is recognised by its marker and replaced, stale staging is removed, and the unit already matching the new package is accepted. A different version, or a `rollback`, is refused with exit 3 because the installed unit matches no packaged copy of the current release; add `--adopt` to proceed. This is safe here: `--adopt` stores the unit it replaces in `/opt/lucifer/backups` (the interrupted package's copy, not a hand edit).
3. The unit and tmpfiles contents from before the interrupted run are kept in `/opt/lucifer/backups/interrupted-*` (the run's `unit` and `tmpfiles` files); copy them back only if you want exactly that older state, then `sudo systemctl daemon-reload`.
4. The retry does not know the service was running before the kill and will not restart it. If it was playing, run `sudo systemctl start lucifer` afterwards (or `systemctl is-active lucifer` to check).

Releases that completed a switch are never removed by this cleanup; a release directory without an interruption marker is never deleted, and the command tells you to remove it by hand. If the old service cannot be restarted the installer says so; look at `systemctl status lucifer`. Configuration, catalogue, input map and all earlier releases are never modified, and `rollback` uses the same transaction (it installs that release's own unit files). Old releases stay until you delete them from `/opt/lucifer/releases` (never delete the current or previous one).

Manual equivalent if the installer is unavailable: extract the archive to `/opt/lucifer/releases`, stop the service, `ln -sfn` the link, reinstall the unit if it changed, `daemon-reload`, start.

## 9. Uninstall

```sh
sudo systemctl disable --now lucifer
sudo rm /etc/systemd/system/lucifer.service /etc/tmpfiles.d/lucifer.conf /etc/udev/rules.d/99-lucifer-nfc.rules
sudo systemctl daemon-reload
sudo rm -rf /opt/lucifer
# Optional, destroys configuration and the catalogue:
sudo rm -rf /etc/lucifer /var/lib/lucifer && sudo userdel lucifer
```

## What is and is not proven

Checked on a development machine: the package builds and unpacks, the checksum verifies, `deploy/test-lucifer-install.sh` exercises install, update and rollback in a temporary root with stubbed `systemctl`, `useradd` and friends (stubs are not physical evidence), the unit passes `systemd-analyze verify` where available, the CI runs build and tests. Not proven: anything on the Pi (architecture choice, native dependencies, boot start, device groups, udev, ALSA name, KillMode=mixed behaviour with a real mpv, real installation, update and rollback). CI does not prove hardware.
