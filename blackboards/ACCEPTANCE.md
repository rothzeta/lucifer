# Lucifer v1 acceptance record

[Project blackboard](../BLACKBOARD.md)

## Release candidate

| Field | Value |
| --- | --- |
| Candidate commit and package | Pending |
| Test date and operator | Pending |
| Pi model, OS, and userland architecture | Pi 3B planned; actual values pending |
| .NET and mpv versions | Pending |
| Navidrome version | Pending |
| PN532 firmware and configured serial path | Pending |
| USB-to-TTL adapter identity | CH341 planned; actual identity pending |
| Actual tag or ring chip | Pending; writer source names NTAG215 |
| DAC and configured ALSA identifier | FiiO K5 Pro planned; actual identifier pending |
| Control device and event mapping | KB1K discussed; actual values pending |
| Selected timeouts and absence interval | Pending |
| Selection-to-audio measurements | Pending |
| Track-transition listening result | Pending |

The initial review was static. The review environment had no .NET SDK, mpv, NFC reader, DAC, or live Navidrome connection. None of the cases below is marked passed.

## Mandatory cases

“Automated” means a black-box check through public behaviour or an HTTP/IPC/reader boundary. “Device” means the actual target assembly. Use at least three representative data sets for the codec, selection policy, and track-order behaviour.

| ID | Mode | Case and observable passing result | Result |
| --- | --- | --- | --- |
| A01 | Automated | Help, unknown command, and missing write argument return promptly without reader access. Invalid invocations return nonzero. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A02 | Automated and device | Configuration errors identify the field. Writer mode requires no server credentials. A second reader owner is refused. The application builds and its modes run from the build output. Installation is checked in A23. | Partial: automated part at 7dd4cf8; device part open |
| A03 | Device | Program a new supported tag, read it again, and use it to play the intended release. Verified identity equals the requested identity. | Not run |
| A04 | Automated | Canonical, uppercase, malformed, unknown-version, unrelated-text-first, and conflicting-identity records produce the specified results. Invalid writer input makes no write or format request. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A05 | Automated and device | Removed tag, failed read-back, empty read-back, and mismatched identity produce failure. No final verified-success message is emitted. | Partial: automated part at 7dd4cf8; device part open |
| A06 | Device | Register a third album in configuration, program its object, restart, and play it without editing or rebuilding C#. | Not run |
| A07 | Automated | Missing mapping, stale provider ID, failed authentication, HTTP 200 with failed API status, empty album, and malformed response produce specific outcomes. Existing playback is unchanged. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A08 | Automated and device | Hold one valid object through a complete track. There is exactly one album selection and no time reset or repeated catalogue request. | Partial: automated part at 7dd4cf8; device part open |
| A09 | Automated | One missed detection or transient reader observation among successful detections does not trigger album removal or restart. Sustained absence follows the configured policy. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A10 | Automated and device | Initial NDEF, Navidrome request, or IPC connection before any load command fails transiently, then succeeds while the object stays present. Playback starts once. Invalid tags remain suppressed without log or request flooding. | Partial: automated part at 7dd4cf8; device part open |
| A11 | Device | Remove the object: current audio continues. Re-present after confirmed absence: the album restarts once. | Not run |
| A12 | Automated IPC peer | An event and an unrelated reply precede the matching reply. The command waits for its own request ID and completes correctly. Include a matching error reply. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A13 | Automated IPC peer | Missing reply, malformed JSON, and closed socket end the operation within its deadline. Shutdown does not hang. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A14 | Automated HTTP fixture | Shuffled complete positions yield disc/track order. Missing, invalid, or duplicate disc/track pairs follow the documented server-order fallback. Track 1 on different discs is not a duplicate. Each input song appears exactly once. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A15 | Automated and device | Replace A with B. Final queue contains all and only B. Repeat with A paused: B starts at its first track unpaused. Failed B preparation leaves A playing. Failure after mutation starts is reported and accepts a later valid selection. | Partial: automated part at 7dd4cf8; device part open |
| A16 | Automated IPC peer and device | A playlist command succeeds but the stream fails or produces no load/terminal event. The separate stream-start deadline reports failure. The state does not falsely remain playing. Invalid URLs and unusable selected audio output yield a visible failure. | Partial: automated part at 7dd4cf8; device part open |
| A17 | Automated | Delay A preparation, then select valid B. B becomes desired; late A completion cannot replace B. Scanning and controls remain responsive during preparation. | Automated pass at 7dd4cf8 (fakes/fixtures; reviewed) |
| A18 | Device | Play/pause, next, previous, repeated keys, first/last-track boundaries, loading, pause, and album-ended states follow LUC-09 in service mode. FiiO volume works. | Not run |
| A19 | Device | Disconnect and reconnect the reader. Scanning recovers under the declared health/supervisor policy. Any audio interruption matches the documented whole-service restart policy. Idle-with-no-tag does not create restart loops. | Not run |
| A20 | Device | Kill mpv. The service restores one working child. A present tag or a new selection starts under the documented restart policy. | Not run |
| A21 | Automated and device | Stop during child startup, delayed HTTP, and unanswered IPC. The process group exits by the shutdown deadline. No owned child or stale runtime socket remains. | Partial: automated part at 7dd4cf8; device part open |
| A22 | Device | Play a full album, a true multi-disc example, and a continuous track pair. No track is lost, duplicated, reordered, or restarted. Record selection latency with no numeric v1 SLA. The continuous fixture has no audible added break relative to its source files. Resolve unexplained interruptions. | Not run |
| A23 | Device | Install the package from the instructions, reboot, and play. Stop/restart and package replacement work. Reader placement is repeatable for 20 placements with the actual object and antenna position. | Not run |

## Passing evidence

Implementation tasks record applicable component evidence and may contribute only part of a mixed-mode case. Keep the case as Not run or Partial until its complete release conditions pass. LUC-12 records the final integrated and device result.

For each run, record the case ID, commit, mode, inputs, expected result, observed result, and log or test-report path. Use credential-free evidence. Preserve failed runs when a later fix changes their result.

A simulated PN532 sequence verifies policy. It does not verify radio coupling. A fake mpv peer verifies protocol handling. It does not verify decoding or the DAC. Device evidence closes those specific gaps.

## Release decision

**Status: not evaluated; v1 NOT complete.** Software candidate delivered at 7dd4cf8 with automated/component evidence only (reports under docs/coordination/v1/, task record docs/coordination/v1/task-record.md). All device cases and the physical baseline remain open. Mark ready only after all mandatory cases pass against the candidate. Record any accepted scope change in the project decisions before removing a gate.
