task: CODE-REVIEW-4
role: reviewer
status: complete
outcome: "Review of ruach/fix-3 (code 6da0d62, report d71cf45) against CODE-REVIEW-3 and the FIX-3 report. 11 of the 12 claimed fixes are real and complete. The R1 split moves code without edits (checked mechanically), and both S5 SHAs match their tags. Build has 0 warnings, 401/401 tests pass 3 times, the lucifer-maint test passes and systemd-analyze verify is clean. 1 BLOCKING finding: the C1b change maps every IOException to 'is not a directory. Remove it'. That includes a read-only /tmp, which is the exact failure the new lucifer.service comment describes, and the message names a path where nothing exists (reproduced). The private-info residue in the tree is only in the expected assignment and FIX-3 files. 6 OPTIONAL findings."
reviewed_revision: 6da0d628964ad12c75da4e8c62f911f7ccbeb766
artifacts:
  - docs/coordination/v1/CODE-REVIEW-4/review-report.md
  - docs/coordination/v1/CODE-REVIEW-4/review-assignment.md
verification:
  - "dotnet build --no-incremental (DOTNET_ROOT=<home>/.dotnet): 0 Warning(s), 0 Error(s)"
  - "dotnet test, 3 runs: Passed 401, Failed 0, Skipped 0 each run"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok', exit 0"
  - "systemd-analyze verify (systemd 257) on lucifer.service with ExecStart replaced by a stub script in scratch: no output, exit 0"
  - "R1: sorted-line diff of the old Mpv.cs (orchestrator) against the concatenation of Mpv.cs, Mpv.Launch.cs, Mpv.Transport.cs, Mpv.Ipc.cs, Mpv.Status.cs and MpvTypes.cs (fix-3). The only extra lines are 5x4 using lines, 5 namespace lines, 4 file comments, 4 '{' and 4 '}', 6 blank lines, and 'public sealed class Mpv : IDisposable' turned into 5x 'public sealed partial class Mpv : IDisposable'. The diff is whitespace-sensitive, so indentation is unchanged and no type changed nesting. _gate/lock lines: 15 old, 15 new. All instance fields and their initialisers stay in Mpv.cs; the one static field (PreviousRestartAfter) has no dependencies, so the order of initialisation across partial files does not matter. Commit 6da0d62 touches only Amp.Nfc/Mpv*.cs. The test diff over the whole range has only the S8, C1b and T1 changes"
  - "S5 (read-only gh api): actions/checkout git/ref/tags/v4 is a commit 11d5960a326750d5838078e36cf38b85af677262, the same as v4.4.0. actions/setup-dotnet git/ref/tags/v4 is a commit 67a3573c9a986a3f9c594539f4ab511d57bb3ce9, the same as v4.3.1. Both pinned SHAs exist as commits"
  - "S1b parity, checked empirically with systemd-run --user -p EnvironmentFile on systemd 257. CR inside a value: systemd truncates it to 'SE' (differs, so refusing is right). \\001 and ESC: systemd passes them through unchanged. Invalid UTF-8 (\\377): systemd refuses the file ('Failed to load environment files: Invalid argument', the unit does not start). Tab: kept. U+0085: kept. lucifer-maint refuses CR, \\001, ESC and \\377, and accepts tab. It refuses U+0085 under a UTF-8 locale and accepts it under LC_ALL=C"
  - "C1b: reproduced with unshare -rm and a read-only bind mount as XDG_RUNTIME_DIR, LUCIFER_IPC_DIR unset, running 'Amp.Nfc tag read'. Output: 'Error: Default IPC directory <scratch>/rt/lucifer is not a directory. Remove it or set LUCIFER_IPC_DIR...', exit 1, and nothing exists at that path"
  - "Private-info scan: git grep -I -i over the fix-3 tree and HEAD for the local username, home path, <home-root>/, the mail provider word, <dev>, <tmp>/ruach-herdr, the host name and the old SHAs 38c01fe/e83d774/2f8e4f5, plus an email regex. Results are in the report body. Commit metadata: one identity on all branches, a noreply address"
  - "Not run: device or hardware checks, publish.sh, shellcheck, the CI workflow itself"
