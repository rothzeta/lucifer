---
task: README-1
status: complete
outcome: "README.md rewritten for a newcomer: purpose, status (v1 software delivered, device acceptance open), requirements, build/test, commands with maintenance workflow, full configuration table from Options.cs, catalogue and input map, transport controls, recovery, packaging summary linking INSTALL.md, a Hardware and software inventory table with a read-only collection script, troubleshooting, project layout and records. Only README.md changed besides this report."
artifacts:
  - README.md
  - docs/coordination/v1/README-1/impl-report.md
  - docs/coordination/v1/README-1/impl-assignment.md
  - branch ruach/readme-1
candidate_revision: ed75f702fd5a2ab44b9e0a5c0a57af2c9d886296
tested_revision: a69401d170e12ccc00f6dade8194b9799afaa05e
verification:
  - "dotnet build (DOTNET_ROOT=<home>/.dotnet): 0 errors, 2 existing CA1416 test warnings"
  - "dotnet test Amp.Nfc.Tests: 390 passed, 0 failed"
  - "Amp.Nfc help (exit 0): option names and defaults in the README configuration table checked against the output and Options.cs"
  - "check with catalogue.example.json and placeholder NAVIDROME_* (exit 0): quoted output matches"
  - "relative links in README.md resolve to existing files (scripted check, no MISSING)"
  - "Read in source: TagWriter reads back and prints Verified; reader-lock message text; PN532 detected line; publish.sh arguments"
  - "Not run: publish.sh, input inspect, tag commands, play (no Pi, reader, mpv, or server); anchor links checked by reading only"
review: not-run
discoveries:
  - "Inconsistency: the built executable is Amp.Nfc, but help text and the old README/input-map example say amp-nfc. README documents Amp.Nfc and notes the alias; source untouched."
  - "Option scope: only LUCIFER_NFC_DEVICE, LUCIFER_IPC_DIR and LUCIFER_READER_TIMEOUT_SECONDS are validated for tag modes; the rest (MPV, audio, catalogue, timeouts, input map/required/debounce) only for play and check. LUCIFER_INPUT_DEVICE is also read by input inspect."
  - "The writer prompt says NTAG215 (TagCommands.cs) while TagWriter accepts any Ultralight/NTAG-compatible card; the README lists NTAG215 as planned and unverified."
  - "The old README's status text (prototype, separate writer, baseline commit f49202a) was stale and was replaced; the old reference to a baseline with no successful build was dropped."
  - "deploy/lucifer.service Documentation points at /opt/lucifer/current/INSTALL.md, but the package places INSTALL.md at the archive root, so it resolves only under the release directory layout; not changed."
  - "No device facts, hostnames, versions or addresses were added; Actual inventory cells are _pending (LUC-08)_."
blockers: []
---

# README-1 implementer report

README.md was restructured from the existing content (transport controls and shutdown/recovery sections kept, the settings table extended with meanings and the required NAVIDROME_* variables, the stale status replaced). INSTALL.md is linked, not duplicated. The inventory section follows the assignment; planned values come from README, ACCEPTANCE.md, DEVICE.md, TAGS.md and deploy/*.

Scope: only README.md and this report were changed; the assignment is committed unchanged. Nothing pushed.
