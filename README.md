# Lucifer

Lucifer is a physical album player. A programmed NFC object selects an album. Navidrome supplies the tracks and streams. A persistent mpv process plays the audio through the local DAC.

The first device uses a Raspberry Pi 3B, a PN532 V3 reader through a CH341 USB-to-TTL adapter, and a FiiO K5 Pro. The application is C#.

## Current status

The repository contains a playback prototype and a separate NFC writer source file. The player reads NDEF text with the format `amp:album:1:mbid:<release-uuid>`. It currently has two album mappings in C#.

The writer at the repository root is not included in the application project. A reproducible writer command, configurable catalogue, recovery behaviour, controls, and service deployment are part of the v1 completion work.

Start with the [v1 blackboard](BLACKBOARD.md). The [source review](docs/reviews/2026-10-17-v1-review.md) explains the findings. The [acceptance record](blackboards/ACCEPTANCE.md) defines release evidence.

The [execution plan to v1](BLACKBOARD.md#execution-plan-to-v1) sequences the twelve tasks into target validation, album programming, reliable playback, controls and recovery, installation, and release acceptance. Start with the Pi baseline and LUC-01.

## Run the existing prototype

The current project targets .NET 10 and references Iot.Device.Bindings 4.2.0. It requires mpv, the configured serial reader, an accessible Navidrome server, and the selected ALSA audio output.

Set `NAVIDROME_URL`, `NAVIDROME_USER`, and `NAVIDROME_PASSWORD` in the process environment. The reader, audio output, and socket paths are currently constants in `Amp.Nfc/Program.cs`.

```sh
dotnet run --project Amp.Nfc/Amp.Nfc.csproj
```

This command describes the checked-in player. The planned `play`, `tag read`, `tag write`, and `check` modes are not implemented yet.

## Completion records

- [Tags and catalogue](blackboards/TAGS.md)
- [Playback](blackboards/PLAYBACK.md)
- [Device and release](blackboards/DEVICE.md)
- [Acceptance cases and results](blackboards/ACCEPTANCE.md)

The review baseline is commit `38c01feb7b528088dff57e3e95374c75e0bb3be8`. No successful build or device test is claimed by the initial documentation review.
