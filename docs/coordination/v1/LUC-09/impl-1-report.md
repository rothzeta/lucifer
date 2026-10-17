---
task: LUC-09-impl
status: complete
outcome: "Transport controls are connected end to end in service mode against fakes: an evdev record reader (64- and 32-bit layouts), an explicit key-mapping file with no built-in codes, an `input inspect` command, the transport policy in PlaybackOwner/Mpv, and PlaySession wiring with degradation or required-device failure. Verified only with regular files and FIFOs carrying input_event records and an in-process fake mpv peer; no real controller, evdev node, mpv, Pi or service."
artifacts:
  - Amp.Nfc/InputEvents.cs
  - Amp.Nfc/InputMapping.cs
  - Amp.Nfc/InputControls.cs
  - Amp.Nfc/InputInspect.cs
  - Amp.Nfc/Mpv.cs
  - Amp.Nfc/PlaybackOwner.cs
  - Amp.Nfc/PlaySession.cs
  - Amp.Nfc/Options.cs
  - Amp.Nfc/CommandLine.cs
  - Amp.Nfc/Cli.cs
  - Amp.Nfc/Player.cs
  - Amp.Nfc.Tests/InputAdapterTests.cs
  - Amp.Nfc.Tests/TransportControlTests.cs
  - Amp.Nfc.Tests/PlaySessionTests.cs
  - input-map.example.json
  - README.md
  - docs/coordination/v1/LUC-09/impl-1-report.md
  - branch ruach/luc-09-impl
candidate_revision: c312d7af6a2cd120b12f397e7d9d7a4cfbbc2f5a
tested_revision: c312d7af6a2cd120b12f397e7d9d7a4cfbbc2f5a
verification:
  - "tests written first (InputAdapterTests.cs, TransportControlTests.cs): did not compile before implementation (InputEvent, InputMapping, InputControls, EvdevInputDevice, Mode.InputInspect missing)"
  - "dotnet build Lucifer.slnx: 0 errors"
  - "dotnet test Lucifer.slnx: 380 passed, 0 failed (279 before + 101 new); full run repeated 4 times identically after the fixes below; no existing test changed or removed (PlaySessionTests.cs only gained 4 tests)"
  - "two flaky runs during development exposed a real race: status/pause preference came only from the asynchronous pause property event, so two controls in quick succession could see stale state; fixed in Mpv by applying the pause change as soon as mpv acknowledges it (events still say the same thing)"
  - "manual: built app `input inspect --device <file of 24-byte records>` printed press/release lines and exit 0; `--device rel` exit 2 before opening anything"
review: not-run
discoveries:
  - "Policy lives in Mpv.TransportAsync (documented there and on PlaybackOwner.TransportAsync), under the existing operation lock, so it is serialized with album replacement. Position comes from mpv get_property playlist-pos, playlist-count and time-pos queried inside the lock; if mpv does not report them, next/previous fall back to the previous raw playlist-next/prev force (keeps the LUC-07 behaviour and test)."
  - "Next on the last track sends `stop` and sets status Ended (a later end-file reason=stop for that entry does not demote Ended to Idle). Mpv keeps the last applied URL list; play or play/pause while Ended replays it through the same replace/append/unpause sequence and failure policy as a normal load."
  - "Next/Previous are ignored while Loading and Failed (only play/pause act during loading, as the board says). Toggle while Loading flips the pause preference."
  - "Repeat handling: only value 1 (press) acts, for every action; debounce is per key code, measured from the last accepted press, default 250 ms (LUCIFER_INPUT_DEBOUNCE_MS 0-5000). Queue of accepted controls is bounded at 8 (drop with a log line)."
  - "The evdev reader uses a blocking FileStream read on a dedicated background thread; Dispose does not wait for a read still blocked in the kernel. A device that ends (EOF/ENODEV) is logged as lost and controls stop; with LUCIFER_INPUT_REQUIRED=true that is a fatal run error."
  - "A configured LUCIFER_INPUT_MAP that is unreadable or invalid is a configuration error for `check` and `play` (exit 1), not a silent disable. Unset device or mapping disables controls with a log line and a `controls:` line in `check`."
  - "Service access line for deploy/lucifer.service (not edited, owned by another worker): in [Service] add `SupplementaryGroups=input` (or use a udev rule/ACL on a stable /dev/input/by-id path). Documented in README Transport controls."
  - "Example mapping input-map.example.json uses 164/163/165 (the Linux KEY_PLAYPAUSE/KEY_NEXTSONG/KEY_PREVIOUSSONG constants) purely as a marked placeholder; no code path defaults to them."
blockers: []
---

# LUC-09 implementer report

## What changed

