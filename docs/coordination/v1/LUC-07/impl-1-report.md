---
task: LUC-07-impl
status: complete
outcome: "An album is now applied as one serialized operation: all tracks are prepared and validated first; the generation is checked immediately before the first mpv command; the replace/append sequence then runs to completion; a new album starts unpaused; a failure after the queue started to change reports Failed, stops the player to clear the queue and is recoverable by the next valid selection; player status comes from mpv events. Verified only against an in-process fake mpv peer and stubbed HTTP; no real mpv, audio, Navidrome, Pi or DAC."
artifacts:
  - Amp.Nfc/Mpv.cs
  - Amp.Nfc/PlaybackOwner.cs
  - Amp.Nfc/PlayerAlbumApplier.cs
  - Amp.Nfc/PlaySession.cs
  - Amp.Nfc.Tests/AlbumPlaybackTests.cs
  - docs/coordination/v1/LUC-07/impl-1-report.md
  - branch ruach/luc-07-impl
candidate_revision: 88e309971fa47972c5191a7131ccad7d0d3d123a
tested_revision: 88e309971fa47972c5191a7131ccad7d0d3d123a
verification:
  - "tests written first (AlbumPlaybackTests.cs, 20 cases): did not compile before implementation (PlaybackOwner, TransportCommand, MpvFailureKind.AlbumIncomplete, Mpv.LaunchArguments missing)"
  - "dotnet build Lucifer.slnx: 0 errors"
  - "dotnet test Lucifer.slnx: 279 passed, 0 failed (259 before + 20 new); full run repeated 3 times identically; no existing test changed or removed"
  - "one new test (transport serialized with replacement) failed once on first run because Pause was ignored while status was Loading; policy changed to allow Pause/TogglePause while Loading, test unchanged"
review: not-run
discoveries:
  - "Interfaces IAlbumApplier/IPreparedAlbum unchanged. The generation check uses the CancellationToken the controller already cancels in Supersede (synchronously, under its gate). Mpv.LoadAlbumAsync honours that token while waiting for the operation lock and once immediately before the first command, then ignores it (uses only the IPC deadlines) so a newer selection cannot truncate a queue mid-build; the newer commit then waits on the lock and replaces."
  - "Behavior changes in Mpv: the pause flag is now tracked from mpv's pause property events even before/regardless of selection and is no longer assumed false at a new load; a new load sends set_property pause false after the appends; mpv is launched with --loop-playlist=no --loop-file=no."
  - "Failure policy (Mpv.LoadAlbumAsync doc comment): error reply to the FIRST loadfile = nothing changed, rethrown as CommandError (controller treats as transient). Any later failure, or a timeout/connection error on the first command, = outcome uncertain: status Failed 'album load incomplete', selection observations ignored, `stop` sent to clear the queue, MpvException(AlbumIncomplete) thrown. If the stop is not confirmed, a flag makes the next load send `stop` first and send no playlist command while that fails. The controller's existing bounded retry therefore never replays onto an unknown queue."
  - "PlayerAlbumApplier now rejects permanently (current album keeps playing) a track list with duplicate track ids or a non-http(s) stream URL, before mutation. Its log line changed from 'Playing N tracks.' to 'Queued N tracks; waiting for mpv to start playback.' because acceptance is not playback."
  - "PlaySession logs 'Player: <status>' (and the failure detail) on every Mpv status change, so a stream-deadline or incomplete-load failure is visible."
  - "LUC-09 entry point: PlaybackOwner.TransportAsync(TransportCommand.{Play,Pause,TogglePause,Next,Previous}) returns Applied or Ignored. It is stateless over Mpv, so any number of owners share one operation lock; transport is evaluated against the observed status inside that lock (Pause/Toggle while Loading/Playing, Play while Paused, Next/Previous while Playing/Paused; otherwise Ignored, nothing sent). Previous-on-first-track and Next-on-last-track semantics are deliberately left to LUC-09 (currently raw playlist-prev/next force). PlaySession's createApplier receives only Mpv; LUC-09 should construct new PlaybackOwner(mpv) alongside it."
  - "Known limits: after a definite load failure (end-file error, stream deadline) the queue is left as mpv has it; only the status is Failed. A later selection replaces it. A controller 'Applied' phase therefore means queue accepted, not audio started; audio state is only in Mpv.Status."
  - "Proposed board updates (not applied): LUC-07 evidence for A10, A15, A16 (adapter part), A17 recorded with the substitutes below; A15/A16 device halves, A22 and the mpv-semantics list below stay open for LUC-08/LUC-12."