review:
  - "BLOCKING C1c (low severity, regression from the C1b fix) Amp.Nfc/IpcDirectoryPolicy.cs:41-45: 'catch (IOException)' turns every IOException from CreateDirectory into Unsafe(path, 'is not a directory') and drops the cause. On Linux .NET maps EROFS, ENOSPC, EIO and others to a plain IOException, so a read-only /tmp or runtime directory reports 'Default IPC directory X is not a directory. Remove it ...' when nothing exists at X. That is the documented case in deploy/lucifer.service:40-43 and INSTALL.md:61 (an unset LUCIFER_IPC_DIR under ProtectSystem=strict 'fails loudly'): it fails loudly, but with the wrong reason. Before FIX-3 the raw 'Read-only file system' IOException surfaced. Evidence: the unshare reproduction under verification. Fix: map the exception only when something is at the path (File.Exists(path) or new FileInfo(path).LinkTarget is not null), otherwise rethrow or pass the IOException as the inner exception with its message. Add one test that injects or simulates a non-EEXIST failure, or at least asserts that the message for a missing path is not 'Remove it'"
  - "OPTIONAL S8b (low, security) Amp.Nfc/LogText.cs:12: the check runs per UTF-16 unit, and char.GetUnicodeCategory returns Surrogate for each half of a pair, so supplementary-plane format characters pass through (TAG characters U+E0001, U+E0020-E007F; U+1D173-1D17A). U+2028/U+2029 (Zl/Zp) also pass, and some viewers render them as line breaks. Fix: decide per Rune (or CharUnicodeInfo.GetUnicodeCategory(string, int)) and also replace LineSeparator and ParagraphSeparator. Add a test with \\U000E0041 and \\u2028"
  - "OPTIONAL TC2 (low, tests) deploy/test-lucifer-maint.sh:46,55-58: the 'value echoed' guard matches *SECRET*, but the four new cases split the word ('SE\\rCRET', 'SE\\001CRET', ...), so an echoed value would never match and the guard cannot fail for them. Fix: keep a contiguous marker, for example 'A=SECRET\\001x' and 'A=SECRET\\rx', or match *CRET*"
  - "OPTIONAL S1c (low, docs/consistency) deploy/lucifer-maint:10-18,38: the header says refused lines are ones 'systemd would read differently', but systemd passes \\001 and ESC through unchanged (verified). Those are refused for safety, not for parity. [[:cntrl:]] depends on the locale: under a UTF-8 locale it also refuses C1 characters such as U+0085, which systemd accepts, and under LC_ALL=C it does not. Both ways fail closed, so behaviour is acceptable. Line 18 is also about 120 characters in a block wrapped at 78. Fix: say 'or is unsafe (control characters other than tab)', re-wrap line 18, and optionally set LC_ALL=C for the check so the result does not depend on the caller's locale"
  - "OPTIONAL R1b (low, readability) Amp.Nfc/MpvTypes.cs:1-4, Amp.Nfc/Mpv.Transport.cs:1-4, Amp.Nfc/Mpv.Status.cs:1-4 and the other split files: all 4 usings were copied into every file, including MpvTypes.cs, which uses none of them. ': IDisposable' is repeated on every partial declaration. This is deliberate for a pure move, and the build is warning-free. Fix (separate commit, after merge): remove the unused usings per file, and keep ': IDisposable' and the class doc comment only in Mpv.cs"
  - "OPTIONAL S5b (low, maintainability) .github/workflows/ci.yml:17-18: the '# v4' comment names a moving major tag, so a later reader cannot tell which release was pinned. Fix: '# v4.4.0' and '# v4.3.1', the exact tags that resolve to these SHAs today"
  - "OPTIONAL P5 (low, non-public info, owner awareness) commit identity on every branch, plus docs/coordination/v1/task-record.md:91 and docs/coordination/v1/DELIVERY-3/impl-assignment.md:6: the noreply address has the form <owner>+<host>@users.noreply.github.com, and the plus tag is this machine's host name. It is a noreply address, so it is not residue under the assignment's rule, and it is already on origin/master. It does reveal the host name. Fix: owner decision; if it is unwanted, use the plain <id>+<owner>@users.noreply.github.com form for future commits and include it in the planned history rewrite"
