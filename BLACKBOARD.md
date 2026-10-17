---
id: lucifer
type: project
status: active
priority: P1
updated: "2026-10-17"
focus: "Complete a reliable NFC album player on the existing Pi"
next: [LUC-08, LUC-09, LUC-11, LUC-12]
blocked_by: []
related: [lucifer-tags, lucifer-playback, lucifer-device]
---

# Lucifer v1 completion blackboard

## Goal

Program an NFC object for an album. Place it on the reader. Hear that exact album from Navidrome through the FiiO K5 Pro. Add another album without editing C#. Operate the device after a reboot and recover from routine failures.

The core playback path is present in source. V1 is complete when the [acceptance record](blackboards/ACCEPTANCE.md) contains passing evidence for the release candidate.

## Attention

1. Start **LUC-08** on the real Pi. Confirm the current hardware path and installed OS. This determines whether the remaining work is software completion or hardware diagnosis.
2. Start **LUC-01** to make the writer a usable command.
3. Start **LUC-05** to fix mpv reply handling.
4. Complete **LUC-04** before judging scan reliability. The current loop both suppresses retries after failure and rearms after a single uncertain read.
5. Keep implementation changes small. Extract responsibilities while fixing behaviour.

**Review baseline:** `f49202a22613ff87320568ab0fe55fe90012de21`, 17 October 2026. See the [source review](docs/reviews/2026-10-17-v1-review.md).

## Active boards

Child boards own task status, dependencies, owner, and completion evidence. This table is an index.

| Board | Scope | Next work | Completion result |
| --- | --- | --- | --- |
| [Tags](blackboards/TAGS.md) | LUC-01 to LUC-04 | Runnable commands, tag contract, catalogue, stable selection | A new programmed object selects its album reliably |
| [Playback](blackboards/PLAYBACK.md) | LUC-05 to LUC-07 | IPC replies, API errors, complete album replacement | The correct queue plays and failures remain observable |
| [Device](blackboards/DEVICE.md) | LUC-08 to LUC-12 | Physical baseline, controls, lifetime, install, release checks | The Pi works as an everyday player |

## V1 scope and decisions

“Recovered” identifies earlier user direction. “Existing” identifies behaviour or a format in the reviewed source. “Proposed” is the completion policy used by this plan. It is not a claim that the user previously approved that detail.

| ID | Basis | Decision |
| --- | --- | --- |
| D01 | Recovered, 28 August | Keep C# and direct PN532 access through the USB-to-TTL adapter. Keep one reader owner. |
| D02 | Recovered and existing | Navidrome is the first music source. mpv is the local audio player. Keep those responsibilities separate. |
| D03 | Recovered, 17 August | The physical album concept is a standard ceramic NFC ring with a distinct album case or artwork. Full sculptures and elaborate reliefs are later work. Verify the actual ring chip; the source writer names NTAG215. |
| D04 | Existing format; earlier design rationale | Keep `amp:album:1:mbid:<release-uuid>`. Use an exact MusicBrainz release identity. Keep the existing `amp:` namespace when displaying the Lucifer product name. |
| D05 | Proposed v1 scope | Store exact release-to-Navidrome mappings in a small configuration file. Adding a third album must require no source edit. Automatic catalogue import can follow v1. |
| D06 | Existing behaviour, retained as proposed policy | A valid new presentation starts the album from the beginning. A stationary object does not restart it. Removal keeps current audio playing. A confirmed removal followed by re-presentation restarts the album once. |
| D07 | Proposed reliability policy | A newer valid selection supersedes older pending work. Keep current audio during preparation. A preparation failure leaves it playing. A failure after queue replacement is explicit and recoverable. |
| D08 | Proposed v1 operation | Provide play/pause, next, and previous through the selected controller after inspecting its events. The FiiO knob supplies volume control. KB1K was part of earlier hardware discussions; its mapping is a bench task. |
| D09 | Recovered uncertainty; proposed scope | Screen size and final enclosure were not settled. Use a simple stable reader placement for v1. A display is not a release gate in this plan. |
| D10 | Proposed delivery | Use one application project and one test project. Publish for the actual Pi OS. Keep Native AOT as a later optimisation unless target evidence makes it necessary. |
| D11 | Recovered future interest; proposed deferral | Preserve the future direction of play counts, including tag-side data. Counting semantics and writes are outside this first release. |

