task: CODE-REVIEW-5
role: reviewer
status: complete
outcome: "Focused re-review of ruach/fix-4 (code 4d48fce, report da62ae0) against the CODE-REVIEW-4 findings. All 6 targeted fixes are real and complete. C1c (the blocking finding) is fixed: a read-only runtime directory and a read-only /tmp now report the system's reason and the LUCIFER_IPC_DIR hint, with no 'Remove it' (reproduced). Build has 0 warnings, 408/408 tests pass 3 times, the lucifer-maint test passes and systemd-analyze verify is clean. The new tests fail on the fix-3 code. No new problems of any weight and no personal identifiers in code, README, deploy or CI files. No BLOCKING findings remain. 2 OPTIONAL cosmetic findings."
reviewed_revision: 4d48fce5607d0efd574bcaa59da5e8c95d6a23e6
artifacts:
  - docs/coordination/v1/CODE-REVIEW-5/review-report.md
  - docs/coordination/v1/CODE-REVIEW-5/review-assignment.md
verification:
  - "dotnet build --no-incremental (DOTNET_ROOT=<home>/.dotnet): 0 Warning(s), 0 Error(s)"
  - "dotnet test --no-build, 3 runs: Passed 408, Failed 0, Skipped 0 each run"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok', exit 0"
  - "systemd-analyze verify (systemd 257) on lucifer.service with ExecStart replaced by a stub script in scratch: no output, exit 0"
  - "C1c, runtime directory: unshare -rm, a read-only bind mount as XDG_RUNTIME_DIR, LUCIFER_IPC_DIR unset, 'Amp.Nfc tag read'. Output: 'Error: Default IPC directory <scratch>/ro/lucifer cannot be created: Read-only file system : '<scratch>/ro/lucifer'. Set LUCIFER_IPC_DIR to a directory you own.', exit 1"
  - "C1c, /tmp fallback: unshare -rm with /tmp remounted read-only, XDG_RUNTIME_DIR and LUCIFER_IPC_DIR unset. Output: 'Error: Default IPC directory /tmp/lucifer-root cannot be created: Read-only file system : '/tmp/lucifer-root'. Set LUCIFER_IPC_DIR ...', exit 1 (root is the mapped user inside the namespace)"
  - "Red-first: the fix-3 tree with the fix-4 IpcDirectoryTests.cs and LogTextTests.cs, filtered to those classes: 3 of 17 fail. These are LogText U+E0041 (kept), LogText U+2028/U+2029 (kept) and Prepare_reports_the_real_cause (DirectoryNotFoundException instead of InvalidOperationException). The other 14 pass"
  - "TC2 mutation: a copy of lucifer-maint changed to print '$key=$value' in the control-character refusal. The fix-4 test fails with 'FAIL: value echoed: carriage-return-inside' and exit 1. The fix-3 test passes it ('lucifer-maint: ok')"
  - "R1b: in f228c6f the only changed lines that are not usings are 4x 'public sealed partial class Mpv : IDisposable' turned into 'public sealed partial class Mpv'. Mpv.cs keeps ': IDisposable' and the class doc comment. The commit touches only the 5 split files. No logic edits"
  - "S5b (read-only gh api): actions/checkout git/ref/tags/v4.4.0 is a commit 11d5960a326750d5838078e36cf38b85af677262, and actions/setup-dotnet git/ref/tags/v4.3.1 is a commit 67a3573c9a986a3f9c594539f4ab511d57bb3ce9. Both equal the pinned SHAs, and the comments now name those tags"
  - "S1c: the lucifer-maint header lines 9-20 are 71-79 characters. The fix-3 header already had 79-character lines, and the 122-character line is gone"
  - "Unused-using check: a scratch copy of fix-4 with 'using System.Text;' removed from Amp.Nfc/Mpv.cs builds with 0 warnings and 0 errors"
  - "Private-info scan: added diff lines grepped for the local username, <home-root>/, the dev root, ruach-herdr, the mail provider word, the host name and an email regex. The only hit is docs/coordination/v1/FIX-4/impl-assignment.md (the <dev>/ruach validator path, expected assignment residue). git grep over the fix-4 tree excluding docs: no hits. The commit identity in the range is the noreply address already noted as P5"
  - "Not run: device or hardware checks, publish.sh, shellcheck, the CI workflow itself, ENOSPC/EIO injection"
