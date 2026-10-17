task: REVIEW-2
role: reviewer
status: complete
outcome: "Re-review of candidate 4bcca29 after FIX-1. No blocking findings remain. F1 is fixed in code: mpv gets --no-terminal and --msg-level=all=no, its stdout and stderr are pipes that are drained and discarded, and every app message that can carry mpv text is URL-free or redacted. Real mpv was not run. F2-F5, F7 and F8 are fixed. F6 is fixed for the reported case ($ and spaces), but lucifer-maint still parses the env file differently from systemd (N1, optional). Two more optional findings are minor doc and diagnosability items. Build passes, 390 tests pass twice, the maint test passes, and the linux-arm64 package and checksum verify."
reviewed_revision: 4bcca2984e78cbbbcf04e3f645d81de56c2372aa
previous_reviewed_revision: d177fe674c2080fe4c528b132f8317422be9edb8
review_baseline: 3f09c09e0211ef8c4416ad453c08921e9c91ac44
artifacts:
  - docs/coordination/v1/REVIEW-2/review-assignment.md
  - docs/coordination/v1/REVIEW-2/review-report.md
verification:
  - "dotnet build Lucifer.slnx: exit 0, 0 errors, 2 warnings (CA1416 at PlaySessionTests.cs:43 and MpvTests.cs:397; the CS0184 warnings are gone)"
  - "dotnet test Lucifer.slnx --no-build, run 1: 390 passed, 0 failed, 0 skipped (11 s)"
  - "dotnet test Lucifer.slnx --no-build, run 2: 390 passed, 0 failed, 0 skipped (11 s); no flakiness seen"
  - "bash deploy/test-lucifer-maint.sh: 'lucifer-maint: ok', exit 0"
  - "deploy/publish.sh --rid linux-arm64 --version review2 --out <scratch>: exit 0; sha256sum -c OK; bin/Amp.Nfc is an ELF aarch64 binary; packaged unit has KillMode=mixed, TimeoutStopSec=30, SupplementaryGroups=dialout audio input"
  - "built binary (Debug, linux-x64): help exit 0 and lists LUCIFER_SHUTDOWN_TIMEOUT_SECONDS and LUCIFER_ABSENCE_CONFIRM_MS; no args exit 2; bogus exit 2; check with the example catalogue exit 0; SHUTDOWN=25 exit 0; SHUTDOWN=26 exit 1 with a field error naming 25; URL userinfo exit 1 with a field error; the password appears in none of the outputs"
  - "systemd 257 user manager, scratch transient units: KillMode=mixed sent SIGTERM only to the main process, the child got no SIGTERM, and the leftover child was SIGKILLed right after main exited (stop took 34 ms). KillMode=control-group signalled both. With mixed, a main process that exits 1 on its own leaves an empty control group."
  - "systemd 257 EnvironmentFile versus lucifer-maint on the same scratch file: they differ on backslashes, trailing whitespace, quotes, leading whitespace, ';' comments, whitespace-only lines and lines without '=' (see N1)"
  - "Not run: real mpv (not installed here), so whether --no-terminal and --msg-level=all=no silence mpv's own output is from mpv's documentation; linux-arm package; Pi, PN532, evdev, DAC, Navidrome; system-level systemd; GitHub Actions"
