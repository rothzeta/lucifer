task: FIX-4
role: implementer
status: complete
outcome: "C1c, S8b, TC2, S1c, R1b and S5b are fixed in small commits. P5 and N1 were skipped as instructed. Build is clean and all 408 tests pass 3 times."
candidate_revision: 4d48fce5607d0efd574bcaa59da5e8c95d6a23e6
tested_revision: 4d48fce5607d0efd574bcaa59da5e8c95d6a23e6
artifacts:
  - docs/coordination/v1/FIX-4/impl-report.md
  - docs/coordination/v1/FIX-4/impl-assignment.md
  - Amp.Nfc/IpcDirectoryPolicy.cs
  - Amp.Nfc/LogText.cs
  - Amp.Nfc.Tests/IpcDirectoryTests.cs
  - Amp.Nfc.Tests/LogTextTests.cs
  - deploy/lucifer-maint
  - deploy/test-lucifer-maint.sh
  - Amp.Nfc/Mpv.Ipc.cs
  - Amp.Nfc/Mpv.Launch.cs
  - Amp.Nfc/Mpv.Status.cs
  - Amp.Nfc/Mpv.Transport.cs
  - Amp.Nfc/MpvTypes.cs
  - .github/workflows/ci.yml
  - "branch ruach/fix-4"
verification:
  - "dotnet build: 0 Warning(s), 0 Error(s)"
  - "dotnet test, 3 runs: Passed 408, Failed 0, Skipped 0 each run"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok'"
  - "systemd-analyze verify on lucifer.service with ExecStart replaced by /bin/true: no output, exit 0"
  - "read-only gh api: actions/checkout tag v4.4.0 is a commit 11d5960a326750d5838078e36cf38b85af677262 and actions/setup-dotnet tag v4.3.1 is a commit 67a3573c9a986a3f9c594539f4ab511d57bb3ce9, both equal to the pinned SHAs"
  - "Not run: the new IPC test against the old code (no red-first run), the CI workflow, shellcheck, hardware checks"
discoveries:
  - "C1c: Prepare now puts both CreateDirectory calls in one try. On IOException it reports 'is a symbolic link' or 'is not a directory' only when a link or file is at the path. Otherwise it throws InvalidOperationException 'Default IPC directory X cannot be created: <system reason>. Set LUCIFER_IPC_DIR to a directory you own.' with the IOException as inner exception, so the hint for an unset LUCIFER_IPC_DIR stays. The new test makes the runtime directory's parent a file, which gives an IOException with nothing at the default path. A real read-only file system was not reproduced (needs unshare)"
  - "Because the parent creation is now inside the try, an IOException there is reported through the same message instead of raw. UnauthorizedAccessException is still raw"
  - "S8b: LogText.Clean now enumerates runes and replaces control, Format, LineSeparator and ParagraphSeparator characters with one '?' per character. A lone surrogate becomes U+FFFD. New LogTextTests cover U+E0041, U+2028, U+2029, U+202E, ESC and printable non-ASCII"
  - "TC2: the four cases are now SECRET followed by CR, \\001, ESC and \\377 and then x, so the echo guard can fail"
  - "S1c: header wording corrected and re-wrapped. The optional LC_ALL=C change was not made"
  - "R1b: unused usings removed per Mpv file (MpvTypes.cs has none now). ': IDisposable' stays only in Mpv.cs. No logic edits"
blockers: []
