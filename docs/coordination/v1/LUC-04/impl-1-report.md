---
task: LUC-04-impl
status: complete
outcome: "Player.cs no longer latches on currentUid. A new SelectionController tracks the observed object, the candidate release and its retry/suppression state, a selection generation and the applied album. Verified only with a scripted reader, fake clock and fake applier; no real PN532, tag, mpv, Navidrome or audio was exercised, so radio coupling (tag motion, RF dropouts, how often the binding returns null) is unverified."
artifacts:
  - Amp.Nfc/SelectionController.cs
  - Amp.Nfc/SelectionTypes.cs
  - Amp.Nfc/Pn532SelectionReader.cs
  - Amp.Nfc/PlayerAlbumApplier.cs
  - Amp.Nfc/Player.cs
  - Amp.Nfc/Options.cs
  - Amp.Nfc.Tests/SelectionControllerTests.cs
  - Amp.Nfc.Tests/SelectionOptionsTests.cs
  - docs/coordination/v1/LUC-04/impl-1-report.md
  - "branch ruach/luc-04-impl"
candidate_revision: de88108fe02f5feb81c44d5823bec3652bae6240
tested_revision: de88108fe02f5feb81c44d5823bec3652bae6240
verification:
  - "dotnet build: 0 errors; 1 warning, the existing CA1416 in MpvTests.cs"
  - "dotnet test (solution): 151 passed, 0 failed, run 3 times with identical results (118 before, 33 new); selection tests were written first and failed to compile before the implementation"
  - "bun validate.ts on this report with --repo . : see Validation section"
review: not-run
discoveries:
  - "Applier boundary for LUC-07: IAlbumApplier.PrepareAsync(release, ct) returns IPreparedAlbum; only CommitAsync replaces the queue. SelectionRejectedException means permanent (suppressed); any other exception is transient (bounded retry). A commit failure is also retried. LUC-07 owns failure-after-replacement reporting."
  - "Catalogue lookup is still the private Albums dictionary in Player.cs, passed to PlayerAlbumApplier as a Func<string, Album?>. LUC-03 only needs to replace that delegate."
  - "Pn532SelectionReader takes over DetectTag from Player.cs. A non-Ultralight target used to read as no tag; it is now a present, suppressed 'Unsupported tag type.' object. TryReadTexts returning false is treated as a transient read failure (bounded), because it cannot be told apart from a blank tag."
  - "Behaviour choices: a different UID supersedes pending preparation at once; confirmed removal cancels pending preparation but leaves committed audio; absence needs at least 2 consecutive nulls AND the configured interval; reader Fault leaves presence and absence counters untouched."
  - "Defaults not exposed as options: MaxAttempts 5, retry delay 1 s doubling to a cap of 8 s. Persistent operational faults are LUC-10."
blockers: []
---

# LUC-04 implementer report

## Changes
- `SelectionController` (StepAsync, one scan tick, never waits on preparation; background prepare+commit under a generation value with cancellation; a commit gate stops a stale preparation committing after a newer one).
- `SelectionTypes.cs`: ISelectionReader/ReaderObservation (Absent, Present with lazy full read, Fault), IAlbumApplier/IPreparedAlbum, SelectionRejectedException, SelectionPolicy.
- `Pn532SelectionReader`: single PN532 owner; UID is decoded before the card is constructed or read; the full read happens once per presentation.
- `PlayerAlbumApplier`: the previous album/track/mpv code behind the narrow interface. `Player.cs` keeps the setup and a loop that steps the controller.
- Options: `LUCIFER_ABSENCE_CONFIRM_MS`, default 1500, accepted 100 to 60000 whole milliseconds, validated in LuciferOptions (play and check modes) as `AbsenceConfirmation`.

## Acceptance coverage (scripted only)
- A08 held tag: plays once over 40 polls; full read once per presentation.
- A09 one miss: single null, short gaps, one stalled-loop null do not rearm; reader faults never confirm absence.
- A10 transient: read, prepare and commit failures retry after delay while polling continues, then play once; attempts bounded, then suppressed until a new presentation; retry stops on removal.
- A11 / D06: removal leaves AppliedRelease; one "Album removed." line; re-presentation after confirmed absence commits exactly once more.
- A17 invalid: malformed/none/conflict tags and rejected releases log once and make no further reads or requests; a new presentation tries again.
- Stale preparation: a late A cannot replace B; superseded work is cancelled; a stale failure schedules no retry; Step does not block on slow preparation.
- Not covered: real reader null behaviour, timing against real tag motion, real mpv/Navidrome, audible output. Scripted sequences do not verify radio coupling; that stays for LUC-12.

## Proposed board updates
- LUC-04: status ready for review; evidence = this report and commit above.
- A08–A11, A17: scripted controller evidence only; physical confirmation open until LUC-12.

## Validation
Result: ok, no diagnostics. Command: `bun <ruach>/skills/ruach-handoff/scripts/validate.ts docs/coordination/v1/LUC-04/impl-1-report.md --repo .`
