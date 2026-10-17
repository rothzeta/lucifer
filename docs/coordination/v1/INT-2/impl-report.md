---
task: INT-2
status: complete
outcome: "LUC-04 merged into the LUC-03 branch; the only conflict (Player.cs) is resolved. Combined revision builds and passes all 186 tests. Verified only with fakes and stubs; no real reader, mpv, DAC or Navidrome."
artifacts:
  - Amp.Nfc/Player.cs
  - Amp.Nfc/PlayerAlbumApplier.cs
  - Amp.Nfc/Navidrome.cs
  - Amp.Nfc.Tests/CatalogueSelectionGlueTests.cs
  - docs/coordination/v1/INT-2/impl-report.md
  - "branch ruach/luc-03-impl"
baseline: a6802550df5270a3078ec5305abec73ae4d006e5
source_revisions:
  - "LUC-03 candidate d270fefdad049c9d27ea60e2e96c444989e535aa, report a6802550df5270a3078ec5305abec73ae4d006e5"
  - "LUC-04 candidate de88108, report ef51e13"
candidate_revision: 9c5719aa7e3c5c6609877167c8408cc1f7ca3850
tested_revision: 9c5719aa7e3c5c6609877167c8408cc1f7ca3850
verification:
  - "dotnet build: 0 errors, 1 pre-existing CA1416 warning in MpvTests.cs"
  - "dotnet test (solution): 186 passed, 0 failed (146 LUC-03 branch + 33 new from LUC-04 + 7 new glue tests)"
  - "built binary: help exit 0; bogus command exit 2; check with catalogue.example.json exit 0 reporting 2 albums"
review: not-run
discoveries:
  - "Navidrome.cs: added NavidromeRejectedException (server answered and refused); StaleMappingException now derives from it. Non-70 failed statuses (e.g. auth code 40) are therefore permanent rejections, which suppresses retries until a new presentation; a wrong password will not self-heal by retry. Reported for the coordinator to confirm."
  - "HTTP non-2xx, timeouts and transport errors still surface as HttpRequestException/other exceptions, which the controller treats as transient."
  - "Player no longer prints its own unknown/stale lines; the controller logs the rejection message, which carries the same text and 'Current album keeps playing'."
  - "Real server behaviour (error 70, auth errors) remains unverified until LUC-06/LUC-12."
blockers: []
---

# INT-2 integration report

## Conflict resolutions
- `Amp.Nfc/Player.cs` (only conflict): kept LUC-04's SelectionController loop (`StepAsync`, `Stop`, `WhenIdleAsync`); dropped LUC-03's inline per-tap lookup. The applier is constructed with `options.CataloguePath` instead of the removed Albums delegate.
- `Amp.Nfc/PlayerAlbumApplier.cs` (LUC-04 file, glue only): resolves with `CatalogueFile.Resolve` per selection. Unknown or invalid catalogue, StaleMappingException, other NavidromeRejectedException, and empty album -> SelectionRejectedException; other exceptions propagate as transient.
- `Amp.Nfc/Navidrome.cs`: rejection base class added as above.

## Tests
New `CatalogueSelectionGlueTests` (7): unknown, invalid file, stale, other refusal, empty album -> rejected; 503 and transport failure -> not rejected; good album prepares. No existing test changed or removed.