review:
  - "FIXED F1 Amp.Nfc/Mpv.cs:104-124,149-161: --no-terminal and --msg-level=all=no are passed, stdout and stderr are redirected with BeginOutput/ErrorReadLine and no-op handlers, stdin is inherited (/dev/null under the unit), and stream URLs go only over IPC, not argv. All app text derived from mpv was checked: every MpvException message is a fixed string except the command-error reply (Mpv.cs:604, redacted) and end-file file_error (Mpv.cs:704, redacted). Lose/FailIncomplete/AlbumIncomplete wrap those messages, and SelectionController.cs:151,246 redact again. Navidrome and HttpClient messages carry no query string (unchanged since REVIEW-1). The tests fail without the fix and are not vacuous. Device confirmation with journalctl remains for LUC-08."
  - "FIXED F2 deploy/lucifer.service:31-33: KillMode=mixed, confirmed on systemd 257 with a scratch unit: mpv is not signalled together with the app, so the race in REVIEW-1 no longer occurs on a service stop. The PlaySession.cs:107,110 guards are effectively unreachable, because they follow the loop condition with no await between. Only the PlaySession.cs:115 guard (after StepAsync) covers a real window. The clean-stop test does not exercise any guard (see test judgement); not blocking."
  - "FIXED F3 docs/install/INSTALL.md:38,60: usermod and the unit-facts line include input."
  - "FIXED F4 README.md:91: the stale sentence is replaced and matches the unit."
  - "FIXED F5 README.md:101: the replacement rule is now qualified. Next and previous are ignored while loading. Policy unchanged, as allowed."
  - "PARTLY FIXED F6 deploy/lucifer-maint:18-29: $, spaces, backticks and quotes no longer break or change values. The parser is still not equivalent to systemd's EnvironmentFile (N1)."
  - "FIXED F7 Amp.Nfc/Options.cs:200, CommandLine.cs:36-37, README.md:53-54: the range is 1-25 with a field error, help and README list both settings, and the built binary confirms both."
  - "FIXED F8 Amp.Nfc/ReaderLock.cs:21,36-45, NavidromeSourceTests.cs:151,203: the lock is named after GetFullPath plus ResolveLinkTarget(final); the symlink test covers absolute and relative links and fails without the fix. Exact-type asserts replace the tautologies. Residual: a symlink in a directory component is not resolved (only the final component is), which is acceptable for /dev/serial/by-id and udev SYMLINK paths."
  - "OPTIONAL N1 deploy/lucifer-maint:10-13,18-29 and deploy/test-lucifer-maint.sh:30: the comment says values are taken 'literally like systemd does for unquoted values', but systemd 257 drops a backslash before the next character, so 'a\\b' becomes 'ab' and the test's '\\n=x' becomes 'n=x'. systemd also strips leading and trailing whitespace, ignores ';' comments, whitespace-only lines and lines without '=', and unquotes values. lucifer-maint keeps the backslashes and whitespace and exits 2 on those lines, so a file the service accepts can make lucifer-maint fail, and values can differ (the password with a backslash; a trailing space on LUCIFER_NFC_DEVICE or LUCIFER_IPC_DIR). Today's effects are refusals rather than unsafe behaviour: maint commands do not use the password over the network, and a different IPC dir or device path fails to create or open. Fix: either implement the systemd unquoted rules (trim, skip ';' and blank or whitespace-only lines, ignore lines without '=', drop backslash escapes) and correct the test expectation, or keep the parser and change the comment, README and env example to say 'no backslashes, no leading or trailing whitespace'."
  - "OPTIONAL N2 deploy/lucifer.service:31-32, docs/install/INSTALL.md:60, README.md:123: the docs say leftovers are SIGKILLed 'when/after TimeoutStopSec runs out'. On systemd 257 with KillMode=mixed, leftovers were SIGKILLed as soon as the main process exited. TimeoutStopSec only bounds a hung app. This is harmless, but the wording misdescribes the stop sequence. Fix: 'SIGKILLed once the app has exited (or when TimeoutStopSec runs out)'."
  - "OPTIONAL N3 Amp.Nfc/Mpv.cs:120-123,202: with the output discarded, an mpv that exits at startup (unsupported option, bad config, missing library) is reported only as 'mpv exited with code N before its IPC was ready', and the cause is lost from the journal. Fix: add an INSTALL troubleshooting line telling operators to run the same mpv command by hand. Alternatively keep the last few stderr lines, redacted, and include them only in the startup-failure message (no URL has been sent at that point)."
  - "TESTS: no FIX-1 test is vacuous except Mpv_exiting_after_a_stop_was_requested_is_a_clean_exit_not_a_fatal_failure (PlaySessionTests.cs:406-430). Kill() returns before mpv's death is observed, and stop.Cancel() follows at once, so the loop leaves through Task.Delay's cancellation and no guard runs. The test does check that teardown over a dead mpv is clean (exit, socket removed). A deterministic test of the line 115 guard is feasible with existing seams: a ScriptedReader whose Script cancels the stop source and returns a Fault, under SupervisorPolicy { FaultThreshold = 1, FaultWindow = 0 }. Without the guard, StepAsync makes FatalReason non-null after stop is requested and the run throws FatalRunException. The line 107/110 guards cannot be tested deterministically without a seam, and they protect nothing that the loop condition does not already cover. Its absence is not blocking: F2 was optional, and KillMode=mixed (verified) is what removes the race. A_chatty_mpv_child_is_drained_and_never_blocks_on_its_output would pass with inherited output too, so it guards only the drain."
  - "REDACTION: UrlRedaction.Redact (UrlRedaction.cs:8-11) replaces scheme://non-space runs with <url>. Every caller passes non-null input (Mpv.cs:604 uses ?? \"\"), and the pattern is simple with linear-ish matching on short messages, so it cannot throw or blow up. It over-redacts any URL, including non-secret ones, which is acceptable here. It does not alter text without '://'."
