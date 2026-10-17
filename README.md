# Lucifer

Lucifer is a physical album player. A programmed NFC tag or ring selects an album: the tag carries an exact MusicBrainz release ID, a catalogue file maps it to a Navidrome album, Navidrome supplies the tracks, and a persistent mpv process plays them through the local DAC.

```text
NFC tag -> PN532 reader -> release UUID -> catalogue.json -> Navidrome album -> mpv -> DAC
```

The first device is planned as a Raspberry Pi 3B with a PN532 V3 reader behind a CH341 USB-to-TTL adapter and a FiiO K5 Pro DAC. The application is C# (.NET 10), one executable with several modes.

## Status

- **v1 software is delivered** (branch `ruach/lucifer-v1-orchestrator`): reader ownership, tag writer, catalogue, Navidrome playback, transport controls, recovery and shutdown policy, and packaging with a systemd unit. The build has 0 errors (2 known CA1416 warnings in the test project) and 390 automated tests pass, using fakes and fixtures only.
- **v1 is not complete.** Device acceptance is open: nothing has been installed or run on a Pi, with the real reader, a real mpv, a DAC or a live Navidrome. The device baseline (LUC-08) and release gate (LUC-12) are pending. The pass/fail record is [blackboards/ACCEPTANCE.md](blackboards/ACCEPTANCE.md); automated passes there are not device passes.

Statements below marked *planned* or *unverified* come from the repository's planning documents, not from a measured device.

## Requirements

