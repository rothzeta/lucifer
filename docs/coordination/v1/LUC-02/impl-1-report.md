task: LUC-02-impl
status: complete
outcome: Typed Guid-backed ReleaseId, shared TagCodec used by tag write, tag read and play, and a validate-write-readback-compare TagWriter behind a fake-able tag boundary. 91 tests pass (35 new). No hardware exercised.
artifacts:
  - docs/coordination/v1/LUC-02/impl-1-report.md
  - Amp.Nfc/ReleaseId.cs
  - Amp.Nfc/TagCodec.cs
  - Amp.Nfc/TagWriter.cs
  - Amp.Nfc/TagCommands.cs
  - Amp.Nfc/Player.cs
  - Amp.Nfc/Cli.cs
  - Amp.Nfc.Tests/TagCodecTests.cs
  - Amp.Nfc.Tests/TagWriterTests.cs
  - branch ruach/luc-02-impl
verification:
  - "Tests written first: dotnet build failed with 5 errors (missing types) before implementation"
  - "dotnet build (solution): 0 warnings, 0 errors"
  - "dotnet test: 91 passed, 0 failed (56 existing unchanged, 35 new)"
  - "Not run: real PN532/NTAG write, play path against a real tag, mpv, Navidrome; Player.cs integration compiled but not run"
discoveries:
  - "Rule choice: a malformed or unknown-version record inside the amp:album: namespace makes the whole tag Invalid even when a valid record is also present; identical repeated identities are not a conflict; unrelated text records are skipped."
  - "UUID must be strict D format (36 chars, no surrounding whitespace) on tags; CLI/ReleaseId.Parse remains lenient via Guid.TryParse with trim."
  - "Play path now logs the codec message instead of separate 'Unsupported'/'Invalid MusicBrainz ID' lines, and looks up the catalogue by normalised lowercase UUID. Albums dictionary is unchanged (LUC-03 owns it)."
  - "tag read now prints the resolved album identity via the shared codec."
  - "Mpv.cs untouched. Cli.cs touched in one line (ReleaseId.From)."
  - "Proposed board update: LUC-02 evidence = tested revision, codec/writer boundary tests for A03-A05; physical write-to-play stays at LUC-12. Tag lock is absent by interface (ITagMedium has no lock method)."
blockers: []
tested_revision: ab472e9a40c2706aac68307fcce6140bd3928ccd
candidate_revision: ab472e9a40c2706aac68307fcce6140bd3928ccd


# LUC-02 implementer report

Commands: `dotnet build`; `dotnet test` (from the worktree root, solution Lucifer.slnx).

## Coverage

- A03 (codec evidence): `TagCodecTests` cover canonical, uppercase/mixed case, malformed (7 data sets), unknown version, unrelated-text-first, none, conflicting in both orders, repeated identical, valid-plus-malformed.
- A04: invalid release (blank, non-UUID, empty UUID, default) refused with no `ITagMedium` call (`Invalid_identity_performs_no_write_or_format_request`); `tag write` also validates before opening the reader.
- A05: `TagWriterTests` with a fake tag: write returning false, thrown interruption, removed tag at read-back, empty read-back, mismatched identity, unparseable/conflicting read-back each fail with no `Verified` output; success path verified only after comparison. `TagWriteException` and other exceptions reach `Cli` which returns nonzero.
- No locking: interface has no lock operation (tested).
