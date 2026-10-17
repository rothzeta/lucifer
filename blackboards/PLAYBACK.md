---
id: lucifer-playback
type: workstream
status: ready
priority: P1
updated: "2026-10-17"
focus: "Make album loading correct and observable"
next: [LUC-05]
blocked_by: []
related: [lucifer-tags, lucifer-device]
---

# Playback

[Project blackboard](../BLACKBOARD.md) · [Acceptance record](ACCEPTANCE.md)

## LUC-05 Handle mpv replies and events correctly

**Status:** done  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** none

Keep one persistent mpv process. Extract its ownership and protocol handling from the main file.

Send an integer `request_id` with every command. Read until the matching reply. Route unsolicited events and disregard unrelated replies without treating them as command failures. Bound connect, send, and reply waits. Fail a pending operation when the process or connection ends.

A new connection per command is not inherently wrong. For v1 playback observations, use one maintained IPC connection with a single read loop. Serialize ordinary commands; a general concurrent RPC framework is unnecessary.

Maintain a small status: idle, loading, playing, paused, ended, or failed. Associate observations with the current selection and track. A playlist command acknowledgement establishes acceptance of the command. Playback state also needs stream and player observations. Relevant events include file loading, end of file, and process exit.

Give stream startup a separate deadline. An accepted command with no successful-load or terminal outcome must become a recoverable load failure rather than remain loading indefinitely. Keep scanning and controls responsive.

Own the child immediately after starting it. Verify readiness by connecting and completing a harmless request. Detect early process exit. On startup cancellation or failure, stop and dispose the child and clean up only the socket owned by this instance.

**Done when:** event-before-reply, wrong reply ID, missing reply, malformed reply, startup cancellation, and child exit are handled within declared deadlines. Actual load failure is distinguishable from accepted command. Record adapter/process evidence for A12, A13, A16, and A21. Supervised recovery in A20 belongs to LUC-10 and the final device gate.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-05/impl-1-report.md. Device portions pending. Fake mpv peer for A12, A13, A16, A21; real mpv semantics unverified.

## LUC-06 Make Navidrome outcomes explicit

**Status:** done  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** LUC-03

Keep Navidrome behind a narrow source boundary: resolve the configured local album and return an ordered set of playable tracks. Keep the API and provider IDs out of the NFC parser.

Validate the HTTP response and the `subsonic-response` envelope. Read `status` and `error` before endpoint data. Distinguish failed authentication, unknown album, no tracks, transport failure, and malformed data. Use a deliberate request deadline and cancellation.

Keep the existing token-based Subsonic authentication and stream URL construction. Do not log authenticated URLs. Reuse the HTTP client.

For complete metadata, order by disc then track. A position is the pair of disc number and track number; track 1 on different discs is not a duplicate. If positions are absent, invalid, or duplicated, preserve the returned album order as the documented fallback and report the metadata issue. Test the fallback. Do not silently move missing-number tracks to the beginning and call the order authoritative.

Verify stream access through the real server in LUC-08 and A22. This task does not require a new Navidrome private API, automatic catalogue sync, or server-side playback.

**Done when:** representative album responses produce complete deterministic track sets and HTTP-success application errors produce meaningful outcomes. Record HTTP/source boundary evidence for A07 and A14. LUC-07 verifies unchanged playback when preparation fails in A15.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-06/impl-1-report.md. Device portions pending. Hand-written HTTP fixtures for A07, A14; real Navidrome unverified.

## LUC-07 Apply each complete album as one operation

**Status:** done  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** LUC-04, LUC-05, LUC-06

Connect the controller, catalogue, source, and audio player. Prepare and validate all tracks before replacing current playback. Check the selection generation immediately before applying the queue. Discard stale preparation.

Serialize album replacement and transport controls. The existing awaited replace-then-append strategy may remain if the resulting queue and failure policy are correct. A supported playlist load is also possible. Do not assume a specific command sequence provides transaction rollback.

The final queue must contain only the selected album, in the expected order, exactly once. A failed preparation preserves the prior album. If queue mutation has started and then fails, report failure and enter a defined recoverable state. Do not report the entire album as playing while only a partial queue exists. If an IPC outcome is uncertain, reconcile the current queue or enter an explicit failed state before retrying; do not blindly replay a possibly applied load.

Start a newly selected album unpaused under D06. Keep stop at album end; no automatic repeat. Ensure invalid or unmapped tags leave current playback unchanged.

Keep scanning and accepting controls during slow preparation. A newer valid album becomes the desired selection. Bounded concurrent I/O and one serialized state owner are sufficient.

**Done when:** A-to-B replacement, failed preparation, partial queue failure, and slow A followed by B have the documented outcome. The player reports loading, playing, paused, ended, and failed states from observable evidence. Record integrated boundary evidence for A10 and A15–A17. Full-album audio in A22 is closed at LUC-12.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-07/impl-1-report.md. Device portions pending. A10, A15-A17 via fake peers; mpv playlist semantics and audible output (A22) unverified.

## Source guidance

The [mpv manual](https://mpv.io/manual/stable/#json-ipc) documents reply IDs, asynchronous events, and playlist commands. The [OpenSubsonic envelope](https://opensubsonic.netlify.app/docs/responses/subsonic-response/) defines application status and error fields. The [review](../docs/reviews/2026-10-17-v1-review.md) links those rules to the exact current code.
