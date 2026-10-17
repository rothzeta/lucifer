---
task: LUC-03-impl
status: complete
outcome: "Compiled Albums dictionary removed. Release-UUID to Navidrome-album-ID catalogue now loads from LUCIFER_CATALOGUE_PATH, is re-read on every tap, and is validated by check and at play start. Verified only with temp files and a stubbed HTTP boundary; no real Navidrome, mpv, reader or DAC."
artifacts:
  - Amp.Nfc/Catalogue.cs
  - Amp.Nfc/CatalogueFile.cs
  - Amp.Nfc.Tests/CatalogueTests.cs
  - Amp.Nfc.Tests/StaleMappingTests.cs
  - catalogue.example.json
  - docs/coordination/v1/LUC-03/impl-1-report.md
  - "branch ruach/luc-03-impl"
candidate_revision: d270fefdad049c9d27ea60e2e96c444989e535aa
tested_revision: d270fefdad049c9d27ea60e2e96c444989e535aa
verification:
  - "tests written first: solution failed to compile against missing Catalogue types (red) before implementation"
  - "dotnet build: 0 errors; 1 pre-existing CA1416 warning in MpvTests.cs"
  - "dotnet test (solution): 146 passed, 0 failed (118 before; 28 new)"
review: not-run
discoveries:
  - "Existing tests Check_passes_with_valid_configuration_without_hardware and Play_is_refused_when_the_reader_is_owned assumed no catalogue file; since check/play now require a valid catalogue they were given LUCIFER_CATALOGUE_PATH pointing at the shipped catalogue.example.json. Assertions kept, one added (2 albums). No test deleted or weakened."
  - "Amp.Nfc.csproj gained InternalsVisibleTo Amp.Nfc.Tests and a Content link copying catalogue.example.json to output; LUC-04 may also touch the csproj."
  - "Navidrome.GetTracksAsync now throws StaleMappingException on Subsonic error code 70 and a plain error for other failed statuses. Code 70 for a missing album is assumed from the Subsonic spec, not checked on a real server (LUC-06/LUC-12)."
  - "Source identity is the catalogue's own source field, which must be navidrome; it is not compared with NAVIDROME_URL. Release MBID comparison against the server was not implemented (server semantics unchecked)."
  - "Player.cs edits: removed Album record and Albums dictionary, replaced lookup with CatalogueFile.Resolve, added StaleMappingException handling. The currentUid latch is untouched (LUC-04)."
  - "Proposed board updates (not made): TAGS.md LUC-03 evidence = CatalogueTests and StaleMappingTests; A06 automated part and A07 mapping-outcome part covered; third-album audio test and real stale detection remain LUC-12/LUC-06."
blockers: []
---

# LUC-03 implementer report

## Changes
- `Catalogue.cs`: parses/validates JSON with `source` and `albums` keyed by UUID; reports all errors by field path (`albums.<uuid>.navidromeId`, `source`, `albums`, `catalogue`); rejects malformed/empty UUIDs, duplicate keys (case variants included), blank/missing/non-string fields, wrong source, non-JSON.
- `CatalogueFile.Resolve`: Found / Unknown (names release, "not in the catalogue") / Invalid (names path and fields). Reads the file each call, so edits apply at run time.
- `Cli.cs`: check and play load the catalogue and exit 1 with field errors; check prints the album count and states IDs are not verified against the server. README Catalogue section and help text updated.
- `Player.cs`: unknown, invalid or stale results print a specific line and `continue` before any queue change, so the current album keeps playing.

## Acceptance coverage
- Third mapping added by editing the file at run time and resolved; provider ID changed by editing only the mapping: `CatalogueTests`.
- Duplicate/blank/malformed entries with field-specific messages, source identity: `CatalogueTests`, plus process-level check tests in `BuiltProcessTests`.
- Stale classification (error 70) against a stub HTTP handler: `StaleMappingTests`.
- Not covered: the Player loop's reaction (no reader here; the branch is a print plus continue), real server stale detection and the third-album audio test (LUC-06/LUC-12), release MBID comparison.

## Validation
Handoff validator run on this report with --repo .: see terminal result.
