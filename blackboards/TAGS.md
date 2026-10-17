---
id: lucifer-tags
type: workstream
status: ready
priority: P1
updated: "2026-10-17"
focus: "Make programming and selecting any registered album dependable"
next: [LUC-01]
blocked_by: []
related: [lucifer-playback, lucifer-device]
---

# Tags and catalogue

[Project blackboard](../BLACKBOARD.md) · [Acceptance record](ACCEPTANCE.md)

## LUC-01 Make one runnable command surface

**Status:** ready  
**Owner:** unassigned  
**Depends on:** none

Bring the root writer into the existing application. Keep one executable. Extract the tag, catalogue, provider, player, and controller responsibilities into cohesive files as they become necessary. Keep the current project path unless a rename has a concrete benefit.

Provide `play`, `tag read`, `tag write --release <uuid>`, and `check` modes. These are proposed commands, not current features. Parse help and arguments before opening the reader. Writer modes must not require Navidrome credentials. A hardware-independent help or configuration check must remain possible.

Move NFC path, mpv path, audio device, IPC directory, catalogue path, and operation deadlines into one validated options model. Retain environment-based Navidrome credentials. Reject blank values and malformed server URLs. Report configuration errors by field without printing credentials or authenticated stream URLs.

Use an explicit maintenance workflow: stop the player service, run the writer, then start the service. Detect conflicting reader ownership and return a useful error. A local daemon control API is not required.

**Done when:** the application builds; all modes appear in help; invalid commands fail before hardware access; writer mode can run independently of Navidrome; one reader owner is enforced. Record A01 and A02.

**Evidence:** pending.

## LUC-02 Share the tag contract and verify writes

**Status:** todo  
**Owner:** unassigned  
**Depends on:** LUC-01

Use a typed release identity backed by `Guid`. The writer emits the existing canonical format:

`amp:album:1:mbid:<lowercase UUID in D format>`

The UUID identifies a MusicBrainz release. It does not identify a reader UID or a server-specific album. Do not change already programmed tags merely to rename AMP to Lucifer.

Share the formatter and parser between writing and playback. Accept case differences in the existing prefix and UUID; normalise identity before lookup. Reject invalid syntax and unknown versions. Search NDEF text records for the supported namespace. Reject conflicting album identities rather than choosing by record order.

Validate the command before formatting or writing. Write an NDEF text record through the binding. Read the tag again, parse the result, and compare it with the intended identity. Report verified success only after the comparison. Return nonzero on an interrupted write, an unreadable result, or a mismatch. Do not lock the tag during ordinary programming.

Scope initial support to the actual tested NTAG-compatible tag or ring. The physical inventory is owned by LUC-08.

**Done when:** the shared codec recognises the exact intended release after writing and reading back. Malformed input changes no tag data. Read-back mismatch is a failure. Record codec and writer boundary evidence for A03–A05. The physical write-to-play result is closed at LUC-12.

**Evidence:** pending.

## LUC-03 Add albums without editing source

**Status:** todo  
**Owner:** unassigned  
**Depends on:** LUC-02

Move the two existing mappings to an external catalogue. Use the canonical release UUID as the key and a Navidrome album ID as the source locator. Artist and title are display metadata.

Retain the existing mappings as migration examples. Do not claim their current availability until the real server confirms them. Provide a documented add/edit/check workflow. Validate UUIDs, duplicate keys, blank IDs, and configured source identity. On stale or unknown mappings, return a specific result and keep the current album playing.

Confirm the selected local album corresponds to the intended edition when registering it. If the server exposes a release MBID, compare it. If metadata is missing, record an explicit verified mapping instead of guessing by title.

For v1, a configuration file is sufficient. Optional later import can page through OpenSubsonic `getAlbumList2` and use the optional `musicBrainzId` field after checking the installed server's semantics. Neither public MusicBrainz access nor a full library scan belongs in each tap.

**Done when:** a third exact mapping can be added and resolved without modifying or rebuilding C#. Changing a local provider ID requires changing only the mapping. Record catalogue evidence for A06 and A07. The third-album audio test is closed at LUC-12.

**Evidence:** pending.

## LUC-04 Separate presence from selection success

**Status:** todo  
**Owner:** unassigned  
**Depends on:** LUC-02

Replace the single `currentUid` latch with a small controller that distinguishes:

- The observed object and its last successful observation.
- The candidate release and any transient retry or permanent rejection.
- The selection generation and the album applied to the player.

Ignore repeated observations after a successful selection. Treat one null detection as uncertain. Confirm absence over a measured interval. Preserve state on identified reader faults. The binding can return null for both no target and transport failure; the application must not invent a distinction it cannot observe.

Retry transient NDEF, source, or player failures with bounded work and delayed attempts. Keep scanning between attempts. Suppress repeated work for malformed or unsupported tags until a new presentation. Persistent operational faults follow LUC-10.

Apply decisions D06 and D07. Removal leaves current audio playing. Re-presentation after confirmed absence starts once. A newer valid selection invalidates older pending preparation. Use a small generation value or cancellation mechanism; do not add an event bus.

Inspect the UID before constructing and reading the full card where the binding permits it. Keep all PN532 calls under one owner.

**Done when:** held tags do not restart; one missed observation does not rearm; a transient initial failure recovers while the object remains; stale preparation cannot replace a newer selection. Record scripted reader/controller evidence for A08–A11 and A17. Physical and control integration is closed at LUC-12.

**Evidence:** pending.
