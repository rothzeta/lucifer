---
id: lucifer-device
type: workstream
status: ready
priority: P1
updated: "2026-10-17"
focus: "Run the player reliably on the existing hardware"
next: [LUC-08]
blocked_by: []
related: [lucifer-tags, lucifer-playback]
---

# Device and release

[Project blackboard](../BLACKBOARD.md) · [Acceptance record](ACCEPTANCE.md)

## LUC-08 Record the physical baseline

**Status:** blocked  
**Owner:** unassigned  
**Depends on:** none

On the actual Pi, record the OS and userland architecture, .NET version, mpv version, PN532 firmware, USB-to-TTL identity/path, tag chip, ALSA DAC identifier, and control-device presence and event identity. Confirm the PN532 board is in the intended UART mode. Record the current Navidrome version without credentials.

Run the current project and play a known programmed album. Record what works and the exact failure if it does not. A past terminal experiment does not replace a run of the reviewed source.

Test the actual ring or tag in its intended position relative to the antenna. Use a simple repeatable placement. Confirm that the ceramic ring's chip is compatible with the implemented reader path. Do not infer compatibility from the word NFC.

**Done when:** the physical inventory and one source-to-audio result are recorded in the acceptance record. Failed hardware checks become named blockers with a next diagnostic action. Re-estimate the remaining effort from this result.

**Evidence:** pending. Blocked: no Pi access from the orchestrator host (no /dev/ttyUSB0, no mpv, no ALSA hardware; the Pi hostname is not recorded and must not be inferred). Needs the owner to provide Pi access or run the inventory. Also needed there: `journalctl` check that mpv prints no stream URL, controller events via `Amp.Nfc input inspect`.

## LUC-09 Connect the selected transport controls

**Status:** active  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** LUC-05, LUC-07, LUC-08

Inspect the selected controller's actual Linux input events, using KB1K if it is available. Implement only the selected input path. Ensure the service account can access it.

Provide play/pause, next, and previous. Use the FiiO knob for volume in v1. A screen is not required by this plan.

Use a simple documented transport policy: play/pause toggles the current track; next advances and stops at album end; previous restarts the current track when more than three seconds have played, otherwise it selects the previous track; at the first track it restarts that track. Play after the album has ended restarts the selected album. These are proposed v1 policies.

During album preparation, controls affect the currently applied queue. Controls received during queue replacement run after replacement. Next and previous preserve the pause preference. During stream loading, play/pause changes that preference. A new album selection starts unpaused, but a later explicit Pause can override it. With no applied album, transport actions do nothing.

Handle button repeats deliberately. Route controls through the same owner as album replacement. Verify service-mode input; interactive mpv terminal hotkeys do not establish hardware control support.

**Done when:** normal, repeated, boundary, paused, loading, and ended control cases pass through the selected input adapter and controller. Record component evidence for A18. Its installed service-mode result is closed at LUC-12.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-09/impl-1-report.md. Device portions pending. Transport policy and evdev adapter implemented with NO default key codes; the selected controller's actual events (LUC-08) are unknown. Remaining: inspect real events with `input inspect`, write the mapping, confirm A18 on the Pi.

## LUC-10 Own shutdown and recovery

**Status:** done  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** LUC-01, LUC-05, LUC-08

Handle service termination as well as Ctrl+C. A small Generic Host lifetime or explicit POSIX signal registration is sufficient.

Use one supervisor policy. For v1, exit with a failure status when the essential mpv process dies or reader faults remain unrecoverable; let systemd restart the application and its owned child group. Transient album and network failures remain recoverable selection outcomes. Do not leave an alive process retrying a permanently dead adapter forever.

Some PN532 failures appear as null detection. Do not treat a long idle period alone as proof that the reader is broken. Use observed transport faults or an explicit reader health check for fatal escalation. Test the actual unplug/replug behaviour.

Give shutdown a deadline. Cancel pending work, close the reader and IPC connection, stop the owned mpv process, and clear owned runtime files. Handle cancellation before startup ownership is handed to the host.

After a supervised restart, scan again. A present tag is a fresh selection and restarts its album. Exact position restoration and uninterrupted playback through process failure are outside v1.

**Done when:** the application handles stop during startup, HTTP preparation, and IPC wait without an owned orphan process; fatal adapter failure exits according to the policy. Record application/process evidence for A19–A21. Recovery through the installed systemd unit is closed at LUC-12.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-10/impl-1-report.md. Device portions pending. A19-A21 application/process parts with substitutes; unplug/replug and systemd recovery pending on the device.

## LUC-11 Publish and install a reproducible device

**Status:** active  
**Owner:** Ruach v1 orchestrator (Implementer workers)  
**Depends on:** LUC-02, LUC-03, LUC-07, LUC-09, LUC-10

Keep .NET 10. Select the publish runtime from the installed OS: `linux-arm64` for 64-bit userland or `linux-arm` for 32-bit userland. Ordinary self-contained publishing is the baseline. Check native OS dependencies and the actual package on the Pi. Native AOT is not a prerequisite.

Supply a configuration example, catalogue example, systemd unit, and install/update/uninstall instructions. The unit must define the service user, device access, configuration location, runtime directory, restart policy, and owned child lifetime. Use a stable serial path or explicit device rule when the adapter supports it. Keep the actual ALSA identifier configurable.

Document how to check health, add an album, stop/program/start, inspect failures, replace the installed package, and restore the prior package. Keep personal credentials outside version control.

Add a basic CI build and the meaningful black-box tests. CI checks behaviour through public tag/controller and protocol boundaries; it does not claim hardware compatibility.

**Done when:** an installation from the documented package works without a development checkout, starts after reboot, accepts the physical controls, and follows the documented update/recovery procedure. Record A02 and A23.

**Evidence:** Component evidence at delivered revision 7dd4cf8 (source tree = reviewed 4bcca29; REVIEW-2 no blocking findings; 390 tests, fakes/stubs only, no hardware). Reports: docs/coordination/v1/LUC-11/impl-1-report.md; INT-4, INT-5, FIX-1. Device portions pending. Packaging for linux-arm64 and linux-arm (architecture not chosen), systemd unit, CI definition, install docs built/verified here. Remaining: install on the Pi without a checkout, reboot, update/rollback (A02, A23).

## LUC-12 Close the release gate

**Status:** blocked  
**Owner:** unassigned  
**Depends on:** LUC-11

Run all mandatory cases in [ACCEPTANCE.md](ACCEPTANCE.md) against one candidate commit and its installed package. Retain observed results.

Use the two known albums and a newly registered third album. Include a true multi-disc example and a pair of tracks intended to continue without a break. Do not assume the Mastodon digital deluxe release is a two-disc fixture merely because it contains two versions of the album.

Run a complete album, repeated object placements, a reboot, service stop/restart, a temporary Navidrome outage, and the declared process/device recovery cases. Record start latency and any audible transition gap. V1 has no numeric latency SLA. For the continuous fixture, require no audible added break relative to the source files. Resolve unexplained restarts, omitted tracks, and wrong-album selection before marking v1 complete.

**Done when:** every mandatory acceptance case passes; release evidence identifies the commit, package, device, versions, operator, and date; all remaining work is explicitly outside v1.

**Evidence:** pending. Blocked on LUC-08/LUC-11 device installation and on real-server fixtures (multi-disc, continuous pair).
