task: FIX-3
status: complete
outcome: "All assigned findings are fixed except N1 (skipped as instructed) and the NuGet lock file (skipped, see below). Build 0 warnings/0 errors, 401 tests pass 3 times, lucifer-maint test passes, systemd-analyze verify is clean. One residual: the unchanged assignment file itself still contains the path strings it asks to remove, so the final grep is empty except for that file."
candidate_revision: 6da0d628964ad12c75da4e8c62f911f7ccbeb766
tested_revision: 6da0d628964ad12c75da4e8c62f911f7ccbeb766
artifacts:
  - docs/coordination/v1/FIX-3/impl-report.md
  - branch ruach/fix-3 (base ecb5f13, 6 commits, not pushed)
verification:
  - "dotnet build: 0 warnings, 0 errors"
  - "dotnet test, run 3 times at the candidate revision: Passed 401, Failed 0, Skipped 0 each time (399 earlier + 2 new IPC tests; the Cf case extends an existing test)"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok'; new refusal cases for CR, control, ESC, invalid UTF-8; tab inside a value accepted; mid-value quote case a\"b\"c restored"
  - "systemd-analyze verify with ExecStart stubbed to a temporary script: no output, exit 0"
  - "Mpv split check: sorted-line comparison of old Mpv.cs against the new files differs only by added usings, namespace, partial class headers and file comments"
  - "git grep -I -i for the username, the mail provider word, home paths, <dev>, ruach temp paths over the tree: only docs/coordination/v1/FIX-3/impl-assignment.md matches (committed unchanged as required)"
  - "Not run: device checks, publish.sh, shellcheck, the CI workflow itself"
review: []
discoveries:
  - "The assignment file FIX-3/impl-assignment.md must stay unchanged but contains the strings the final grep checks for; the coordinator should scrub it in the same pass as any history rewrite before pushing this branch."
  - "A first sed turned the old baseline link text 'master 38c01fe->f49202a' into 'f49202a->f49202a'; fixed in the follow-up commit (now '<old-sha>->f49202a')."
  - "Remaining old SHAs in task-record and DELIVERY-3/CODE-REVIEW assignments were replaced with <old-sha>. Links in docs/reviews and BLACKBOARD now point at f49202a (same tree de2e9e9, verified), so paths and #L anchors still match."
  - "actions/checkout@v4 and actions/setup-dotnet@v4 are lightweight tags resolving to commits 11d5960 and 67a3573 (full SHAs in ci.yml); the trailing comment says v4 because that is the tag that was resolved."
  - "Unchanged by design: the repository owner name in GitHub URLs is not the local username and was left."
blockers: []
dispositions:
  - "P3 fixed: local username, home, checkout, worktree, ruach and temp paths replaced by placeholders in docs, BLACKBOARD and reviews; 12+ links to the old commit repointed to f49202a; remaining pre-rewrite SHAs neutralised. Exception: the assignment file."
  - "S8 fixed: LogText.Clean also replaces Unicode Format characters; test extended with U+202E and U+200B."
  - "R6 fixed: Subsonic client name is 'lucifer' (no test or doc referenced the old name)."
  - "R7 fixed: duplicate blank-line arm removed from lucifer-maint."
  - "T1 fixed: unreachable stop guards removed from the mpv-lost and input-lost checks in PlaySession; the FatalReason guard is kept because the stop can be requested during the controller step; test comment now states the real exit path."
  - "S1b fixed: control characters other than tab and invalid UTF-8 refused (exit 2, value not printed), with tests."
  - "S3b fixed: PrivateTmp rationale corrected in lucifer.service and INSTALL.md."
  - "C1b fixed: plain CreateDirectory then link and mode checks; IOException mapped to the Unsafe message; tests for dangling symlink and file at the path."
  - "Y3 fixed: single Prepare call (Cli); removed from PlaySession; tests unchanged and passing."
  - "TC1 fixed: mid-value quote case restored."
  - "S5 fixed: both actions pinned to full commit SHAs with tag comment."
  - "S5 NuGet lock file skipped: it needs RestorePackagesWithLockFile in the project and a --locked-mode restore in CI, a build behaviour change."
  - "R1 fixed: Mpv.cs split into partial class files Mpv.cs, Mpv.Launch.cs, Mpv.Transport.cs, Mpv.Ipc.cs, Mpv.Status.cs and MpvTypes.cs; code moved only."
  - "N1 skipped as instructed."
