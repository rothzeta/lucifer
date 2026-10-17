---
task: LUC-05-impl
status: complete
outcome: "mpv adapter now correlates replies by request_id, routes events, bounds connect/send/reply/stream-start waits, keeps one IPC connection with one read loop, exposes idle/loading/playing/paused/ended/failed status, and owns/cleans up its child and own socket. Verified only against fake peers and fake child scripts; no audible output or real mpv was exercised."
artifacts:
  - Amp.Nfc/Mpv.cs
  - Amp.Nfc.Tests/MpvTests.cs
  - Amp.Nfc.Tests/FakeMpvPeer.cs
  - docs/coordination/v1/LUC-05/impl-1-report.md
  - "branch ruach/luc-05-impl"
candidate_revision: 46cbd8f120b55740f2834ec7891d7cf67b686c71
tested_revision: 46cbd8f120b55740f2834ec7891d7cf67b686c71
verification:
  - "dotnet build: 0 warnings, 0 errors"
  - "dotnet test (solution): 83 passed, 0 failed, run 4 times with identical results (tests were written first and failed to compile against the old Mpv)"
  - "bun validate.ts on this report with --repo . reported ok"
review: not-run
discoveries:
  - "Mpv, MpvStatus, MpvFailureKind types are public; Mpv.StartAsync keeps its old positional parameters and adds optional MpvTimeouts, so Player.cs is untouched. Player.cs does not yet consume Status; that is LUC-07."
  - "Failed after an error end-file is recoverable: a later start-file for an entry of the current selection returns to Loading, since mpv skips to the next entry."
  - "Observations are tied to the selection through playlist_entry_id from loadfile replies; events for replaced entries are ignored. If mpv omits the id, filtering is disabled for that selection."
  - "Readiness request is get_property mpv-version, then observe_property pause. Real mpv behaviour of these, of end-file reasons, and of playlist_entry_id was not checked against a real mpv here."
  - "Tests need python3 (stale-socket case) and /bin/sh; they use /tmp short paths for unix socket length."
blockers: []
---

# LUC-05 implementer report

## Changes
- `Amp.Nfc/Mpv.cs` rewritten: `MpvTimeouts` (connect 2s, send 2s, reply 5s, stream start 20s by default), `MpvException` with `MpvFailureKind`, one maintained connection with one read loop, serialized commands, integer `request_id` on every command, unrelated replies and events never fail a command.
- Malformed line fails the pending command immediately; matching reply with no `error` is malformed; closed socket or child exit fails pending and later commands fast and sets status Failed.
- Accepted `loadfile replace` gives Loading and arms the stream-start deadline; `file-loaded` gives Playing/Paused, error `end-file` gives Failed with mpv's reason, deadline expiry gives Failed, never Playing. Eof of the last entry gives Ended.
- Child ownership: a socket answering before start is refused and left alone; a dead stale file is replaced; readiness needs connect plus a harmless request; early exit is reported at once; cancellation, failure, and Dispose kill the child and delete only a socket this instance saw appear while its own child lived.
- Tests: `MpvTests.cs` (27 tests) with in-process unix-socket `FakeMpvPeer.cs` and shell-script fake children.

## Acceptance coverage
- A12/A13 adapter parts (event before reply, wrong ID, matching error, missing/malformed/closed within deadline, stream-start deadline load failure): covered by fake-peer tests.
- A16/A21 adapter parts (startup cancellation, early child exit, child exit later, no stale socket from this instance): covered by fake-child tests.
- Not covered: send deadline (a stalled writer is hard to produce with a unix socket; implemented but untested), connect deadline against a full backlog, real mpv, real stream, DAC. A passing fake-peer test does not prove audible output.

## Proposed board updates (not applied)
- PLAYBACK.md LUC-05 evidence: adapter tests above, commit in this report's tested_revision. Remaining for A12/A13/A16/A21: real mpv/device check at LUC-12; LUC-07 must consume `Mpv.Status`/`StatusChanged`.