| Need | Detail |
| --- | --- |
| Hardware (planned, unverified) | Raspberry Pi 3B; PN532 V3 NFC reader on a CH341 USB-to-TTL adapter; FiiO K5 Pro USB DAC; NFC tag or ring (the writer prompt names NTAG215, to be verified). Optional transport controller as a Linux input device (a "KB1K" was discussed). See [Hardware and software inventory](#hardware-and-software-inventory). |
| On the Pi | `mpv` on the service's `PATH` (or `LUCIFER_MPV_PATH`); a Navidrome server and account reachable from the Pi. The packaged app is self-contained, so no .NET runtime is needed there. Native libraries (ICU, OpenSSL, zlib) are listed in [INSTALL.md](docs/install/INSTALL.md). |
| To build | .NET SDK 10 (developed with 10.0.110). Network or a populated NuGet cache. The app references Iot.Device.Bindings 4.2.0. |

## Build and test from source

```sh
dotnet build
dotnet test Amp.Nfc.Tests
```

If `dotnet` is installed per user rather than system-wide, point the shell at it first (the path below is one machine's choice; use your own):

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

The tests need no reader, mpv, DAC or server. They also run the built application as a process (help, invalid commands, configuration, reader ownership). CI runs build and tests (`.github/workflows/ci.yml`); CI does not prove hardware.

Run from source with `dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- <command>`. The built executable is named `Amp.Nfc`; the help text and examples use that name.

## Commands

| Command | Needs | Notes |
| --- | --- | --- |
| `help` | nothing | Lists all commands and settings. |
| `check` | `NAVIDROME_*`, catalogue file | Validates configuration, catalogue and input map by field. Contacts no reader, input device or server. |
| `play` | reader, mpv, DAC, Navidrome, catalogue | Runs the player. Refuses to start if configuration or catalogue is invalid. |
| `tag read` | reader | Prints the tag and its NDEF text. No server credentials. |
| `tag write --release <uuid>` | reader | Writes `amp:album:1:mbid:<uuid>` and reads it back. No server credentials. |
| `input inspect [--device <path>] [--count <n>]` | input device | Prints input events to find key codes. Takes no reader lock. |

Arguments are parsed and configuration validated before any device is opened. Invalid commands print the help to stderr and exit 2. Configuration errors name the failing field and never print credentials or stream URLs.

```sh
Amp.Nfc help
Amp.Nfc check
Amp.Nfc tag read
Amp.Nfc tag write --release a24495e4-eb70-48cd-b0fa-31a606459d26
Amp.Nfc input inspect --device /dev/input/by-id/<name> --count 10
Amp.Nfc play
```

After `dotnet build` the binary is `Amp.Nfc/bin/Debug/net10.0/Amp.Nfc`. Run from the repository root with `NAVIDROME_URL=http://x:4533 NAVIDROME_USER=x NAVIDROME_PASSWORD=x LUCIFER_CATALOGUE_PATH=catalogue.example.json LUCIFER_IPC_DIR=/tmp` set (other settings at their defaults), `check` prints:

```text
Configuration is valid.
  reader:    /dev/ttyUSB0
  ipc dir:   /tmp
  mpv:       mpv (alsa/default:CARD=Pro)
  server:    http://x:4533 (credentials set)
  catalogue: catalogue.example.json (2 albums)
  controls:  disabled: LUCIFER_INPUT_DEVICE and LUCIFER_INPUT_MAP are not set. ...
No reader, input device or server was contacted; album IDs are not verified against the server.
```

### Maintenance workflow: program a tag

Only one process may own the reader (`play`, `tag read` and `tag write` take a lock file in `LUCIFER_IPC_DIR`; a second owner is refused with an error naming the reader). To program or inspect a tag while the service is installed:

```sh
sudo systemctl stop lucifer
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint tag write --release <release-uuid>
sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint tag read      # optional read-back
sudo systemctl start lucifer
```

`lucifer-maint` loads the service's environment file so `LUCIFER_IPC_DIR` and the lock are shared. Without the unit, stop the running `play` process instead and run the command directly.

## Configuration

Set `NAVIDROME_URL`, `NAVIDROME_USER` and `NAVIDROME_PASSWORD` in the process environment for `play` and `check` (they are not read by `tag` or `input inspect`). `NAVIDROME_URL` must be an absolute http or https URL with a host and no embedded credentials. Optional settings use their default when unset; a blank value is an error. In the service these live in `/etc/lucifer/lucifer.env` (template: [deploy/lucifer.env.example](deploy/lucifer.env.example)), which holds the password and must never be committed.

| Variable | Default | Meaning |
| --- | --- | --- |
| `NAVIDROME_URL` | required | Server base URL. |
| `NAVIDROME_USER` | required | Account name. |
| `NAVIDROME_PASSWORD` | required | Account password. |
| `LUCIFER_NFC_DEVICE` | `/dev/ttyUSB0` | Serial path of the PN532 adapter. Prefer a stable path (see INSTALL.md). |
| `LUCIFER_MPV_PATH` | `mpv` | mpv executable (`play`, `check`). |
| `LUCIFER_AUDIO_DEVICE` | `alsa/default:CARD=Pro` | mpv audio device. The default is a repository default, unverified on any device; list real ones with `mpv --audio-device=help`. |
| `LUCIFER_IPC_DIR` | `$XDG_RUNTIME_DIR/lucifer`, else `/tmp/lucifer-<user>` | Directory for the mpv socket and the reader lock. The default is created with mode 0700 and refused if it is a symlink, has another mode or is not writable by the user; an explicit value is used as given. Must be identical for the service and maintenance runs (the package uses `/run/lucifer`). |
| `LUCIFER_CATALOGUE_PATH` | `catalogue.json` | Release UUID to Navidrome album mapping file. |
| `LUCIFER_READER_TIMEOUT_SECONDS` | `30` | Wait for a tag in `tag read` and `tag write`. |
| `LUCIFER_MPV_START_TIMEOUT_SECONDS` | `5` | mpv start deadline. |
| `LUCIFER_HTTP_TIMEOUT_SECONDS` | `15` | Navidrome request deadline. |
| `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS` | `10` | Graceful-stop deadline after SIGTERM or Ctrl+C. 1 to 25, so the unit's `TimeoutStopSec=30` always exceeds it. |
| `LUCIFER_ABSENCE_CONFIRM_MS` | `1500` | 100 to 60000. How long the reader must report no tag before the object counts as removed. |
| `LUCIFER_INPUT_DEVICE` | none | Absolute path of the Linux input device for transport controls (also the default for `input inspect`). |
| `LUCIFER_INPUT_MAP` | none | Key mapping file. Controls are off unless both device and map are set. |
| `LUCIFER_INPUT_REQUIRED` | `false` | `true` makes a missing or lost input device fatal; then device and map are required. |
| `LUCIFER_INPUT_DEBOUNCE_MS` | `250` | 0 to 5000. Contact-bounce window per key. |

Fixed deadlines (not configurable): talking to mpv over its IPC socket allows 2 s to connect, 2 s to send a command and 5 s for its reply; a stream that mpv accepted but does not start playing within 20 s fails the album load (status Failed). These are `MpvTimeouts.Default` in `Amp.Nfc/Mpv.cs`. The reader supervisor thresholds are code defaults too (`SupervisorPolicy`).

`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is a .NET setting, not a Lucifer one; use it only if the Pi lacks ICU and the app fails to start.

## Catalogue and input map

### Catalogue

The catalogue maps a canonical MusicBrainz release UUID (the value on the tag) to a Navidrome album ID. It is a JSON file at `LUCIFER_CATALOGUE_PATH`; [`catalogue.example.json`](catalogue.example.json) is a starting point and holds two earlier built-in mappings as **migration examples only**. Their availability on any server is not confirmed.

```json
{
  "source": "navidrome",
  "albums": {
    "<release-uuid>": { "navidromeId": "<album id>", "artist": "<display>", "title": "<display>" }
  }
}
```

`source` must be `navidrome`. `artist` and `title` are display text only; only `navidromeId` locates audio. Keys are case-insensitive UUIDs.

Add or edit an album:

1. Find the album in Navidrome and copy its album ID. Confirm it is the intended edition (compare the release MBID if the server shows one; otherwise record the mapping deliberately rather than guessing by title).
2. Add or change the entry under `albums`.
3. Run `check`. It reports every problem with a field path such as `albums.<uuid>.navidromeId`: malformed or empty UUIDs, duplicate keys (including case variants), blank or missing fields, wrong `source`, unreadable or non-JSON file.
4. No rebuild or restart is needed: `play` re-reads the file on every tap. If the file has become invalid, a tap reports the errors and the current album keeps playing.
5. Program a tag for that release UUID (maintenance workflow above).

A release not in the file reports `not in the catalogue`; an ID that Navidrome says does not exist reports a stale mapping naming the ID and release. Both leave the current album playing. `check` does not contact the server, so a stale ID is found only on a tap until live server verification (LUC-06, LUC-12) is done.

### Input map

[`input-map.example.json`](input-map.example.json) maps EV_KEY codes to `play`, `pause`, `toggle-pause`, `next` or `previous`. Its codes are placeholders, verified for no controller; find real ones with `input inspect`. Details are in Transport controls below.

## Transport controls

`play` can read play/pause, next and previous from one Linux input device (evdev). Volume is not handled by the application; use the DAC's own knob. The controller and its event codes are not known yet (hardware baseline LUC-08), so **there are no default codes**: controls stay off until you give both a device and a mapping file, and `check` and the `play` start-up log say so (`controls: disabled: ...`).

1. Find the codes. With the controller attached, run `Amp.Nfc input inspect --device /dev/input/event3` (or set `LUCIFER_INPUT_DEVICE`; `--count <n>` stops after n events) and press each button. It prints `EV_KEY code=164 value=1 (press)` lines (sync records are hidden); value 1 is press, 0 release, 2 auto-repeat. It validates its arguments first, only reads the device, needs no server settings and takes no reader lock, so it can run while the player service is up. Prefer a stable `/dev/input/by-id/...` path over `eventN`.
2. Write a mapping from [`input-map.example.json`](input-map.example.json) (its codes are placeholders, not verified for any controller): `keys` is a list of `{ "code": <EV_KEY code 0-767>, "action": "play" | "pause" | "toggle-pause" | "next" | "previous" }`. A code may be mapped once. Only EV_KEY events are used. `check` validates the file (a configured but invalid file is an error) without opening the device.
3. Set `LUCIFER_INPUT_DEVICE` and `LUCIFER_INPUT_MAP`. A device that cannot be opened at start-up (or later disappears) is logged and the player keeps working without controls, unless `LUCIFER_INPUT_REQUIRED=true`, which makes it a start-up error or a fatal run error (exit 1, systemd restarts).
4. Service access: the service user must be allowed to read the device node, typically membership of the `input` group. The packaged unit (`deploy/lucifer.service`) already has `SupplementaryGroups=input`; maintenance runs as the `lucifer` user need the group too (INSTALL.md). With a udev rule on a by-id path, grant access to that node instead.

Policy (proposed v1):

- play/pause toggles the current track (`play` and `pause` only ever set the state; they are ignored when already in it);
- next advances; on the last track it stops at the album end (it does not wrap or repeat);
- previous restarts the current track when it has played more than 3 s, otherwise goes to the previous track; on the first track it always restarts it;
- play (or play/pause) after the album ended restarts the selected album from its first track;
- next and previous keep the pause state; during stream loading play/pause only change the pause preference and next/previous are ignored;
- with no applied album, or after a failed load, controls do nothing; a new album always starts unpaused;
- controls received while an album is being replaced wait on the shared operation lock and then act on the new album, subject to the loading rule above: play/pause apply, but next and previous are ignored while the new album is still loading (start of the stream, and each track change); press them again once playback has started.

Repeats: only a key press acts. Auto-repeat (value 2) and release (value 0) are ignored for every action, so holding next does not skip through the album and holding play/pause does not flicker. A second press of the same key within `LUCIFER_INPUT_DEBOUNCE_MS` of the last accepted press is dropped as contact bounce; different keys are independent. At most 8 controls wait for mpv; more are dropped with a log line.

Each control is logged (`Control next: applied` or `ignored`). Interactive mpv hotkeys prove nothing about this path, and the tests use fake events and a fake mpv: they do not prove hardware support. What the Pi must confirm is listed in the LUC-09 report.

## Shutdown and recovery

`play` stops gracefully on SIGTERM (service stop) or Ctrl+C: it cancels pending album work, closes the reader and mpv IPC, stops the mpv process it started, removes its own mpv socket, releases the reader lock (the lock file itself stays in place; deleting it would reopen a race between owners), and exits 0. This must finish within `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS`; if it overruns, owned resources are released anyway and the process exits 1.

mpv is started with `--no-config --ytdl=no --no-terminal --msg-level=all=no` (so no system or user mpv config, script or log file applies and no URL is handed to yt-dlp) and its output is read and discarded, because its `Playing:` lines carry authenticated stream URLs; failure text that echoes a URL is redacted. If mpv exits after a stop was requested, the run still ends normally (exit 0). The packaged unit uses `KillMode=mixed`: SIGTERM goes to the app, which stops its own mpv; anything still in the control group is SIGKILLed once the app has exited (or when `TimeoutStopSec` runs out if the app hangs).

Supervisor policy (exit 1 so systemd restarts the group; a restart rescans, and a tag still present is selected fresh):

- the mpv process it started exits, or its IPC connection is lost;
- the reader reports transport faults continuously (at least 20 over at least 10 s), or an explicit idle health probe (PN532 firmware query, every 60 s while no tag is present) fails 3 times in a row.

Not fatal: a long idle period with no tag, transient Navidrome/network/album failures (retried within bounded attempts, then suppressed until the tag is presented again), and failed stream loads. The PN532 binding can return null for some transport failures, so only the health probe can catch those. Policy thresholds are code defaults, not options. Unplug/replug behaviour and systemd recovery are not yet verified on a device (LUC-08, LUC-12).

## Packaging and installation

`deploy/publish.sh --rid linux-arm64|linux-arm [--version V] [--out dir]` builds a self-contained, versioned archive plus a SHA-256 file. The RID must match the Pi's userland (`dpkg --print-architecture`: `arm64` gives `linux-arm64`, `armhf` gives `linux-arm`), which is not yet recorded (LUC-08). The archive holds `bin/` (app and `lucifer-maint`), `config/` examples, `systemd/`, `udev/`, `VERSION`, `RID` and INSTALL.md.

Create the `lucifer` user, unpack to `/opt/lucifer/releases`, install the env file, catalogue, unit and tmpfiles snippet, run `lucifer-maint check`, then `systemctl enable --now lucifer`. The exact steps, health check, update, roll-back and uninstall are in [docs/install/INSTALL.md](docs/install/INSTALL.md). **Nothing there has been run on a Pi.**

## Hardware and software inventory

The target device has not been inspected. The *Planned/assumed* column is taken only from repository documents and is unverified; *Actual* is to be filled from the Pi by LUC-08. Do not guess entries.

| Item | Planned/assumed | Actual (to fill from the Pi) | How to collect |
| --- | --- | --- | --- |
| Pi model | Raspberry Pi 3B (planned, unverified) | _pending (LUC-08)_ | `cat /proc/device-tree/model` |
| OS and userland architecture | not chosen; the package RID is derived from it | _pending (LUC-08)_ | `uname -m`; `dpkg --print-architecture` (trust this one); `cat /etc/os-release` |
| .NET runtime and publish RID | none required on the Pi (self-contained package); RID `linux-arm64` or `linux-arm` to be chosen from the architecture | _pending (LUC-08)_ | `cat /opt/lucifer/current/RID` after install; `ldd /opt/lucifer/current/bin/Amp.Nfc \| grep "not found"` |
| mpv version | any mpv on `PATH` (planned, version unspecified) | _pending (LUC-08)_ | `mpv --version \| head -1` |
| Navidrome version | server reachable from the Pi (planned, version unspecified) | _pending (LUC-08)_ | Navidrome web UI About page, or the server's package or container version |
| PN532 board, firmware, UART mode | PN532 V3 behind the USB-to-TTL adapter (planned, unverified) | _pending (LUC-08)_ | firmware line printed by `Amp.Nfc tag read` (`PN532 detected. Version=...`); board switch settings by inspection |
| USB-to-TTL adapter | CH341 (planned); the udev example assumes common IDs 1a86:7523, not read from the adapter | _pending (LUC-08)_ | `lsusb`; `udevadm info -a -n /dev/ttyUSB0`; `ls -l /dev/serial/by-id/` (record the stable path) |
| NFC tag or ring chip | NTAG215 (named in the writer's prompt; verify) | _pending (LUC-08)_ | `Amp.Nfc tag read` output with the actual object on the reader |
| DAC | FiiO K5 Pro (planned) | _pending (LUC-08)_ | `aplay -l`; `mpv --audio-device=help`; the exact id set as `LUCIFER_AUDIO_DEVICE` |
| Control device | KB1K discussed, not selected | _pending (LUC-08)_ | `ls -l /dev/input/by-id/`; `Amp.Nfc input inspect --device <path>` while pressing each button |
| Service account and groups | user `lucifer` with groups `dialout`, `audio`, `input` (from the packaged unit and INSTALL.md) | _pending (LUC-08)_ | `id lucifer`; `systemctl show lucifer -p User -p SupplementaryGroups` |

Read-only collection script, to run on the Pi (it changes nothing; omit lines for software not yet installed). The script and `tag read` print USB and tag serial numbers, so review the output before pasting it into a public file.

```sh
cat /proc/device-tree/model; echo
uname -m; dpkg --print-architecture; cat /etc/os-release
mpv --version | head -1
lsusb
ls -l /dev/serial/by-id/ /dev/ttyUSB* /dev/input/by-id/ 2>&1
udevadm info -a -n /dev/ttyUSB0 | grep -E 'idVendor|idProduct|serial|KERNELS'
aplay -l
mpv --audio-device=help
id lucifer
cat /opt/lucifer/current/RID /opt/lucifer/current/VERSION 2>&1
```

Paste the results into [blackboards/ACCEPTANCE.md](blackboards/ACCEPTANCE.md) (release-candidate table). Include no credentials: leave out the env file, the Navidrome password and any URL that carries one.

## Troubleshooting

- Run `check` first: it names every invalid variable or catalogue field.
- Exit code 2 means invalid arguments, or configuration errors for `play`, `tag` and `input inspect` (`check` exits 1 for configuration errors). `check` and `play` also exit 1 on catalogue or input-map errors; exit 1 after an mpv or reader fault is the designed fail-and-restart behaviour under systemd.
- Reader refused ("already owned by another Lucifer process"): the service or another `tag` run holds the lock. Stop the service, and use the same `LUCIFER_IPC_DIR`.
- Reader missing or silent: confirm `LUCIFER_NFC_DEVICE` exists (`ls -l /dev/ttyUSB* /dev/serial/by-id/`), the user is in `dialout`, and `dmesg | tail` shows the adapter.
- No sound: `mpv --audio-device=help` as the service user, compare with `LUCIFER_AUDIO_DEVICE`, and check the `audio` group.
- A tap does nothing or reports `not in the catalogue` / stale mapping: fix the catalogue entry (above).
- Journal: `journalctl -u lucifer -b`. mpv's own output is deliberately discarded because it contains authenticated stream URLs.
- More cases, including restart-limit recovery, are in [docs/install/INSTALL.md](docs/install/INSTALL.md) (section 7).

## Project layout

| Path | Contents |
| --- | --- |
| `Amp.Nfc/` | The application (C#, .NET 10): command line, options, catalogue, Navidrome, mpv, reader, tags, input, lifecycle. |
| `Amp.Nfc.Tests/` | Automated tests (fakes for reader, mpv and server). |
| `Lucifer.slnx` | Solution. |
| `catalogue.example.json`, `input-map.example.json` | Example catalogue and input map (placeholders). |
| `deploy/` | `publish.sh`, `lucifer.service`, `lucifer-maint`, env, udev and tmpfiles examples, maintenance script test. |
| `docs/install/INSTALL.md` | Pi installation, update, roll-back, uninstall. |
| `docs/reviews/` | Source review. |
| `docs/coordination/v1/` | Engineering coordination records. |
| `BLACKBOARD.md`, `blackboards/` | Plan, decisions and per-area task boards; `ACCEPTANCE.md` is the release evidence record. |
| `.github/workflows/ci.yml` | CI build and tests. |

## Project records

- [v1 blackboard](BLACKBOARD.md) (goal, decisions, execution plan) and the [source review](docs/reviews/2026-10-17-v1-review.md).
- [Tags and catalogue](blackboards/TAGS.md), [Playback](blackboards/PLAYBACK.md), [Device and release](blackboards/DEVICE.md), [Acceptance cases and results](blackboards/ACCEPTANCE.md).
- Coordination: [task record](docs/coordination/v1/task-record.md) (units, reviews, delivery, authority) and per-task assignments and reports under `docs/coordination/v1/<TASK>/`.
