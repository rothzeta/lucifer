---
task: FIX-1
status: complete
outcome: "Review findings F1-F8 are fixed. F1 (blocking): mpv is launched with --no-terminal and --msg-level=all=no and its redirected output is drained and discarded; URLs in failure text are redacted. Build has 0 warnings and 390 tests pass (380 before, 10 new). Not run against real mpv, a Pi or systemd."
artifacts:
  - Amp.Nfc/Mpv.cs
  - Amp.Nfc/UrlRedaction.cs
  - Amp.Nfc/PlaySession.cs
  - Amp.Nfc/SelectionController.cs
  - Amp.Nfc/Options.cs
  - Amp.Nfc/CommandLine.cs
  - Amp.Nfc/ReaderLock.cs
  - deploy/lucifer.service
  - deploy/lucifer-maint
  - deploy/test-lucifer-maint.sh
  - .github/workflows/ci.yml
  - README.md
  - docs/install/INSTALL.md
  - docs/coordination/v1/FIX-1/impl-report.md
  - branch ruach/luc-11-impl
candidate_revision: 4bcca2984e78cbbbcf04e3f645d81de56c2372aa
tested_revision: 4bcca2984e78cbbbcf04e3f645d81de56c2372aa
reviewed_revision_input: d177fe674c2080fe4c528b132f8317422be9edb8
verification:
  - "Tests written first: 8 of the new tests failed against d177fe6 before the fixes (launch arguments, child pipes, URL scan, lock symlink, shutdown cap, help text)"
  - "dotnet build --no-incremental: 0 errors, 2 warnings (the two existing CA1416 test warnings; the two CS0184 warnings are gone)"
  - "dotnet test: 390 passed, 0 failed, run twice identically"
  - "bash deploy/test-lucifer-maint.sh: ok; the previous lucifer-maint run on an env line with $$ and spaces failed with 'w0rd: command not found'"
  - "deploy/publish.sh linux-arm64 and linux-arm (0.0.0-test): sha256sum -c OK; packaged unit has KillMode=mixed and SupplementaryGroups=dialout audio input"
  - "systemd-analyze verify on a scratchpad copy with ExecStart=/bin/true: exit 0"
  - "built binary: help lists LUCIFER_SHUTDOWN_TIMEOUT_SECONDS and LUCIFER_ABSENCE_CONFIRM_MS; bogus command exit 2; check with example catalogue exit 0; check with SHUTDOWN_TIMEOUT=26 exit 1 with a field error"
  - "ci.yml parsed as YAML after adding the lucifer-maint step; not run on GitHub. shellcheck not installed: not run"
  - "Not run: real mpv (its output behaviour is still from documentation), Pi, systemd target, PN532, controller"
review: not-run
findings:
  - "F1 fixed: Mpv.LaunchArguments adds --no-terminal and --msg-level=all=no; the child is started with stdout/stderr redirected and drained into a discard handler. Evidence: Mpv_is_started_without_a_terminal_and_with_log_messages_off; Mpv_child_output_is_not_forwarded_to_the_applications_own_streams (child fd 1 and 2 are pipes, not the test process's streams, with a URL-printing child); A_chatty_mpv_child_is_drained_and_never_blocks_on_its_output (6 MB of child output, child completes); Failing_streams_never_put_authenticated_url_parameters_into_application_output (fake peer sends file_error echoing the full stream URL; status text, FailureDetail and controller log contain no u/t/s parameters or server host). Other log paths: Navidrome messages and HttpRequestException text were already URL-free; new UrlRedaction.Redact is applied to mpv file_error and command-error text and to the two SelectionController 'Could not ...: {ex.Message}' lines."
  - "F2 fixed: unit uses KillMode=mixed (comments, INSTALL.md and README updated). PlaySession no longer raises FatalRunException for mpv loss, a lost required input device or a reader verdict once stop was requested. Evidence: Mpv_exiting_after_a_stop_was_requested_is_a_clean_exit_not_a_fatal_failure (3 rounds, mpv killed then stop requested, run returns without exception and socket removed). Caveat: the window is a few instructions wide, so this test passed before the guard too; it guards the behaviour but is not a deterministic fail-first test."
  - "F3 fixed: INSTALL.md usermod adds input; unit-facts line lists input."
  - "F4 fixed: stale README sentence replaced by a statement that the packaged unit already has SupplementaryGroups=input."
  - "F5 fixed (docs only, policy unchanged): README says controls during replacement wait on the lock, play/pause apply, next and previous are ignored while the new album is loading."
  - "F6 fixed: lucifer-maint parses KEY=value literally with no shell evaluation, skips blank and # lines, rejects malformed lines with exit 2, handles a missing final newline. deploy/test-lucifer-maint.sh checks a password with $$, spaces, backticks, quotes and backslashes; added to CI."
  - "F7 fixed: help and README list LUCIFER_SHUTDOWN_TIMEOUT_SECONDS and LUCIFER_ABSENCE_CONFIRM_MS; shutdown deadline is capped at 25 s with a field error naming the cap; env example, unit comment and INSTALL.md updated. Evidence: ShutdownOptionsTests."
  - "F8 fixed: ReaderLock names the lock by the resolved real path (symlinks, absolute or relative, resolve to one owner); test A_symlink_to_the_reader_and_the_reader_itself_are_one_owner. The two Assert.False(ex is NavidromeRejectedException) lines now assert the exact exception type; CS0184 warnings are gone."
test_adjustments:
  - "ShutdownOptionsTests.Invalid_shutdown_deadlines_are_rejected_by_field: added 26 and 300 as rejected values (301 retained). Required by F7; no existing expectation changed. 300 and 301 are the only values affected by the old range."
  - "NavidromeSourceTests (two tests): tautological assertions replaced by exact-type assertions (F8); nothing weakened."
discoveries:
  - "No other existing test changed or removed. Existing tests set shutdown deadlines of at most 3 s."
  - "Process.Kill of mpv in tests relies on the new draining readers being cancelled with the process; no leftover children or /tmp/ls-* directories after the runs."
  - "Real mpv's behaviour with --no-terminal/--msg-level=all=no and with a closed-pipe stdin is not tested; LUC-08 should confirm with journalctl that no 'Playing:' line appears."
  - "Board proposal (not applied): FIX-1 closes REVIEW-1 F1-F8 at the candidate above; device confirmation of F1 and F2 remains for LUC-08/LUC-12."
blockers: []
---

# FIX-1 implementer report

Per-finding evidence is in the leading block. Nothing was weakened or deleted.
