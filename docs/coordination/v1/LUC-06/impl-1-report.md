---
task: LUC-06-impl
status: complete
outcome: Navidrome source boundary returns explicit outcomes (auth, unknown album, no tracks, transport, malformed) with deadline and cancellation, and deterministic disc/track ordering with a reported server-order fallback; auth failure is now transient-with-backoff.
candidate_revision: 6ddc828a02adde40d50f21065a51818c905e7d82
tested_revision: 6ddc828a02adde40d50f21065a51818c905e7d82
artifacts:
  - docs/coordination/v1/LUC-06/impl-1-report.md
  - Amp.Nfc/Navidrome.cs
  - Amp.Nfc/NavidromeOutcomes.cs
  - Amp.Nfc/NavidromeOrdering.cs
  - Amp.Nfc/PlayerAlbumApplier.cs
  - Amp.Nfc.Tests/NavidromeSourceTests.cs
  - Amp.Nfc.Tests/CatalogueSelectionGlueTests.cs
verification:
  - "dotnet build (solution): 0 errors; 1 pre-existing CA1416 warning in MpvTests.cs"
  - "dotnet test (solution): 230 passed, 0 failed, 0 skipped (was 186; +44)"
  - "Fixtures are hand-written fake-HTTP-handler responses, not captured from a real Navidrome: real-server envelope, error codes, disc/track metadata and stream access remain unverified (LUC-08, A22)."
  - "Not run: any real HTTP, TLS, DNS, Navidrome, mpv, reader or Pi."
discoveries:
  - "Subsonic servers commonly omit discNumber on single-disc albums; an album where no song has a disc number is treated as single-disc (disc 1). A disc missing on only some songs is incomplete and triggers the fallback."
  - "Controller retries are bounded by SelectionPolicy.MaxAttempts (default 5, delays 1-8 s, about 15 s total) and credentials are read once at startup, so an operator fixing credentials still needs a restart plus a re-presented tag; the controller was not changed."
  - "Subsonic codes other than 40-44, 50 and 70 (for example 0, 10, 20, 30, 60) stay permanent rejections as before; the decision did not cover them. 50 (not authorized) is grouped with authentication."
blockers: []
---

# LUC-06 implementer report

## Changes
- `Navidrome.cs`: `GetTracksAsync` returns `AlbumTracks`. It validates the HTTP status, then the `subsonic-response` envelope (status and error before any album data), then each song. The deadline is `options.HttpTimeout` as a linked token, in addition to the existing `HttpClient.Timeout`. Caller cancellation propagates as `OperationCanceledException`; the deadline becomes a transport outcome. The shared `HttpClient`, token auth and `CreateStreamUrl` are unchanged.
- `NavidromeOutcomes.cs` (new): `NavidromeException` hierarchy. Permanent: `StaleMappingException` (70), `NoTracksException`, other failed codes. Transient: `NavidromeAuthenticationException` (40-44, 50), `NavidromeTransportException` (connection, deadline, non-2xx), `NavidromeMalformedException`. No message includes the URL, token, salt or password; HttpRequestException text is not forwarded.
- `NavidromeOrdering.cs` (new): sort by (disc, track) when every song has a valid unique pair; otherwise keep the server order, each song once, with `MetadataIssue` set.
- `PlayerAlbumApplier.cs`: maps permanent outcomes to `SelectionRejectedException`, lets transient ones propagate to the controller, and writes the metadata issue to the operator output. `SelectionController`, Player.cs, Program.cs and Cli.cs are untouched.

## Existing expectations changed
- `CatalogueSelectionGlueTests.Other_server_refusal_is_a_permanent_rejection`: used code 40, which the integration decision makes transient. It now uses code 10 (still permanent). A new test covers code 40 as transient. No test was deleted or weakened otherwise.
- `StaleMappingTests` is unchanged and passes: code 40 is still "not stale" and its message contains "40".

## Coverage (tests in `NavidromeSourceTests.cs`, extended glue tests)
- Ordering data sets: shuffled single disc, shuffled multi-disc with track 1 on several discs, album with no disc tags.
- Fallback cases (theory, 7): absent, zero, negative and non-integer track; duplicate pair; disc missing on some songs; invalid disc. Also a duplicate-pair case checking each song appears once.
- Outcomes: failed status with HTTP 200 wins over album data; auth codes 40/41/44/50; unknown album 70; other code stays permanent; empty album (two shapes); 13 malformed bodies; non-2xx HTTP (503, 401, 500); connection failure with no URL or credential text; unanswered request ending at the 1 s deadline; caller cancellation; request shape.
- Glue: missing mapping and stale ID (existing), failed auth transient, malformed transient, metadata issue reported while still preparing. A real `SelectionController` run shows code 40 retried with backoff (4 requests at MaxAttempts 4) and logged without credentials, while code 70 makes one request and is suppressed. Current playback is untouched in all cases because `PrepareAsync` never touches mpv (the mpv argument is null in these tests).
- Test-first order was not strictly followed: implementation was written before the tests. The tests were then run against it.

## Not proven
Fake handler only: real Navidrome response shapes and error codes, TLS and redirect behaviour, actual stream playability, and A15 unchanged-playback with a real queue (LUC-07).

## Proposed board updates (not applied)
- LUC-06: implemented; evidence `6ddc828`, A07 and A14 automated at the HTTP-handler boundary.
- Record the auth-failure policy decision and the credentials-need-restart caveat under LUC-10 or the device gate.
