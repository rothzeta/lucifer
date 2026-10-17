# Lucifer

Lucifer is a physical album player. A programmed NFC object selects an album. Navidrome supplies the tracks and streams. A persistent mpv process plays the audio through the local DAC.

The first device uses a Raspberry Pi 3B, a PN532 V3 reader through a CH341 USB-to-TTL adapter, and a FiiO K5 Pro. The application is C#.

## Current status

The repository contains a playback prototype and a separate NFC writer source file. The player reads NDEF text with the format `amp:album:1:mbid:<release-uuid>`. Album mappings live in an external catalogue file (see [Catalogue](#catalogue)).

The NFC writer now lives in the application as `tag write`. Recovery behaviour, controls, and service deployment are part of the v1 completion work.

Start with the [v1 blackboard](BLACKBOARD.md). The [source review](docs/reviews/2026-10-17-v1-review.md) explains the findings. The [acceptance record](blackboards/ACCEPTANCE.md) defines release evidence.

The [execution plan to v1](BLACKBOARD.md#execution-plan-to-v1) sequences the twelve tasks into target validation, album programming, reliable playback, controls and recovery, installation, and release acceptance. Start with the Pi baseline and LUC-01.

## Usage

The project targets .NET 10 and references Iot.Device.Bindings 4.2.0. One executable provides every mode:

```sh
dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- help
dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- check
dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- play
dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- tag read
dotnet run --project Amp.Nfc/Amp.Nfc.csproj -- tag write --release <uuid>
```

| Mode | Needs | Notes |
| --- | --- | --- |
| `help` | nothing | Lists all modes and settings. |
| `check` | `NAVIDROME_*`, catalogue file | Validates configuration and the catalogue by field. Contacts no reader or server. |
| `play` | reader, mpv, DAC, Navidrome, catalogue file | Refuses to start if the catalogue is missing or invalid. |
| `tag read` | reader | Prints the tag and its NDEF text. No server credentials. |
| `tag write --release <uuid>` | reader | Writes `amp:album:1:mbid:<uuid>`. No server credentials. |

Arguments are parsed and configuration validated before the reader is opened. Invalid commands print the help to stderr and exit 2; configuration errors name the failing field and never print credentials or stream URLs.

### Settings

Set `NAVIDROME_URL`, `NAVIDROME_USER`, and `NAVIDROME_PASSWORD` in the process environment for `play` and `check`. Optional settings fall back to defaults when unset; a blank value is an error.

| Variable | Default |
| --- | --- |
| `LUCIFER_NFC_DEVICE` | `/dev/ttyUSB0` |
| `LUCIFER_MPV_PATH` | `mpv` |
| `LUCIFER_AUDIO_DEVICE` | `alsa/default:CARD=Pro` |
| `LUCIFER_IPC_DIR` | `/tmp` (mpv socket and reader lock) |
| `LUCIFER_CATALOGUE_PATH` | `catalogue.json` (release UUID to Navidrome album ID; see Catalogue) |
| `LUCIFER_READER_TIMEOUT_SECONDS` | 30 (wait for a tag in `tag read`/`tag write`) |
| `LUCIFER_MPV_START_TIMEOUT_SECONDS` | 5 |
| `LUCIFER_HTTP_TIMEOUT_SECONDS` | 15 |
| `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS` | 10 (1 to 300; deadline for graceful shutdown after SIGTERM or Ctrl+C) |
| `LUCIFER_INPUT_DEVICE` | none (absolute path of the Linux input device for the transport controls; see Transport controls) |
| `LUCIFER_INPUT_MAP` | none (key mapping file; controls are off without it) |
| `LUCIFER_INPUT_REQUIRED` | `false` (`true` makes a missing or lost input device fatal) |
| `LUCIFER_INPUT_DEBOUNCE_MS` | 250 (0 to 5000) |

### Catalogue

The catalogue maps a canonical MusicBrainz release UUID (the value on the tag) to a Navidrome album ID. It is a JSON file at `LUCIFER_CATALOGUE_PATH`; [`catalogue.example.json`](catalogue.example.json) is a starting point and holds the two earlier built-in mappings as **migration examples only**. Their availability on any server is not confirmed until the real server confirms them.

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
2. Add or change the entry under `albums`. Changing the server-side ID of an album needs only this edit.
3. Run `check`. It reports every problem with a field path such as `albums.<uuid>.navidromeId`: malformed or empty UUIDs, duplicate keys (including case variants), blank or missing fields, wrong `source`, unreadable or non-JSON file.
4. No rebuild or restart is needed: `play` re-reads the file on every tap. If the file has become invalid, a tap reports the errors and the current album keeps playing.

Outcomes of a tap: a release not in the file reports `not in the catalogue`; an ID that Navidrome says does not exist (error 70) reports a stale mapping naming the ID and release. Both leave the current album playing. `check` does not contact the server, so a stale ID is only found on a tap until live server verification (LUC-06, LUC-12) is done.

### Transport controls

`play` can read play/pause, next and previous from one Linux input device (evdev). Volume is not handled by the application; use the DAC's own knob. The controller and its event codes are not known yet (hardware baseline LUC-08), so **there are no default codes**: controls stay off until you give both a device and a mapping file, and `check` and the `play` start-up log say so (`controls: disabled: ...`).

1. Find the codes. With the controller attached, run `amp-nfc input inspect --device /dev/input/event3` (or set `LUCIFER_INPUT_DEVICE`; `--count <n>` stops after n events) and press each button. It prints `EV_KEY code=164 value=1 (press)` lines (sync records are hidden); value 1 is press, 0 release, 2 auto-repeat. It validates its arguments first, only reads the device, needs no server settings and takes no reader lock, so it can run while the player service is up. Prefer a stable `/dev/input/by-id/...` path over `eventN`.
2. Write a mapping from [`input-map.example.json`](input-map.example.json) (its codes are placeholders, not verified for any controller): `keys` is a list of `{ "code": <EV_KEY code 0-767>, "action": "play" | "pause" | "toggle-pause" | "next" | "previous" }`. A code may be mapped once. Only EV_KEY events are used. `check` validates the file (a configured but invalid file is an error) without opening the device.
3. Set `LUCIFER_INPUT_DEVICE` and `LUCIFER_INPUT_MAP`. A device that cannot be opened at start-up (or later disappears) is logged and the player keeps working without controls, unless `LUCIFER_INPUT_REQUIRED=true`, which makes it a start-up error or a fatal run error (exit 1, systemd restarts).
4. Service access: the service user must be allowed to read the device node, typically membership of the `input` group. The unit file needs the line `SupplementaryGroups=input` in `[Service]` (deploy/lucifer.service is owned elsewhere; this is the line to add). With a udev rule on a by-id path, grant access to that node instead.

Policy (proposed v1):

- play/pause toggles the current track (`play` and `pause` only ever set the state; they are ignored when already in it);
- next advances; on the last track it stops at the album end (it does not wrap or repeat);
- previous restarts the current track when it has played more than 3 s, otherwise goes to the previous track; on the first track it always restarts it;
- play (or play/pause) after the album ended restarts the selected album from its first track;
- next and previous keep the pause state; during stream loading play/pause only change the pause preference and next/previous are ignored;
- with no applied album, or after a failed load, controls do nothing; a new album always starts unpaused;
- controls received while an album is being replaced wait and then apply to the new album (they share one operation lock with album replacement).

Repeats: only a key press acts. Auto-repeat (value 2) and release (value 0) are ignored for every action, so holding next does not skip through the album and holding play/pause does not flicker. A second press of the same key within `LUCIFER_INPUT_DEBOUNCE_MS` of the last accepted press is dropped as contact bounce; different keys are independent. At most 8 controls wait for mpv; more are dropped with a log line.

Each control is logged (`Control next: applied` or `ignored`). Interactive mpv hotkeys prove nothing about this path, and the tests use fake events and a fake mpv: they do not prove hardware support. What the Pi must confirm is listed in the LUC-09 report.

### Reader ownership and tag maintenance

One process owns the reader. `play`, `tag read`, and `tag write` take a lock file named for the reader in `LUCIFER_IPC_DIR`; a second owner is refused with an error naming the reader. The lock is released when the owner exits. Run every process that uses a reader with the same `LUCIFER_IPC_DIR`.

To program or inspect a tag while the player service is installed:

1. Stop the player service.
2. Run `tag write --release <uuid>` (or `tag read`).
3. Start the player service.

With the packaged systemd unit use `lucifer-maint` so the same environment file and `LUCIFER_IPC_DIR` apply; see [docs/install/INSTALL.md](docs/install/INSTALL.md). Without the unit, stop the running `play` process.

### Shutdown and recovery

`play` stops gracefully on SIGTERM (service stop) or Ctrl+C: it cancels pending album work, closes the reader and mpv IPC, stops the mpv process it started, removes its own mpv socket and reader lock, and exits 0. This must finish within `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS`; if it overruns, owned resources are released anyway and the process exits 1.

Supervisor policy (exit 1 so systemd restarts the group; a restart rescans, and a tag still present is selected fresh):

- the mpv process it started exits, or its IPC connection is lost;
- the reader reports transport faults continuously (at least 20 over at least 10 s), or an explicit idle health probe (PN532 firmware query, every 60 s while no tag is present) fails 3 times in a row.

Not fatal: a long idle period with no tag, transient Navidrome/network/album failures (retried within bounded attempts, then suppressed until the tag is presented again), and failed stream loads. The PN532 binding can return null for some transport failures, so only the health probe can catch those. Policy thresholds are code defaults, not options. Unplug/replug behaviour and systemd recovery are not yet verified on a device (LUC-08, LUC-12).

### Packaging and installation

`deploy/publish.sh --rid linux-arm64|linux-arm` builds a self-contained archive with a SHA-256 file; the RID must match the Pi userland, which is not yet recorded (LUC-08). The unit, env and udev examples, and install, update, roll-back and uninstall steps are in [docs/install/INSTALL.md](docs/install/INSTALL.md). Nothing there has been run on a Pi.

### Tests

```sh
dotnet build
dotnet test Amp.Nfc.Tests
```

The tests need no reader, mpv, DAC, or server. They run the built application as a process for help, invalid commands, configuration, and reader ownership.

## Completion records

- [Tags and catalogue](blackboards/TAGS.md)
- [Playback](blackboards/PLAYBACK.md)
- [Device and release](blackboards/DEVICE.md)
- [Acceptance cases and results](blackboards/ACCEPTANCE.md)

The review baseline is commit `38c01feb7b528088dff57e3e95374c75e0bb3be8`. No successful build or device test is claimed by the initial documentation review.