- `InputEvents.cs`: `InputEvent`, `InputLayout` (24-byte records on 64-bit userland, 16 on 32-bit), `EvdevInputDevice`, `InputDevicePath.Validate`.
- `InputMapping.cs`: explicit `keys` file (`code` 0-767, `action` play|pause|toggle-pause|next|previous), field-path errors, no defaults.
- `InputControls.cs`: press-only dispatch, per-key debounce, ordered bounded queue into `PlaybackOwner`; `ControlsSetup`/`ControlsConfig` (startup degradation vs required).
- `InputInspect.cs` + `CommandLine`/`Cli`: `input inspect [--device] [--count]`; validated before opening, no reader lock, no server settings.
- `Mpv.TransportAsync`, `PlaybackOwner`: transport policy (see README "Transport controls").
- `Options`, `PlaySession`, `Player`: `LUCIFER_INPUT_DEVICE`, `_MAP`, `_REQUIRED`, `_DEBOUNCE_MS`; controls run in `play`; `check` shows a `controls:` line and warning.
- `input-map.example.json`, README section.

## A18 component coverage

| Case | Tests |
| --- | --- |
| normal (toggle, play, pause, next, previous through the device) | `TransportControlTests` toggle/next/previous, `A_mapped_press_reaches_the_player_through_the_device_adapter`, `Controls_run_inside_the_play_session_...` (PlaySession, shell mpv child + fake peer) |
| repeated (auto-repeat, bounce, order) | `Auto_repeat_never_advances_...`, `Presses_of_one_key_inside_the_debounce_window_...`, `Different_keys_are_not_debounced_...`, `Presses_are_applied_in_the_order_they_arrive`, `Release_unmapped_keys_and_non_key_events_do_nothing` |
| boundary | `Next_on_the_last_track_stops_...`, `Previous_on_the_first_track_restarts_it_...` |
| previous > 3 s vs <= 3 s | `Previous_after_more_than_three_seconds_...`, `Previous_within_three_seconds_...` (3.0, 2.9, 0.0) |
| paused | `Next_and_previous_while_paused_keep_it_paused_...` |
| loading | `During_loading_play_and_pause_...`, `During_loading_toggle_flips_...`, `A_new_album_starts_unpaused_but_a_later_explicit_pause_overrides_it` |
| ended | `Play_after_the_album_ended_restarts_...`, `Toggle_after_the_album_ended_...`, `Restarting_after_next_on_the_last_track_...` |
| during replacement / no album | `A_control_pressed_during_replacement_...`, `A_press_during_album_replacement_waits_...`, `With_no_applied_album_every_control_does_nothing`, `A_control_pressed_with_no_album_...` |
| layouts / decoding | `Record_sizes_...`, `Decode_...` and device-file tests for 32- and 64-bit |
| mapping / options / CLI / check | `InputAdapterTests` |
| degradation | `Missing_device_at_startup_...`, `A_missing_input_device_is_a_logged_degradation_...`, `..._fails_startup_when_controls_are_required_...`, `Losing_a_required_input_device_...` |

Volume is not handled by the application (DAC knob).

## What these tests do not prove

Fake events and a fake mpv do not prove hardware support, real evdev semantics (permissions, blocking reads, ENODEV on unplug), real mpv answers, or the installed service. Interactive mpv hotkeys do not count either.

## Device-pending (LUC-08 / LUC-12)

1. Which device node and EV_KEY codes the selected controller (KB1K if available) sends for each button; whether it sends press only, press/release, or auto-repeat; which of its several nodes is the right one; a stable by-id path.
2. The service user can open the node (`SupplementaryGroups=input` or equivalent) under the installed unit; behaviour after unplug/replug (device lost is logged and controls stay off until restart; with LUCIFER_INPUT_REQUIRED=true the service restarts).
3. The struct input_event size on the actual Pi userland (24 bytes for 64-bit, 16 for 32-bit; native size is used automatically).
4. mpv semantics assumed here: `get_property` playlist-pos / playlist-count / time-pos replies are numbers while playing; `seek 0 absolute` restarts the track and keeps the pause state; `playlist-next/prev force` keep the pause state; `stop` after the last track leaves mpv alive and idle; a loaded stream is seekable (a non-seekable stream may ignore the restart); `set_property pause` during loading holds the track paused once it loads.
5. End-to-end physical presses during real playback, and the service-mode result at LUC-12.

## Proposed board updates (not applied)

LUC-09 evidence: component results for A18 as above; LUC-08 should record the inspect output; LUC-11/deploy needs `SupplementaryGroups=input` and the LUCIFER_INPUT_* environment entries.

## Validation

Validated with `bun <ruach>/skills/ruach-handoff/scripts/validate.ts docs/coordination/v1/LUC-09/impl-1-report.md --repo .`; the result is in the final message.
