task: CODE-REVIEW-2
role: reviewer
status: complete
outcome: "FIX-2 (85a255b..a446916, code 629664d) reviewed. No BLOCKING findings. Every claimed fix is present and does what it says. Build, 3x399 tests, the lucifer-maint test and systemd-analyze verify all pass. Acceptance check (3) does NOT hold as written: removing the stop guard at PlaySession.cs:107 is caught by no test, because that guard cannot be reached in any deterministic order. The rewritten test does catch the realistic regression (an unguarded loss check after StepAsync), which the old test missed. 7 OPTIONAL findings."
reviewed_revision: a446916
candidate_revision: 629664d
artifacts:
  - docs/coordination/v1/CODE-REVIEW-2/review-report.md
  - docs/coordination/v1/CODE-REVIEW-2/review-assignment.md
verification:
  - "dotnet build: 0 errors, 0 warnings"
  - "dotnet test --no-build, 3 runs: Passed 399, Failed 0, Skipped 0 each time"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok (exit 0)"
  - "systemd-analyze verify (systemd 257) on a scratch copy of deploy/lucifer.service with ExecStart=/bin/true and EnvironmentFile=-/nonexistent: no output, exit 0"
  - "S1 parity: 28 env-file shapes fed to systemd-run --user --pipe -p EnvironmentFile= and to deploy/lucifer-maint with a stub Amp.Nfc. Every shape that systemd reads differently is refused (exit 2, value not printed), except invalid UTF-8 and a CR inside a value (OPTIONAL S1b). The shapes that are accepted give identical values: $, spaces, a quote in the middle, = and # inside a value, empty value, tab inside, duplicate key, no final newline, CRLF, a comment line ending in a backslash, a leading NBSP"
  - "S3 probe: systemd-run --user -p ProtectSystem=strict -p PrivateUsers=yes: /tmp and /var/tmp are read-only, RuntimeDirectory= is writable. The built Amp.Nfc starts under strict (it reaches the reader). With LUCIFER_IPC_DIR unset it fails loudly: 'Error: Read-only file system : /tmp/lucifer-<user>'"
  - "Mutation check (scratch copy from git archive): (a) removed '&& !stop.IsCancellationRequested' at PlaySession.cs:107. Clean-stop test passes, full suite 399/399 passes, so the mutant survives. (b) Added an unguarded 'if (mpv.Lost...) throw' after StepAsync. The new test fails with FatalRunException 'mpv exited with code 137'; the 85a255b version of the test passes it"
  - "Private-info scan of git diff 85a255b..a446916, commit metadata and messages: no email, secret, token or private IP. Commits use the noreply address. One local home path is in docs/coordination/v1/FIX-2/impl-report.md:9 (the dotnet path), the same class as the earlier P1"
  - "Not run: device or hardware (hardening on a real Pi), publish.sh, shellcheck, a root-owned foreign IPC directory case"
review:
  - "OPTIONAL T1 (low, tests/simplicity) Amp.Nfc/PlaySession.cs:107 (and :110), Amp.Nfc.Tests/PlaySessionTests.cs:407-440: the guard '&& !stop.IsCancellationRequested' on the mpv-loss check is never reached once stop is set. The while condition (:105) exits first, and StepAsync and Task.Delay(Tick, stop) throw OperationCanceledException. Removing it leaves all 399 tests passing, so acceptance check (3) fails as stated. The impl report's claim that the test now pins the guard is inaccurate. The clean-stop guarantee comes from the loop shape, and the new test does pin that (mutant (b) is caught). Fix: drop the redundant guards at :107/:110, or keep them and say in the test comment that it pins the loop order, not that guard."
  - "OPTIONAL S1b (low, correctness) deploy/lucifer-maint:36-42: two shapes are accepted that systemd reads differently. A value with invalid UTF-8 (systemd 257 drops the variable; maint exports it) and a CR inside a value ('A=a\\rb': systemd gives A=a; maint gives a<CR>b). Fix: also refuse any control character other than tab in a value (add *[[:cntrl:]]* minus tab) and invalid UTF-8 (for example iconv -f UTF-8 -t UTF-8 on the value). Add both to the refused() cases."
  - "OPTIONAL S3b (low, docs) deploy/lucifer.service:40-42, docs/install/INSTALL.md:63: the reason given for omitting PrivateTmp ('lucifer-maint shares the IPC directory and lock') is wrong. The shared directory is /run/lucifer, which PrivateTmp does not affect. Under ProtectSystem=strict without PrivateTmp, /tmp is read-only (verified). That is in fact the useful property: with LUCIFER_IPC_DIR unset the service fails loudly instead of quietly locking a private /tmp/lucifer-lucifer that maintenance runs cannot see. Fix: state that reason, and that /tmp is read-only for the service."
  - "OPTIONAL C1b (low, readability) Amp.Nfc/IpcDirectoryPolicy.cs:37-41: a dangling symlink or a regular file at the default path makes Directory.Exists false. CreateDirectory then throws a raw IOException ('already exists'), because the when-filter is false, so the clear 'Remove it or set LUCIFER_IPC_DIR' message never shows. The run is still refused, so it is safe. The catch is otherwise dead, since CreateDirectory does not throw for an existing directory. Fix: drop the Exists/catch and run the LinkTarget/mode checks after a plain CreateDirectory, mapping IOException to Unsafe(path, ...)."
  - "OPTIONAL Y3 (low, simplicity) Amp.Nfc/Cli.cs:113 and Amp.Nfc/PlaySession.cs:73: IpcDirectoryPolicy.Prepare runs twice on every play. Fix: keep the Cli call (it guards the reader lock for all modes) and leave PlaySession to the caller, or keep only one with a comment."
  - "OPTIONAL TC1 (low, tests) deploy/test-lucifer-maint.sh:26: the literal-password case dropped the quotes inside the value ('\"q\"' became 'q'). Mid-value quotes are accepted, and systemd keeps them (verified), so that coverage was lost for no reason. Fix: restore a mid-value quote such as 'a\"b\"c'."
  - "OPTIONAL P2 (low, private info) docs/coordination/v1/FIX-2/impl-report.md:9: a local home directory path, the same class as the earlier P1 owner decision. Fix: write 'local DOTNET_ROOT' in future coordination reports, and include it in any P1 history cleanup."
