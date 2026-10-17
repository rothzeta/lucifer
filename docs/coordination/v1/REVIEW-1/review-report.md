task: REVIEW-1
role: reviewer
status: complete
outcome: "Independent review of the combined v1 software candidate d177fe6 against baseline 3f09c09. Build, 380 tests (twice, no flakiness), built-binary checks and the linux-arm64 package with checksum all pass. Selection, generation/cancellation, mpv request-id/IPC, album replacement, transport, shutdown and reader-lock logic hold up under review. One blocking finding remains: the mpv child's terminal output is inherited by the service, so mpv's own 'Playing: <url>' lines put authenticated Subsonic stream URLs (u, t, s) into the journal, contradicting LUC-06 and the INSTALL claim. Seven optional findings."
reviewed_revision: d177fe674c2080fe4c528b132f8317422be9edb8
review_baseline: 3f09c09e0211ef8c4416ad453c08921e9c91ac44
artifacts:
  - docs/coordination/v1/REVIEW-1/review-assignment.md
  - docs/coordination/v1/REVIEW-1/review-report.md
verification:
  - "dotnet build Lucifer.slnx: exit 0, 0 errors, 4 warnings (CA1416 x2 in tests, CS0184 x2 in NavidromeSourceTests.cs:151,203)"
  - "dotnet test Lucifer.slnx --no-build, run 1: 380 passed, 0 failed, 0 skipped (9 s)"
  - "dotnet test Lucifer.slnx --no-build, run 2: 380 passed, 0 failed, 0 skipped (9 s); no flakiness seen"
  - "built binary: help exit 0; no args exit 2; 'bogus' exit 2; 'tag write' exit 2; '--release nope' exit 2; all before reader access"
  - "built binary: check with URL userinfo -> field error NAVIDROME_URL, password not echoed; SHUTDOWN_TIMEOUT=0 -> field error; valid env + catalogue.example.json -> exit 0, password absent from output"
  - "built binary: tag read and tag write refused (exit 1, error names reader and lock) while a separate process held the lock file; tag read on a missing device exits 1 promptly"
  - "reader lock: a refused second FileShare.None/DeleteOnClose open does not unlink the holder's lock file (scratch probe); file removed when holder exits"
  - "deploy/publish.sh --rid linux-arm64 --version review1 --out <scratch>: archive built; sha256sum -c OK; package contains bin/Amp.Nfc, lucifer-maint, config, systemd (SupplementaryGroups=dialout audio input), udev, INSTALL.md, VERSION, RID"
  - "Not run: linux-arm package, real mpv (not installed here), PN532, evdev device, DAC, Navidrome, systemd, Pi, GitHub Actions"
review:
  - "BLOCKING F1 Amp.Nfc/Mpv.cs:104-106,137-145: mpv is started without --no-terminal/--really-quiet and with inherited stdout/stderr, so under the unit its info line 'Playing: <stream URL>' (and open-failure messages) carry u, t and s into the journal; contradicts PLAYBACK LUC-06 'Do not log authenticated URLs' and INSTALL.md:119 / README.md:37. Fix: add --no-terminal (or --msg-level=all=no, or redirect and discard child output) and a launch-arguments test."
  - "OPTIONAL F2 deploy/lucifer.service:33: KillMode=control-group sends SIGTERM to mpv at the same time as the app, so mpv can die before the app sees stop; the loop (PlaySession.cs:107) can then raise FatalRunException and exit 1 on an ordinary stop. Fix: KillMode=mixed, so the app stops its own child as README.md:120 states."
  - "OPTIONAL F3 docs/install/INSTALL.md:38,60,66,69: usermod adds dialout,audio only; 'sudo -u lucifer ... input inspect' does not get the unit's SupplementaryGroups, so the documented control discovery step is likely to get EACCES, and 'id lucifer' will not show input. Fix: add input to usermod and to the 'Unit facts' line."
  - "OPTIONAL F4 README.md:90: says the unit still needs SupplementaryGroups=input and that it is 'owned elsewhere'; the unit already has it. Fix: delete the stale sentence."
  - "OPTIONAL F5 Amp.Nfc/Mpv.cs:388-391 with README.md:100: Next/Previous are Ignored in Loading, which also covers the window right after replacement and each track change, so 'controls received while an album is being replaced ... apply to the new album' is true only for play/pause. Fix: qualify the README line; optionally queue one next/previous until file-loaded. Confirm on device for A18."
  - "OPTIONAL F6 deploy/lucifer-maint:97-100: sourcing the env file in bash with set -u reinterprets values; a NAVIDROME_PASSWORD containing $ or spaces fails or changes meaning, even for tag commands that need no password. Fix: parse KEY=value literally (while IFS== read) or document the restriction on the password."
  - "OPTIONAL F7 Amp.Nfc/CommandLine.cs:166-179, README.md settings table, Options.cs:200: help omits LUCIFER_SHUTDOWN_TIMEOUT_SECONDS and LUCIFER_ABSENCE_CONFIRM_MS, README omits ABSENCE_CONFIRM_MS, and the 1-300 s shutdown range exceeds TimeoutStopSec=30. Fix: list both and cap the range below 30 or document the coupling in the help."
  - "OPTIONAL F8 Amp.Nfc/ReaderLock.cs:362 and Amp.Nfc.Tests/NavidromeSourceTests.cs:151,203: the lock name is the device path string, so /dev/lucifer-nfc and /dev/ttyUSB0 are different owners of one reader (mitigated by lucifer-maint sharing the env); the two 'Assert.False(ex is NavidromeRejectedException)' checks are compile-time constants (CS0184) and test nothing. Fix: name the lock after the resolved real path; replace the tautologies with assertions that mean something or delete them."
