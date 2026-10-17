task: LUC-01-impl
status: complete
outcome: One executable with play, tag read, tag write --release, check and help; validated options model; reader-ownership lock; one test project. All 56 tests pass; built binary checked. No hardware, mpv, DAC or server exercised.
artifacts:
  - docs/coordination/v1/LUC-01/impl-1-report.md
  - Amp.Nfc/ (Cli.cs, CommandLine.cs, Options.cs, ReaderLock.cs, Player.cs, TagCommands.cs, Navidrome.cs, Mpv.cs, Program.cs)
  - Amp.Nfc.Tests/
  - Lucifer.slnx
  - README.md
  - branch ruach/luc-01-impl
verification:
  - "dotnet build (solution): 0 warnings, 0 errors"
  - "dotnet test: 56 passed, 0 failed (written first; failed to compile before implementation)"
  - "Built binary: help exit 0; no args, bogus, tag write, tag write --release zz exit 2 with stderr help; check without env exit 1 listing the three NAVIDROME fields; check with valid env exit 0; tag read with no server env reached the reader open (error: Access to the port is denied), exit 1"
  - "Not run: real PN532, mpv, DAC, Navidrome, target Pi; ownership checked only through the lock-file boundary and built-process tests"
discoveries:
  - "Root Program.cs was never compiled; its TypeNameFormat use did not compile against the binding (needs Header.TypeNameFormat). Fixed while folding it in."
  - "Reader lock is keyed by reader path inside LUCIFER_IPC_DIR, so processes must share that directory to conflict. Proposal for BLACKBOARD/DEVICE: the service unit and maintenance runs must use the same LUCIFER_IPC_DIR."
  - "Added unrequested defaults/env names: LUCIFER_* variables, tag-wait deadline (30 s) and mpv start deadline (5 s) replace the previous infinite wait and fixed 5 s loop. LUCIFER_CATALOGUE_PATH is validated but unused until LUC-03."
  - "Behaviour kept as-is for later tasks: tag write has no strict read-back comparison (LUC-02); play loop, catalogue and mpv handling unchanged (LUC-03/04/06)."
  - "Proposed board updates: LUC-01 evidence with the tested revision and A01/A02 automated parts; A02 device part (second owner on target) remains pending."
blockers: []
tested_revision: 74159f4c5038cc6fe6d0422219c97a8bddb26d57
candidate_revision: 74159f4c5038cc6fe6d0422219c97a8bddb26d57

# LUC-01 implementer report

## Coverage

- A01: help, unknown command, missing/invalid write argument and no args are tested in-process (`CommandLineTests`) and through the built process (`BuiltProcessTests`): prompt, nonzero for invalid, no reader lock created and no reader path mentioned.
- A02 automated parts: field-by-field errors for blank/missing/malformed values, no echo of URL or password (`OptionsTests`, `check` process test); writer modes run without NAVIDROME_* (process test reaches the reader open); second owner refused with an error naming the reader and telling the operator to stop the service (`ReaderLockTests`, process tests for `tag read`, `tag write`, `play`). Build output runs all modes.
- A02 device part and A23 installation: not covered.

## Commands

`dotnet build`, `dotnet test` from the repository root; manual binary runs via `dotnet Amp.Nfc/bin/Debug/net10.0/Amp.Nfc.dll <args>` with LUCIFER_NFC_DEVICE and LUCIFER_IPC_DIR pointing at disposable locations.

## Maintenance workflow

Documented in README "Reader ownership and tag maintenance": stop service, write, start service.
