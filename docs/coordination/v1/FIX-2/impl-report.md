task: FIX-2
role: implementer
status: complete
outcome: "All assigned findings fixed except S5 and R1 (skipped as instructed); P1 needs nothing in touched text. Build 0 errors, 0 warnings; 399 tests pass; lucifer-maint script test and systemd-analyze verify pass."
candidate_revision: 629664d
tested_revision: 629664d
artifacts:
  - docs/coordination/v1/FIX-2/impl-report.md
  - docs/coordination/v1/FIX-2/impl-assignment.md
  - "branch ruach/fix-2 (4 fix commits on top of 85a255b)"
verification:
  - "dotnet build (DOTNET_ROOT=<home>/.dotnet): 0 errors, 0 warnings (was 2 CA1416 plus one new one, all removed)"
  - "dotnet test --no-build: Passed 399, Failed 0, Skipped 0 (was 390; +9 new)"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok, including seven refusal cases that check exit 2 and that the value is not echoed"
  - "systemd-analyze verify of deploy/lucifer.service with ExecStart=/bin/true and EnvironmentFile=-/nonexistent: no output, exit 0"
  - "Mpv_exiting_after_a_stop and ReaderLock tests run 3 times in a row: all pass"
  - "Not run: publish.sh, shellcheck, device or hardware, a mutation check that the rewritten clean-stop test fails if the stop guard is removed"
review:
  - "S1 fixed: deploy/lucifer-maint refuses (exit 2, key and line number named, value never printed) a backslash, a leading or trailing space on a value, a value starting or ending with a quote, and an indented non-comment line; comment, lucifer.env.example and INSTALL.md corrected; test now asserts the refusals instead of the wrong literal result. Whitespace-only lines are skipped as systemd does."
  - "C1 fixed: IPC default is $XDG_RUNTIME_DIR/lucifer (absolute path only) else /tmp/lucifer-<user>; new IpcDirectoryPolicy.Prepare creates it 0700 and refuses a symlink, another mode, or a directory not writable by the user; LUCIFER_IPC_DIR is used as given and not policed (deploy keeps /run/lucifer). Help and README updated. Limit: ownership is proven by mode 0700 plus a successful write probe, not by an owner uid check (.NET has no owner API); root would pass a directory owned by someone else."
  - "S2 fixed: --no-config and --ytdl=no added with a test; comments adjusted."
  - "S3 fixed: ProtectSystem=strict, ReadWritePaths=/var/lib/lucifer, ProtectHome, ProtectKernelTunables/Modules/Logs, ProtectControlGroups, RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK (netlink added for getaddrinfo/ALSA; a guess). No PrivateTmp/PrivateDevices. INSTALL.md says it is unverified on a device."
  - "S4 fixed: https in the env example with a replay note."
  - "S5 skipped (needs network to resolve SHAs); remains open."
  - "S6 fixed: LogText.Clean replaces control characters in logged titles and tag text; test through PlayerAlbumApplier output."
  - "S7 fixed: DeleteOnClose removed, lock file kept; test renamed and extended; README and INSTALL.md wording corrected."
  - "I1 fixed: AppliedRelease and FatalReason read under their locks; Mpv._life and the InputControls CTSs disposed (the Mpv read loop takes its token once so a late unwind cannot touch a disposed source)."
  - "Y1 fixed: PauseAsync/ResumeAsync/NextAsync/PreviousAsync removed from Mpv; tests use extension methods in MpvCommandExtensions over an internal Mpv.CommandAsync seam (CommandAsync has no production caller)."
  - "Y2 fixed: PlaybackOwner Status/FailureDetail/StatusChanged, the scheme recheck in PlayerAlbumApplier, the HttpClient.Timeout in Player (Navidrome keeps its deadline) and InputMapping.Empty plus its test removed."
  - "R1 skipped as instructed (large refactor)."
  - "R2 fixed: Amp.Nfc used in help, InputControls hint, input-map example and README."
  - "R3 fixed: comments in PlaybackOwner, PlayerAlbumApplier, SelectionTypes name real callers. Remaining LUC-08/LUC-12 mentions refer to open device work and were left."
  - "R4 fixed: Milliseconds(env, name, ...) matches Deadline; WaitForTag and neighbours in TagCommands put arguments on one line."
  - "R5 fixed: [assembly: SupportedOSPlatform(\"linux\")] in both projects."
  - "K1 fixed: README documents the fixed 2/2/5 s IPC and 20 s stream-start deadlines."
  - "N2 fixed: unit comment, INSTALL.md and README say leftovers are killed once the app has exited, or at TimeoutStopSec if it hangs."
  - "Clean-stop test strengthened: the reader poll now kills mpv, waits for exit, lets the player record the loss, then requests the stop; asserts the run completed (RanToCompletion), the socket is gone and the child is dead. Before, Kill and Cancel ran back to back so the test passed whatever the guard did."
  - "P1: no personal identifier in any non-coordination text I touched (the remaining rothzeta mentions are public repository URLs in docs/reviews, untouched). History and docs/coordination left alone as instructed."
discoveries:
  - "Class named IpcDirectory clashed with the IpcDirectory option property, so the helper is IpcDirectoryPolicy."
  - "The HttpClient.Timeout removal means the Navidrome deadline (linked CTS) is the only 15 s bound; HttpClient keeps its 100 s default as a backstop."
  - "AF_NETLINK in RestrictAddressFamilies was added beyond the assignment's list as a precaution; remove it if the device shows it is not needed."
blockers: []

# FIX-2 implementation report

Base 85a255b. Fix commits on ruach/fix-2, in order: S1/S4 (deploy), C1/R5, S2/S6/I1/Y1/Y2/R3, then S7/S3/R2/R4/K1/N2 with the clean-stop test. The revision tested is 629664d; this report is committed after it.
