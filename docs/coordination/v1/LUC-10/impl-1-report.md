---
task: LUC-10-impl
status: complete
outcome: "play now stops on SIGTERM and Ctrl+C within a validated deadline, and exits nonzero when the mpv it started is lost or the reader is unrecoverable. Idle with no tag is not a fault. Verified only with real child processes standing in for mpv, an in-process fake mpv IPC peer, scripted readers and a fake clock; no real mpv, PN532, Pi, Navidrome or systemd was exercised."
artifacts:
  - Amp.Nfc/Lifecycle.cs
  - Amp.Nfc/ReaderWatch.cs
  - Amp.Nfc/PlaySession.cs
  - Amp.Nfc/Player.cs
  - Amp.Nfc/Program.cs
  - Amp.Nfc/Options.cs
  - Amp.Nfc/Mpv.cs
  - Amp.Nfc/ReaderLock.cs
  - Amp.Nfc/Pn532SelectionReader.cs
  - README.md
  - Amp.Nfc.Tests/PlaySessionTests.cs
  - Amp.Nfc.Tests/ReaderWatchTests.cs
  - Amp.Nfc.Tests/ShutdownOptionsTests.cs
  - Amp.Nfc.Tests/StopSignalTests.cs
  - Amp.Nfc.Tests/ReaderLockTests.cs
  - docs/coordination/v1/LUC-10/impl-1-report.md
  - branch ruach/luc-10-impl
candidate_revision: eb945867da62ebcf014671cd8aa6aeca1925d079
tested_revision: eb945867da62ebcf014671cd8aa6aeca1925d079
verification:
  - "tests written first: build failed with 8 compile errors before implementation"
  - "dotnet build: 0 errors"
  - "dotnet test (solution): 215 passed, 0 failed, run 3 times identically (186 before, 29 new); no existing test changed or removed"
  - "after the runs: no leftover sleep children and no /tmp/ls-* directories"
review: not-run
discoveries:
  - "Navidrome*.cs untouched. Shared-file edits: Player.cs reduced to hardware wiring (its loop moved to PlaySession.cs), Program.cs uses StopSignals, Mpv.cs gains one read-only property Lost, Options.cs one option, Pn532SelectionReader.cs implements IReaderHealth. Cli.cs unchanged: its existing catch maps FatalRunException and ShutdownTimeoutException to exit 1 and cancellation to exit 0."
  - "Behavior change: the old loop logged reader errors and retried forever; that is gone. ReaderLock now deletes its file on clean release (DeleteOnClose); a refused second owner never touches the first owner's file."
  - "Banner order changed: PN532 firmware is printed before mpv starts."
  - "Process.Kill only kills mpv itself, not a process tree. Orphans of mpv's own children are left to systemd KillMode=control-group (LUC-11 unit must keep it)."
  - "If mpv startup itself hangs in code that ignores cancellation, the child exists before the shutdown hook is registered; Mpv.StartAsync cancels promptly in the tested cases, but this window is not covered by the hard deadline."
  - "Board proposal (not applied): LUC-10 application/process evidence for A19-A21 recorded with the substitutes below; unplug/replug and systemd recovery stay open for LUC-08/LUC-12."
blockers: []
---

# LUC-10 implementer report

## Design
- SIGTERM and Ctrl+C: `StopSignals.Register` (PosixSignalRegistration plus CancelKeyPress) cancels one token.
- `PlaySession.RunAsync` (new) takes an open reader, health probe and applier factory. Stop gets `LUCIFER_SHUTDOWN_TIMEOUT_SECONDS` (default 10, 1 to 300, validated) to unwind; after that the owned mpv is disposed through a hook and `ShutdownTimeoutException` gives exit 1. Teardown cancels pending work, bounded-waits for it, and disposing mpv stops the child and deletes only its own socket. Startup cancellation is handled inside `Mpv.StartAsync` (child stopped, socket removed); `Cli` releases the reader lock.
- Fatal policy: `mpv.Lost` (process exit or IPC loss, not a failed load) and `ReaderWatch` verdicts throw `FatalRunException`, exit 1. `ReaderWatch`: 20 consecutive identified faults over at least 10 s, or 3 consecutive failed idle health probes (every 60 s, retry after 5 s). A null detection resets the fault run but never counts as failure. A present tag resets everything. A throwing reader is converted to faults.
- `IReaderHealth` is implemented by the PN532 adapter as a firmware-version query.

## Commands and results
- `dotnet build`: 0 errors.
- `dotnet test`: 215 passed, three runs. New tests: PlaySessionTests (stop during child startup, delayed HTTP preparation, unanswered IPC loadfile, work that ignores cancellation, mpv killed after startup, unrecoverable faults, transient fault burst, 2 s idle with no tag, graceful stop), ReaderWatchTests (24 h simulated idle, probe thresholds, recovery, throwing probe, decorator), ShutdownOptionsTests, StopSignalTests (SIGTERM to the test process), ReaderLockTests (own file removed, second owner refused and first lock intact).
- A second reader owner remains refused: existing BuiltProcessTests still pass, plus the new lock tests.

## Coverage and what the substitutes do not prove
- Child processes are `sh` scripts that `exec sleep`; the IPC peer is in-process. Real mpv exit behavior on SIGTERM, its own socket cleanup and audio output are unproven.
- SIGTERM is tested by signalling the test process, not a built `play` process: a built `play` needs a PN532 serial device, which is absent here.
- Reader faults and health are scripted. Whether the binding throws, returns null or hangs after unplug is unknown; a blocking Poll that never returns would be caught only by the outer shutdown deadline after a stop, not by the supervisor.
- Timings (10 s / 60 s / 20 faults) are untested defaults chosen without field data.

## Device-pending
- PN532 unplug/replug behavior and whether `FirmwareVersion` fails on unplug (LUC-08).
- Real mpv death and restart, owned child group kill, and recovery through the installed systemd unit with KillMode=control-group (LUC-12).
- Fresh-selection-after-restart with a tag left on the reader (logic follows from the controller; not run end to end).

## Validation
`bun <ruach>/skills/ruach-handoff/scripts/validate.ts <this report> --repo .` returned ok:true with both revisions resolved. The assignment file is unchanged (committed earlier in 88b276c).