discoveries:
  - "No path found by which an authenticated stream URL or the Navidrome password reaches app stdout or stderr, exception text, tests or committed files at 4bcca29. Test fixtures use fake values (TOKEN, SALT, hunter2, music.example.test). The remaining unverified channel is mpv itself: real-mpv behaviour, and an operator-installed ~/.config/mpv/mpv.conf with log-file= under the lucifer home, which --msg-level does not govern. Passing --no-config would close the second one if wanted."
  - "KillMode=mixed also means that if the app dies without stopping mpv, the leftover mpv is SIGKILLed promptly (observed with a scratch unit), so crash recovery is not delayed."
  - "Acceptance mapping is unchanged from REVIEW-1 (automated and component parts only; device parts open). D01-D11 still hold; FIX-1 changes none of them."
blockers: []

# REVIEW-2: re-review after FIX-1

Reviewed revision `4bcca29` (`ruach/luc-11-impl`). The fix diff is `d177fe6..4bcca29`, excluding `docs/coordination`. The worktree is at `5dcd4de`, which adds only the FIX-1 report.

**No blocking findings remain.** F1 is fixed at code and test level. Confirming it needs a real mpv run with `journalctl` (LUC-08). N1 to N3 are optional.

## How F1 was checked

Every `MpvException` construction and every place that builds app output from `ex.Message` was traced. These are `Mpv.cs`, `SelectionController.cs`, `PlayerAlbumApplier.cs`, `PlaySession.cs` and `Cli.cs`. Only two strings can originate from mpv, and both are redacted at the source:

- the reply `error` (`Mpv.cs:604`);
- `file_error` (`Mpv.cs:704`).

Every other message is a fixed string or comes from .NET socket or HTTP errors, which contain no query string. URLs reach mpv only through IPC `loadfile`, so they never appear in `/proc/<pid>/cmdline`.

## systemd evidence (scratch, user manager, systemd 257)

The main script spawned a child that traps TERM.

- **`KillMode=mixed`:** the main process logged `main-got-TERM`. The child logged nothing, and the journal showed `Killing process … with signal SIGKILL` right after main exited. The stop took 34 ms.
- **`KillMode=control-group`:** both processes logged TERM.

The same user manager, with `EnvironmentFile=`, gave the N1 differences:

| Line in the env file | systemd value | lucifer-maint value |
| --- | --- | --- |
| `A=…\n=x` | `…n=x` | `…\n=x` |
| `B="quoted pw"` | `quoted pw` | `"quoted pw"` |
| `C=a\b  ` | `ab` | `a\b  ` |
| `  D=lead` | `lead` | — |
| a whitespace-only line, `; semi`, `noequals` | ignored | exit 2 |

The `  D=lead` line could not be shown in lucifer-maint's column, because lucifer-maint exits 2 on the earlier whitespace-only line. The env example already says "no quotes".