discoveries:
  - "Original review defects R01-R11 are fixed in source at component level; R12 (device evidence) and the device halves of R05/R09/R11 are deliberately left to LUC-08/LUC-12. Details below."
  - "Implementer and integration reports, README and INSTALL scope their claims correctly to substitutes (fake mpv peer, scripted reader, files/FIFOs) and mark device items UNVERIFIED-ON-DEVICE; ACCEPTANCE.md is unchanged and claims no case as passed."
  - "mpv's terminal-output behaviour in F1 is from mpv's documented info logging; no mpv binary was available to show it here. The LUC-08 run should confirm with journalctl after F1 is fixed."
blockers:
  - "F1 (authenticated stream URLs reach the journal through mpv output) must be fixed before this candidate is accepted."

# REVIEW-1: Lucifer v1 software candidate

Reviewed revision `d177fe6` (`ruach/luc-11-impl`), diff `3f09c09..d177fe6` excluding `docs/coordination`. The worktree is at `b978e9f`, which adds only the INT-5 report on top of `d177fe6`. Scope: LUC-01..07, 09, 10 and the LUC-11 packaging, CI and service parts.

## Findings

### F1: authenticated stream URLs reach logs through mpv (blocking)

- **Location:** `Amp.Nfc/Mpv.cs:104-106` (`ProcessStartInfo` without redirection), `Amp.Nfc/Mpv.cs:137-145` (`LaunchArguments`), `Amp.Nfc/Navidrome.cs` `CreateStreamUrl` (`u`, `t`, `s` in the query).
- **Problem:** the child inherits the app's stdout and stderr, which the unit sends to the journal. No `--no-terminal`, `--really-quiet` or `--msg-level` is passed. For each loaded file mpv logs `Playing: <url>` at info level, and it also logs messages when a file fails to open. Here the URL contains the user name, token and salt.
- **Why it matters:** `t` and `s` together are a replayable Subsonic credential. PLAYBACK LUC-06 requires "Do not log authenticated URLs". INSTALL.md:119 says "Credentials are never printed", and README.md:37 says configuration errors never print stream URLs. The app code avoids the URLs carefully, but its child undoes that. INSTALL also tells operators to share `journalctl` output when diagnosing problems.
- **Evidence:** code reading. No mpv binary was available here, so the log line comes from mpv's standard terminal output, not a run.
- **Suggested direction:** add `--no-terminal` to `LaunchArguments`, or redirect the child's stdout and stderr and discard them. Diagnostics then come from IPC `end-file` and `file_error`, which carry no URL. Extend `Mpv_is_started_without_playlist_or_file_repeat` to assert this.

### F2 to F8 (optional)

These are summarised in the leading block, with locations and fixes. The main reasoning:

