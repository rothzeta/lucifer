# Installing Lucifer on the Pi

Everything here that needs the Pi is marked **UNVERIFIED-ON-DEVICE**. The package build and the unit syntax are checked on a development machine; no installation on a Pi has been performed or is claimed. Real results belong to LUC-08 (baseline) and LUC-12 (release gate); these steps feed A02 and A23.

Target architecture is not chosen here. Determine it on the Pi (LUC-08) and build for that:

| Pi userland | Command to check | RID |
| --- | --- | --- |
| 64-bit | `dpkg --print-architecture` prints `arm64` (or `uname -m` prints `aarch64`) | `linux-arm64` |
| 32-bit | `dpkg --print-architecture` prints `armhf` | `linux-arm` |

`uname -m` can report a 64-bit kernel on a 32-bit userland, so trust `dpkg --print-architecture`. Compatibility of either package with the Pi is not established until it runs there. Native AOT is not used.

## 1. Build the package (development machine)

Needs the .NET 10 SDK and network or a populated NuGet cache for the runtime pack.

```sh
deploy/publish.sh --rid linux-arm64 --version 1.0.0   # or --rid linux-arm
```

Output in `dist/`: `lucifer-<version>-<rid>.tar.gz` and `lucifer-<version>-<rid>.tar.gz.sha256`. The archive contains `bin/` (self-contained app plus `lucifer-maint`), `config/` (env, catalogue and input-map examples), `systemd/`, `udev/`, `VERSION`, `RID` and this file. Without `--version` the name comes from `git describe`.

Copy both files to the Pi (for example with `scp`).

## 2. Prerequisites on the Pi (UNVERIFIED-ON-DEVICE)

- `mpv` installed and on the service's `PATH` (or set `LUCIFER_MPV_PATH`).
- Native libraries the self-contained app needs; check after unpacking with `ldd /opt/lucifer/current/bin/Amp.Nfc | grep "not found"` and `ldd .../libcoreclr.so`. Typical Debian packages: `libicu*`, `libssl3`, `zlib1g`. If ICU is absent, set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` in the env file.
- A transport controller visible as a Linux input device (optional; controls stay off without it). See the Transport controls part of section 3.
- Network reachability of Navidrome and a Navidrome account for this device. Credentials go only in `/etc/lucifer/lucifer.env`.

## 3. First install (UNVERIFIED-ON-DEVICE)

```sh
sha256sum -c lucifer-1.0.0-linux-arm64.tar.gz.sha256     # must print OK
sudo useradd --system --home-dir /var/lib/lucifer --create-home --shell /usr/sbin/nologin lucifer
sudo usermod -aG dialout,audio,input lucifer             # also set by the unit; needed for maintenance runs
sudo install -d -o root -g root /opt/lucifer/releases
sudo tar -xzf lucifer-1.0.0-linux-arm64.tar.gz -C /opt/lucifer/releases
sudo ln -sfn /opt/lucifer/releases/lucifer-1.0.0-linux-arm64 /opt/lucifer/current
R=/opt/lucifer/current
sudo install -d -o root -g lucifer -m 0750 /etc/lucifer
sudo install -o root -g lucifer -m 0640 $R/config/lucifer.env.example /etc/lucifer/lucifer.env
sudo install -o lucifer -g lucifer -m 0640 $R/config/catalogue.example.json /var/lib/lucifer/catalogue.json
sudo $EDITOR /etc/lucifer/lucifer.env                    # credentials, LUCIFER_AUDIO_DEVICE, LUCIFER_NFC_DEVICE
sudo install -m 0644 $R/systemd/lucifer.service /etc/systemd/system/lucifer.service
sudo install -m 0644 $R/systemd/lucifer.tmpfiles.conf /etc/tmpfiles.d/lucifer.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/lucifer.conf
sudo -u lucifer $R/bin/lucifer-maint check               # validates config and catalogue
sudo systemctl daemon-reload
sudo systemctl enable --now lucifer
```

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

The previous release directory stays in `/opt/lucifer/releases`, so rollback is a symlink change.

```sh
sha256sum -c lucifer-1.1.0-linux-arm64.tar.gz.sha256
sudo tar -xzf lucifer-1.1.0-linux-arm64.tar.gz -C /opt/lucifer/releases
readlink /opt/lucifer/current                                    # note the prior release
sudo systemctl stop lucifer
sudo ln -sfn /opt/lucifer/releases/lucifer-1.1.0-linux-arm64 /opt/lucifer/current
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint check
sudo install -m 0644 /opt/lucifer/current/systemd/lucifer.service /etc/systemd/system/lucifer.service   # only if the unit changed
sudo systemctl daemon-reload && sudo systemctl start lucifer
```

Restore the prior package:

```sh
sudo systemctl stop lucifer
sudo ln -sfn /opt/lucifer/releases/lucifer-1.0.0-linux-arm64 /opt/lucifer/current   # the release noted above
sudo systemctl start lucifer
```

Configuration and the catalogue live outside the release directories and are untouched by either. If the prior unit file differed, reinstall it from the prior release before `daemon-reload`. Remove old releases only after the new one has been accepted.

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

Checked on a development machine: the package builds and unpacks, the checksum verifies, the unit passes `systemd-analyze verify` where available, the CI runs build and tests. Not proven: anything on the Pi (architecture choice, native dependencies, boot start, device groups, udev, ALSA name, KillMode=mixed behaviour with a real mpv, update and rollback). CI does not prove hardware.