The required result is one device, one selected album, one provider, and one audio output. Multiroom playback, additional services, browser administration, artwork screens, custom firmware, automatic recommendation, and general plugin loading are outside this v1.

## Cross-board dependencies

| Work | Must have |
| --- | --- |
| LUC-01, LUC-05, LUC-08 | No task prerequisite |
| LUC-02 | LUC-01 |
| LUC-03 | LUC-02 |
| LUC-04 | LUC-02 |
| LUC-06 | LUC-03 |
| LUC-07 | LUC-04, LUC-05, LUC-06 |
| LUC-09 | LUC-05, LUC-07, LUC-08 |
| LUC-10 | LUC-01, LUC-05, LUC-08 |
| LUC-11 | LUC-02, LUC-03, LUC-07, LUC-09, LUC-10 |
| LUC-12 | LUC-11 |

Start the three independent tasks together where practical. Coordinate edits to the existing player file; extract an owned component before concurrent changes.

## Execution plan to v1

Deliver the following milestones in order. The child boards retain task status and detailed acceptance requirements. This sequence is a default for one implementer; independent work may proceed whenever its listed prerequisites are complete.

| Milestone | Tasks in implementation order | Deliverable | Exit gate |
| --- | --- | --- | --- |
| 0 Confirm the target | LUC-08 | Recorded Pi, reader, tag, DAC, controller, and server inventory; one attempted source-to-audio run | Record the observed result and any hardware blocker in the acceptance record. Select the publish architecture and revise the effort estimate. |
| 1 Program and register albums | LUC-01 → LUC-02 → LUC-03 | One executable with validated configuration, shared tag codec, verified writing, and an external catalogue | Build and automated portions of A01, A02, A04, and A05 pass; a third mapping resolves without rebuilding. Physical write-to-play evidence follows on the Pi. |
| 2 Select and play reliably | LUC-05 → LUC-04 → LUC-06 → LUC-07 | Correlated mpv replies, observable playback state, stable tag presence, bounded retries, explicit API outcomes, and complete queue replacement | Relevant automated checks in A07–A17 pass, including held-tag suppression, transient recovery, complete ordering, and delayed A superseded by B. No partial queue is reported as complete playback. |
| 3 Operate and recover | LUC-10 → LUC-09 | Bounded shutdown, fatal-fault exit policy, and the selected transport input | Component checks cover stop during startup/HTTP/IPC and control behaviour. Exercise the actual reader and controller on the Pi; systemd recovery is confirmed after installation. |
| 4 Install the candidate | LUC-11 | CI build and tests, publish package, configuration examples, systemd unit, and installation/update/restore instructions | Install without a development checkout. Reboot, controls, service stop/restart, and package replacement work on the Pi. |
| 5 Release v1 | LUC-12 | One installed candidate with complete acceptance evidence | All A01–A23 pass for the candidate, including the third album, a full album, multi-disc order, continuous tracks, 20 placements, and fault recovery. Record the release commit and package. |

Start the target baseline first because reader compatibility and placement can change the implementation effort. If the Pi is unavailable, proceed with milestones 1 and 2 using reader, HTTP, and IPC substitutes. Record the missing device evidence explicitly; keep the target baseline and physical release gates open. Inspect controller events before implementing its input mapping.

### First implementation change

Take LUC-01 as the first code change. Keep `Amp.Nfc/Amp.Nfc.csproj` as the application project, bring the root writer behind explicit command dispatch, and extract options and reader ownership only as needed for that task. Add one test project for command/configuration behaviour that must work without hardware. Defer codec, catalogue, and playback fixes to their own tasks so this change has a clear review boundary.

Validate with an application build and command checks from the built output. Help, invalid commands, and missing writer arguments must finish without opening the reader; writer mode must not need Navidrome credentials. Confirm conflicting reader ownership through an appropriate boundary check and later on the target. Record the commit and results in LUC-01 before starting LUC-02.

### Risks that can change the plan