review:
  - "OPTIONAL C1d (low, readability) Amp.Nfc/IpcDirectoryPolicy.cs:49-50: on Linux the IOException message already ends with the path (\"Read-only file system : '<path>'\"), so the new message names the path twice and has an odd ' : ' in the middle. The user still gets the right cause and the hint, so this is cosmetic. Fix if wanted: keep only the reason part of the message, or start the message with 'Cannot create the default IPC directory:' and append ex.Message, which already holds the path"
  - "OPTIONAL R1c (low, readability) Amp.Nfc/Mpv.cs:3 still has 'using System.Text;', which nothing in Mpv.cs uses (removing it builds clean). R1b asked for unused usings to be removed per file. Amp.Nfc/MpvTypes.cs now starts with a blank line, and Amp.Nfc/Mpv.Launch.cs:1-2 lists System.Net.Sockets before System.Diagnostics, so the usings are out of order. Fix: remove the using, the leading blank line, and sort the two usings"
discoveries:
  - "The new C1c test covers a slightly different case from the review: the runtime directory's parent is a file, so the IOException (DirectoryNotFoundException, ENOTDIR) comes from creating the parent. That only reaches the new branch because FIX-4 moved the parent creation into the try. It still pins the C1c rule, because putting the old catch body back with the parent inside the try would map it to 'is not a directory' and fail the test. The real EROFS path is covered by the two unshare reproductions above, not by a unit test"
  - "UnauthorizedAccessException (EACCES, for example a runtime directory that is not writable) is still raised raw, without the LUCIFER_IPC_DIR hint. This was already so before FIX-3, and the error names the path. It is not a regression"
  - "LogText: a lone surrogate becomes U+FFFD (category OtherSymbol) and is kept. This is harmless and the FIX-4 report states it"
  - "Personal-identifier residue is still limited to the coordination assignment files (FIX-4/impl-assignment.md now has the <dev> validator path, as earlier ones did). P5 (the host name in the noreply commit address) stays an owner decision"
blockers: []

# CODE-REVIEW-5 report

Reviewed: `ruach/fix-4`. The code revision is `4d48fce`, the report is `da62ae0`, and the range is `ruach/fix-3..ruach/fix-4`. Personal identifiers are written as `<home>`, `<dev>` and `<scratch>`.

**No blocking findings remain.** C1c is fixed and verified. The two optional findings are cosmetic.

## Disposition of the CODE-REVIEW-4 findings

| Finding | Verdict | Evidence |
| --- | --- | --- |
| C1c (blocking) | fixed | Something at the path gives 'symbolic link' or 'not a directory'. Otherwise the message gives the system reason, the hint and the IOException as inner exception. Reproduced for a read-only runtime dir and a read-only /tmp. The new test fails on fix-3 |
| S8b | fixed | Per-rune check. Cf, Zl and Zp are replaced, astral tag characters too. The tests fail on fix-3 |
| TC2 | fixed | Contiguous `SECRET` marker. An echoing mutant is now caught, but was not caught before |
| S1c | fixed | 'or that is unsafe … refused for safety' wording. Re-wrapped. LC_ALL=C was not adopted (optional) |
| R1b | fixed, small gap | Pure move with no logic edits. One unused using is left in Mpv.cs (R1c) |
| S5b | fixed | `# v4.4.0` and `# v4.3.1` resolve to the pinned SHAs |
| P5, N1 | skipped | As instructed |

## Dimensions

- **Correctness:** the C1c branch order is right. A dangling symlink is checked before File.Exists, which follows links. Every other IOException keeps its cause.
- **Simplicity and readability:** the changes are small and well scoped. See C1d and R1c.
- **Security:** the S8b gap is closed and no identifiers were added to code, deploy or CI files.
- **Tests:** there are 408 tests, 7 of them new, and the new ones fail on the old behaviour. The shell guard can now fail.
