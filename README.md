# Lucifer

Lucifer is a physical album player. A programmed NFC object selects an album. Navidrome supplies the tracks and streams. A persistent mpv process plays the audio through the local DAC.

The first device uses a Raspberry Pi 3B, a PN532 V3 reader through a CH341 USB-to-TTL adapter, and a FiiO K5 Pro. The application is C#.

## Current status

The repository contains a playback prototype and a separate NFC writer source file. The player reads NDEF text with the format `amp:album:1:mbid:<release-uuid>`. It currently has two album mappings in C#.

The NFC writer now lives in the application as `tag write`. A configurable catalogue, recovery behaviour, controls, and service deployment are part of the v1 completion work.

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
| `check` | `NAVIDROME_*` | Validates configuration by field. Contacts no reader or server. |
| `play` | reader, mpv, DAC, Navidrome | The catalogue is still compiled in. |
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
| `LUCIFER_CATALOGUE_PATH` | `catalogue.json` (reserved for the external catalogue) |
| `LUCIFER_READER_TIMEOUT_SECONDS` | 30 (wait for a tag in `tag read`/`tag write`) |
| `LUCIFER_MPV_START_TIMEOUT_SECONDS` | 5 |
| `LUCIFER_HTTP_TIMEOUT_SECONDS` | 15 |

### Reader ownership and tag maintenance

One process owns the reader. `play`, `tag read`, and `tag write` take a lock file named for the reader in `LUCIFER_IPC_DIR`; a second owner is refused with an error naming the reader. The lock is released when the owner exits. Run every process that uses a reader with the same `LUCIFER_IPC_DIR`.

To program or inspect a tag while the player service is installed:

1. Stop the player service.
2. Run `tag write --release <uuid>` (or `tag read`).
3. Start the player service.

Service installation is part of later work; until then stop the running `play` process.

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