| Uncertainty | Resolve it with | Consequence |
| --- | --- | --- |
| Actual ring chip, antenna placement, or UART path does not work | LUC-08 inventory and repeatable physical reads | Diagnose the hardware path before promising a device completion date. Software boundary work may continue. |
| Exact release mapping or track positions are incomplete on the server | LUC-03 registration and LUC-06 response fixtures, then real-server checks | Verify mappings explicitly and use the documented ordering fallback; keep automatic matching outside v1. |
| Actual controller events differ from the proposed mapping | LUC-08 event inspection | Choose and document the available input path before LUC-09. Keep the existing play/pause, next, and previous release requirements. |
| Stream startup or continuous playback fails on the installed device | A16 and A22 with the actual server, files, mpv, and DAC | Fix the observed playback failure before release; a successful IPC response cannot close this gate. |

Use the existing estimate of 5–8 focused implementation sessions plus device checks only if the target baseline confirms the physical chain. It is an initial planning allowance, not a delivery date. Re-estimate after milestone 0 and after reliable album replacement in milestone 2. Keep displays, enclosure refinement, automatic import, play counts, and additional providers outside this release.

## Shared signals

| Signal | Current evidence |
| --- | --- |
| NFC to Navidrome to mpv flow | Implemented in source; current target run pending |
| Programmed identity | NDEF text with a release UUID |
| Album registration | Two compiled mappings |
| Writer delivery | Source present; runnable command pending |
| Controls | Transport policy and evdev adapter implemented; no default key codes; real controller events and mapping pending on the device |
| Build and tests | Delivered revision 7dd4cf8 (source = reviewed 4bcca29): 0 build errors, 390 tests pass (fakes and stubs). Earlier note: initial review ran no build. Planning workspace has .NET SDK 10.0.110; build and application tests remain unrun. mpv and target devices are unavailable here. |
| Device release | No target acceptance evidence recorded. LUC-08 blocked on Pi access; software candidate delivered on ruach/lucifer-v1-orchestrator; linux-arm64 and linux-arm packages build here |
| Completion estimate | Planning judgement: about 5–8 focused implementation sessions plus device checks, if LUC-08 confirms the physical chain works |

## Definition of done

- All twelve tasks have completion evidence.
- Every mandatory acceptance case passes on the candidate commit.
- A third album is added and programmed without a C# change.
- The correct complete album plays, including track and disc transitions.
- A stationary tag causes one selection. Transient failures do not require manual repair.
- The agreed controls work in service mode.
- Installation from the published package, reboot, shutdown, and restart pass on the Pi.
- The device, configuration, runtime versions, and measured behaviour are recorded.

A passing simulated test does not close a physical acceptance case. A successful IPC reply does not establish audible output.

## Working rules

Use `ready`, `todo`, `active`, `blocked`, or `done` for tasks. Use `blocked` only when a named external condition prevents work. Every task starts unassigned.

Implementation tasks close on evidence for their own responsibility. They may use substitutes at hardware or protocol boundaries. References to acceptance cases map coverage; they do not make an early task depend on later integration or installation. LUC-12 closes every complete integrated and device case against the installed candidate.

Claim one task before editing its files. Record the owner, candidate commit, test result, and remaining limit. Update this index only when the focus, dependency, or shared signal changes. Keep task state in its child board. Use Git history for completed activity.

The writing uses short procedural statements inspired by the previously selected Command Center layout. It does not claim formal ASD-STE100 certification.

## Recent changes

- 17 October 2026: Orchestrator delivered the v1 software candidate (LUC-01 to LUC-07, LUC-10, packaging parts of LUC-11, controls software of LUC-09) onto ruach/lucifer-v1-orchestrator at 7dd4cf8 after independent review; device evidence and LUC-08/09/11/12 completion remain open. See docs/coordination/v1/task-record.md.
- 17 October 2026: Recovered the physical album concept and current software direction.
- 17 October 2026: Reviewed the first repository commit and the relevant official protocols.
- 17 October 2026: Created three workstream boards, twelve tasks, and a release acceptance record. Application code is unchanged by this documentation work.
- 17 October 2026: Checked out PR #1 and added an ordered execution plan, milestone gates, the first implementation change, and risks that can change delivery.