blockers: []
---

# LUC-07 implementer report

## What changed

- `Mpv.cs`: `LoadAlbumAsync` is the atomic album operation described above; new `TransportAsync`, `MpvFailureKind.AlbumIncomplete`, `LaunchArguments`; pause tracking from observation.
- `PlaybackOwner.cs` (new): narrow façade for album apply and transport; documents the LUC-09 routing.
- `PlayerAlbumApplier.cs`: validation before mutation, commit through the owner.
- `PlaySession.cs`: status logging.

## Acceptance coverage (`Amp.Nfc.Tests/AlbumPlaybackTests.cs`)

Real `SelectionController`, `CatalogueFile`, `Navidrome` (stubbed `HttpMessageHandler`), `PlayerAlbumApplier`, `Mpv` over `FakeMpvPeer` extended in-test with a playlist and pause model.

| Acceptance | Tests |
| --- | --- |
| A15 A→B final queue all and only B, ordered, once | `Replacing_A_with_B_...` |
| A15 paused A, B starts unpaused | `Replacing_a_paused_A_starts_B_unpaused_...` (unpause after replace; Paused→Playing from events) |
| A15 failed B preparation keeps A | `Failed_preparation_of_B_...`, `Permanently_rejected_B_...`, `Duplicate_track_ids_...` |
| A15 failure after mutation reported, later selection accepted | `Failure_after_the_queue_changed_...`, `Uncertain_outcome_is_reconciled_...`, `First_playlist_command_rejected_...` |
| A16 accepted, no load/terminal event → stream deadline | `Accepted_playlist_without_load_or_terminal_event_...`, `Stream_error_on_the_selected_album_...` |
| A17 slow A then B; late A cannot replace B; scanning responsive | `Slow_A_then_valid_B_...` (A's HTTP ignores cancellation), `Superseded_commit_is_rejected_by_the_owner_...`, `Supersession_during_the_apply_...` |
| A10 transient failure then success, plays once | `Transient_source_failure_then_success_...`, `First_playlist_command_rejected_...` |
| Invalid/unmapped tags leave playback unchanged | `Invalid_or_unmapped_tag_...` |
| Transport serialized with replacement; status gating | `Transport_is_serialized_...`, `Transport_commands_follow_observed_status_...` |
| Stop at album end, no repeat | `Last_track_ending_stops_...`, `Mpv_is_started_without_playlist_or_file_repeat` |

## What these tests do not prove

The peer is scripted by this repository. It does not prove audible output, a working stream from a real Navidrome, DAC selection, or that real mpv behaves as assumed below. Timings use short deadlines (300–400 ms) and a fake clock.

## mpv assumptions to verify on the device (LUC-08/LUC-12)

1. `loadfile <url> replace` discards the previous playlist and starts the new first entry; `append` adds in order; each reply carries `data.playlist_entry_id`.
2. `loadfile replace` while paused leaves mpv paused, and `set_property pause false` afterwards starts playback; a `pause` property-change event follows.
3. `stop` stops playback and clears the playlist (so the incomplete-load reconcile leaves no queue), and its `end-file reason=stop` events for abandoned entries are ignored here.
4. `end-file reason=eof` for the last entry arrives with that entry's id and playback then stops (no loop with `--loop-playlist=no --loop-file=no`); mpv with `--idle=yes` stays alive.
5. Stream errors and unreachable URLs produce `end-file reason=error` (or nothing, caught by the 20 s stream deadline); an unusable audio device surfaces as a visible failure.
6. `playlist-next/prev force` at the playlist ends (LUC-09).

## Validation

Validated with `bun <ruach>/skills/ruach-handoff/scripts/validate.ts docs/coordination/v1/LUC-07/impl-1-report.md --repo .`; the result is in the final message. `candidate_revision` and `tested_revision` are the implementation commit.