discoveries:
  - "C1 race analysis: in sticky /tmp, a directory another user creates first is refused (0700 means the write probe fails; any other mode fails the mode check). A symlink planted at any point before the checks is refused through LinkTarget. Once the directory is ours it cannot be swapped. Remaining limit, as documented: root passes a directory owned by someone else. The DoS (an attacker pre-creates the directory) gives a clear error."
  - "S3 device risk: /dev is untouched (no PrivateDevices or DeviceAllow), so tty, input and snd stay reachable. ProtectHome is harmless with HOME=/var/lib/lucifer and mpv --no-config. AF_NETLINK covers glibc AI_ADDRCONFIG and .NET NetworkChange. Still unverified on a device, as INSTALL.md says."
  - "I1: Mpv.ReadLoopAsync is called synchronously (Mpv.cs:258), so the token is captured before any Dispose. InputControls.Dispose cancels before disposing, and a token from a cancelled, disposed source is still safe for the running loops. No disposal race found. SelectionController and ReaderWatch reads are under _gate."
  - "Y1, Y2, S2, S6, S7, R2-R5, K1, N2: present and complete. No amp-nfc name is left outside docs/coordination and docs/reviews. Navidrome.cs:43 is now the only HTTP deadline."
blockers: []

# CODE-REVIEW-2 report

Reviewed: `git diff 85a255b..a446916` (code revision 629664d) and the repository state at a446916, against the findings in `ruach/code-review:docs/coordination/v1/CODE-REVIEW/review-report.md` and the claims in `docs/coordination/v1/FIX-2/impl-report.md`. S5 and R1 were skipped by assignment and remain open. P1 still needs an owner decision.

**No blocking findings remain.** The one item to settle is acceptance check (3). The guard it names is redundant, so no deterministic test can detect its removal (T1). The coordinator should either accept the test as pinning the loop order or ask for the guard to be removed.

## S1 parity table (systemd 257 vs lucifer-maint)

| shape | systemd | lucifer-maint |
| --- | --- | --- |
| `A=pa$$ w0rd $HOME \`id\`` | literal | same |
| `A=a"b"c`, `A=a'b`, `A=a=b`, `A=x #y`, `A=`, tab inside | literal | same |
| `# c \` then `A=…` | A set | same |
| CRLF, no final newline, duplicate key | `crlf`, value, last wins | same |
| `A=a\b`, `A= lead`, `A=trail `, `A="q"`, `  A=ind`, trailing VT/FF | changed | refused, exit 2 |
| `; semi`, `A =x`, `export A=x` | comment / `x` / ignored | refused, exit 2 |
| `A=\xff\xfe` (invalid UTF-8) | variable dropped | **exported** (S1b) |
| `A=a\rb` | `a` | **`a\rb`** (S1b) |

## Mutation evidence (check 3)

The loop in `PlaySession.RunCoreAsync` is `while (!stop) { if (Lost && !stop) throw; … await StepAsync(stop); …; await Task.Delay(Tick, stop); }`. The new test cancels `stop` inside the reader poll after mpv is dead. The loop then goes straight to `Task.Delay`, which throws OperationCanceledException, so the check at :107 never runs while `stop` is set. Mutant (a) removes that guard and survives. Mutant (b) adds an unguarded loss check after `StepAsync` and is caught by the new test only.