- **F2:** under `KillMode=control-group`, systemd sends SIGTERM to every process in the cgroup at the same moment. If mpv exits before the .NET SIGTERM handler cancels `stop`, the loop at `PlaySession.cs:107` checks `mpv.Lost` first and exits 1 with "mpv is gone". The stop is not restarted, but the unit can end `failed` and the logs mislead. `KillMode=mixed` still SIGKILLs leftovers after `TimeoutStopSec`. This is a race found by reading; it was not reproduced.
- **F3/F4:** these are doc inconsistencies introduced when INT-5 added `input` to the unit.
- **F5:** this follows LUC-09 literally, since the controls "run after replacement". In practice, a next pressed during a replacement or a track change is dropped, and the README says otherwise.

## Original defects (docs/reviews/2026-10-17-v1-review.md)

| ID | State at d177fe6 | Evidence |
| --- | --- | --- |
| R01 failed attempt suppresses tag | Fixed | `SelectionController` phases Unread/Retrying/Preparing/Applied/Suppressed; bounded backoff `Transient()`; tests `Transient_*`, `Retries_are_bounded_*` |
| R02 one null rearms | Fixed | `OnAbsent` needs at least 2 nulls and `AbsenceConfirmation`; Fault never confirms absence |
| R03 events parsed as replies | Fixed | `Mpv.SendAsync`/`Handle`: integer `request_id`, events routed, unrelated or late replies ignored, reply/send/connect deadlines |
| R04 writer not executable | Fixed | Root `Program.cs` removed; `tag write` in one executable; built-binary checks above |
| R05 unverified write | Fixed (component) | `TagWriter` validates first, reads back, compares, throws on mismatch. Whether the binding's read-back re-reads the tag is device-pending (A05) |
| R06 compiled mappings | Fixed | `Catalogue`/`CatalogueFile`, re-read on every tap |
| R07 envelope ignored | Fixed | `Navidrome.Parse` reads status and error first; typed outcomes; credential-free messages |
| R08 queued reported as playing | Fixed | Status comes from mpv events with a stream-start deadline; `AlbumIncomplete` stops and reconciles before any reload |
| R09 inert app after fatal failure | Fixed (component) | `PlaySession` supervisor, `ReaderWatch`, startup ownership in `Mpv.StartAsync`, `ShutdownHooks`; systemd recovery is device-pending |
| R10 no control route | Fixed (component) | evdev adapter, mapping file, `PlaybackOwner`; real controller pending (A18) |
| R11 not reproducible | Fixed in repo, device-pending | Options model, unit, tmpfiles, publish script, INSTALL; F2/F3/F7 are refinements |
| R12 no device proof | Consciously deferred | LUC-08/LUC-12; ACCEPTANCE unchanged |

## Acceptance (automated and component parts only)

- **A01, A02, A04, A07, A12, A13, A14:** automated parts are covered and pass. A01 and A02 were checked directly on the built binary.
- **A05, A08-A11, A15-A17, A21:** component parts pass with substitutes. Their device parts remain open.
- **A18:** component policy passes, with the F5 caveat. The device part is open.
- **A19, A20:** supervisor policy is unit-tested. The device part is open.
- **A03, A06, A22, A23:** device only; open. The A23 package and checksum were checked here for linux-arm64.

No report overstates a mixed-mode case.

The decisions hold in code. D01 has one reader owner through the lock and a single poll loop. D04 keeps the codec format exactly. D05 uses the external catalogue. D06 and D07 are implemented by the controller generation, superseding work and the commit semaphore. D08 has no app-side volume. D10 has one app project and one test project, self-contained and without AOT. D11 adds no tag writes.

## Areas checked without findings

- **Generation and cancellation:** the stale check runs under the gate, cancellation is honoured up to the first mpv command, and the late-A-cannot-replace-B ordering holds.
- **mpv operation lock:** album replacement and transport are serialized.
- **Entry-id filtering:** old-entry events are filtered by entry id.
- **Partial-queue policy:** a rejected first command does not count as a mutation, and every other failure leads to stop and reconcile.
- **Shutdown:** shutdown is bounded at both levels, and the child is killed on startup cancellation.
- **Stale sockets:** a socket that nothing answers on is removed, and a live one is refused.
- **Reader lock:** a refusal does not remove the holder's file.
- **Secrets:** the diff contains no credentials.
- **Unverified areas:** real mpv event ordering and terminal output, the PN532 binding's behaviour under faults, evdev on the Pi, the linux-arm package, CI on GitHub.