discoveries:
  - "Private-info residue in the fix-3 tree is limited to docs/coordination/v1/FIX-3/impl-assignment.md:4,9 (<dev>, <tmp>/ruach-herdr-<id> and the old SHAs, quoted as strings to remove) and docs/coordination/v1/FIX-3/impl-report.md:15 (the word <dev>) and :20 (old SHA 38c01fe in the sed note). HEAD adds docs/coordination/v1/CODE-REVIEW-4/review-assignment.md (the <home>/.dotnet path and <dev>/ruach). This is all the expected assignment and FIX-3 residue. No residue in code, README, deploy, CI or install docs. Emails other than noreply: only git@github.com and the test fake on music.example.test. The <tmp>/ruach-herdr-<random> placeholders in task-record.md hold random suffixes only"
  - "P3 tree part: fixed. The old SHAs 38c01fe, e83d774 and 2f8e4f5 no longer appear outside FIX-3 files. BLACKBOARD.md (1) and docs/reviews/2026-10-17-v1-review.md (12) point at f49202a, whose tree is de2e9e9 (checked), so anchors stay valid. The external part of P4 (GitHub serving the old SHAs) is still an owner action"
  - "systemd 257 logs the full value of an invalid-UTF-8 env line to the journal ('invalid UTF-8 value for key A: ...'). A malformed password in /etc/lucifer/lucifer.env therefore leaks into the journal when the service starts, whatever lucifer-maint does. Possible INSTALL note: run 'lucifer-maint check' after editing the env file and before restarting the service"
  - "T1: the removed '&& !stop' guards could fire in principle, because stop is set asynchronously by a signal between the while check and the Lost check. That window is a few instructions both with and without the guards, so removing them changes nothing in practice. The remaining FatalReason guard is justified: a stop requested during controller.StepAsync can end that step in a way ReaderWatch counts, and without the guard a graceful stop would become exit 1. The new test comment matches the path the test exercises"
  - "Y3: PlaySession.RunCoreAsync is reached only through Player.RunAsync from Cli.cs:119, after Prepare at Cli.cs:113, so the single call covers every path"
blockers: []

# CODE-REVIEW-4 report

Reviewed: `ruach/fix-3`. The code revision is `6da0d62`, the report is `d71cf45`, and the base is the orchestrator (`4c6c69a`, merge base `ecb5f13`). Personal identifiers are written as `<home>`, `<owner>`, `<host>` and `<scratch>`.

**Blocking findings remain: C1c**, a regression from the C1b fix with a small fix. All the other fixes are accepted.

## Disposition of the FIX-3 claims

| Finding | Verdict | Note |
| --- | --- | --- |
| P3 | fixed (tree) | Residue only in the FIX-3 and assignment files. The history rewrite and the GitHub-side part of P4 are still owner actions |
| S8 | fixed, gap | BMP Cf characters are replaced. Astral Cf and Zl/Zp are not (S8b) |
| R6 | fixed | `c=lucifer` at both call sites. No `amp-v0` is left outside the history docs |
| R7 | fixed | Blank lines are skipped once, by the regex |
| T1 | fixed | The FatalReason guard is justified (see discoveries) |
| S1b | fixed | Matches systemd for CR and invalid UTF-8. \001 and ESC are refused although systemd accepts them, which fails closed (S1c) |
| S3b | fixed | The rationale is correct: ProtectSystem=strict makes /tmp read-only |
| C1b | fixed, **regression** | All IOExceptions are mislabelled (C1c) |
| Y3 | fixed | One `Prepare`, in `Cli.cs:113` |
| TC1 | fixed | `a"b"c` is inside the password value |
| S5 | fixed | Both SHAs equal their tags. The comment could name the exact release (S5b) |
| R1 | fixed | The code was moved, not edited (mechanical check) |
| N1, NuGet lock | skipped | As instructed |

## Dimensions

- **Simplicity:** no new complexity. R1b (copied usings) is cosmetic.
- **Readability:** the split files are well scoped. See R1b and the long header line in S1c.
- **Immutability:** unchanged. All mutable Mpv state stays in `Mpv.cs` under `_gate`.
- **Configurability:** unchanged. The client name is now a fixed `lucifer`, as intended.
- **Security:** see C1c (a misleading diagnostic, not an exposure), S8b and P5. The pinned actions are correct.
- **Tests:** 401 tests pass, including 2 new IPC tests. See TC2.
