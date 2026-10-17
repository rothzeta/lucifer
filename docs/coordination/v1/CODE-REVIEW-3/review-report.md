task: CODE-REVIEW-3
role: reviewer
status: complete
outcome: "Whole-repository review of origin/master 0d8d60d, with history scans of the three origin branches (orchestrator tip fb0c9eb). The code is unchanged since CODE-REVIEW-2 (a446916): build has 0 warnings, 399/399 tests pass and the lucifer-maint test passes. 2 BLOCKING findings, both non-public information. P3: the username and local paths were added back to published history after the rewrite (ce70825, c71bfbc and later). P4: GitHub still serves the pre-rewrite commits by SHA (personal email in 38c01fe and e83d774, home paths in the 2f8e4f5 tree), and published docs link to them. No code findings are blocking. 6 earlier optional findings remain open and 4 new optional ones were found."
reviewed_revision: 0d8d60d
artifacts:
  - docs/coordination/v1/CODE-REVIEW-3/review-report.md
  - docs/coordination/v1/CODE-REVIEW-3/review-assignment.md
verification:
  - "dotnet build (local DOTNET_ROOT): Build succeeded, 0 warnings, 0 errors"
  - "dotnet test: Passed 399, Failed 0, Skipped 0"
  - "deploy/test-lucifer-maint.sh: 'lucifer-maint: ok' (exit 0)"
  - "git diff a446916 0d8d60d outside docs/: empty. Code, deploy and CI files are byte-identical to the CODE-REVIEW-2 revision; the earlier findings were re-checked against the current files"
  - "History scan: every blob reachable from origin/master (98 commits), origin/docs/lucifer-v1-completion (2) and origin/ruach/lucifer-v1-orchestrator (99), 270 unique blobs, checked with regexes for emails, IPv4, home, /opt and /tmp paths, the username, tokens (ghp_, xox, AKIA, sk-, bearer), user:pass@ URLs, password=, .local/.lan hosts, long hex and 22-char IDs. Commit author/committer identities and all message bodies were also checked"
  - "Identities: every commit on the three branches uses a GitHub noreply address (99 lowercase, 1 capitalised author name). No other email appears in any message. Emails in blobs: git@github.com (the remote URL), two GitHub noreply forms, and a test fake on example.test"
  - "GitHub (read-only): git ls-remote origin; gh pr view 1; GET on repos/<owner>/lucifer/commits/<sha> for the old tips; GET on the contents of task-record.md at ref 2f8e4f5"
  - "Not run: device or hardware checks, publish.sh, shellcheck, systemd-analyze (unit unchanged since CODE-REVIEW-2, which ran it)"
review:
  - "BLOCKING P3 (medium, non-public info) docs/coordination/v1/task-record.md:93 (origin/master 0d8d60d and the orchestrator) and :99 (orchestrator fb0c9eb), docs/coordination/v1/DELIVERY-4/impl-assignment.md:2 (master ce70825 and later), docs/coordination/v1/CODE-REVIEW-3/review-assignment.md:5 (orchestrator fb0c9eb): the local username and machine layout are back in published history. These lines hold the full <home>/.dotnet path, the main checkout path <dev>/lucifer and one <tmp>/ruach-herdr-<id> path. They were written after the rewrite, in the commits from c71bfbc onward. Evidence: the history scan above; 'git log --find-object' places the blobs at ce70825 on master and fb0c9eb on the orchestrator. Fix: replace them with placeholders (<home>, <repo>, <tmp>). Because the history is already published, the commits since the rewrite (c71bfbc..0d8d60d on master, and up to fb0c9eb on the orchestrator, 4 to 5 docs-only commits) need another rewrite and force-push, which needs owner authorization. Coordinators should write assignments with placeholders from now on, since the identifier came in through the coordinator's own assignment text."
  - "BLOCKING P4 (medium, non-public info, partly needs an owner decision) docs/reviews/2026-10-17-v1-review.md:5,42,50,58,66,74,82,90,98,106,114,122 (12 clickable github.com links to commit 38c01fe), BLACKBOARD.md:29, docs/coordination/v1/task-record.md:83-85,93, docs/coordination/v1/DELIVERY-3/impl-assignment.md:11, docs/coordination/v1/CODE-REVIEW/review-assignment.md:2, docs/coordination/v1/README-1/impl-report.md:25: GitHub still serves the pre-rewrite objects by SHA. GET commits/38c01fe and commits/e83d774 return author and committer = <personal-email>. The contents of task-record.md at ref 2f8e4f5 return 12 lines with home or <dev> paths. The published docs link straight to 38c01fe, so a visitor needs one click. Fix (in the repo): point the links at f49202a, which has the same tree (de2e9e9, checked), so the paths and #L anchors stay valid. Drop or neutralise the full old SHAs in BLACKBOARD.md and docs/reviews, and as far as practical in docs/coordination. Fix (outside the repo, owner): ask GitHub Support to remove the cached views and dangling objects for 38c01fe, e83d774 and 2f8e4f5. Without that, only deleting and recreating the repository removes them. The PR #1 timeline also shows a head_ref_force_pushed event, which normally links the old head SHA."
  - "OPTIONAL N1 (low, non-public info / docs) catalogue.example.json:5,10, Amp.Nfc.Tests/CatalogueTests.cs:185-186, README.md:246: the two navidromeId values are 22-character IDs carried over from the IDs hard-coded in the original Program.cs (f49202a). They look like real IDs from the owner's server. README.md:246 calls the file 'placeholders', but INSTALL.md:55 calls them 'migration examples'. They are not credentials and are useless without the server and an account, but they reveal two albums in a private library. Fix: replace them with obvious fakes (for example 'REPLACE-navidrome-album-id-1') and make README and INSTALL agree. Keeping them is acceptable if the owner chooses to."
  - "OPTIONAL T1 (low, tests/simplicity), still open, re-checked: Amp.Nfc/PlaySession.cs:107,110. The '&& !stop.IsCancellationRequested' guards cannot be reached once stop is set: the while condition at :105 exits first, and Task.Delay at :118 throws OperationCanceledException. Still matters only as dead code and a misleading test claim. Fix: as CODE-REVIEW-2 suggested (drop the guards, or say in the test that it pins the loop order)."
  - "OPTIONAL S1b (low, correctness), still open: deploy/lucifer-maint:37-42. The case pattern refuses backslash, whitespace and quotes only. A CR inside a value and invalid UTF-8 are still exported, though systemd reads them differently. Still matters only for a malformed env file. Fix: as before (refuse [[:cntrl:]] other than tab, and invalid UTF-8)."
  - "OPTIONAL S3b (low, docs), still open: deploy/lucifer.service:40-41 still gives 'lucifer-maint shares the IPC directory and lock' as the reason for omitting PrivateTmp. /run/lucifer is not affected by PrivateTmp. Fix: as before."
  - "OPTIONAL C1b (low, readability), still open: Amp.Nfc/IpcDirectoryPolicy.cs:37-41. The Exists check plus the 'when (Directory.Exists)' catch is still dead for a race, and a dangling symlink or a file at the path still surfaces a raw IOException instead of the Unsafe() message. Fix: as before."
  - "OPTIONAL Y3 (low, simplicity), still open: Amp.Nfc/Cli.cs:113 and Amp.Nfc/PlaySession.cs:73 both call IpcDirectoryPolicy.Prepare on every play. Fix: keep one."
  - "OPTIONAL TC1 (low, tests), still open: deploy/test-lucifer-maint.sh:26. The literal-value case still has no mid-value quote. Fix: use 'a\"b\"c' inside the password value."
  - "OPTIONAL S5 / R1 (deliberately skipped), still open: .github/workflows/ci.yml:17-18 still uses @v4 tags. Amp.Nfc/Mpv.cs is 812 lines. S5 still matters a little for supply chain: the workflow has permissions contents:read, so the exposure of a tag hijack is limited to CI output, but a hijacked action can still read the checkout. R1 is a readability cost only."
  - "OPTIONAL S8 (low, security, new) Amp.Nfc/LogText.cs:10: Clean replaces char.IsControl (Cc) only. Unicode format characters (Cf: U+202E RIGHT-TO-LEFT OVERRIDE, U+200B, U+2066-2069) in server track titles or tag text reach the journal unchanged and can make a line display misleadingly. Fix: also replace when char.GetUnicodeCategory(c) == UnicodeCategory.Format, and add a test case."
  - "OPTIONAL R6 (low, readability, new) Amp.Nfc/Navidrome.cs:38,157: the Subsonic client name is still 'amp-v0'. The project and package are Lucifer, and R2 already removed the amp-nfc name everywhere else, so it shows in Navidrome's player list under the old name. Fix: use 'lucifer' (behaviour only changes the client label in Navidrome)."
  - "OPTIONAL R7 (low, readability, new) deploy/lucifer-maint:27-29: blank lines are skipped twice (the regex at :27 and the '' arm at :29). Fix: drop the '' arm, or the regex."
discoveries:
  - "Remote refs (git ls-remote origin): HEAD and master = 0d8d60d; docs/lucifer-v1-completion = 93c59d2; ruach/lucifer-v1-orchestrator = fb0c9eb (4 docs-only commits beyond master); refs/pull/1/head = 93c59d2 (the rewritten commit). There is no refs/pull/1/merge any more. PR #1 is MERGED, its head is docs/lucifer-v1-completion, and its only commit 93c59d2 has a noreply author. The task-record note that refs/pull/1 still holds the old objects is out of date for the ref itself, but see P4 for the SHA-addressable objects."
  - "Rewrite mapping, confirmed by tree equality: 38c01fe -> f49202a (tree de2e9e9) and e83d774 -> 93c59d2."
  - "Local refs pr-1, master and most ruach/* branches in the main checkout still contain the old history (for example 38c01fe). They must never be pushed. This branch (ruach/code-review-3) carries the assignment unchanged, as required, and that assignment holds the P3 path at line 5. It is the same blob already on origin's orchestrator, but do not push this branch until P3 is fixed."
  - "Scan negatives: no IPv4 addresses, tokens, API keys, authenticated URLs other than test fakes (u:topsecretpw@, user:hunter2@ on example.test), hard-coded passwords (the first commit reads NAVIDROME_* from the environment), hostnames other than the navidrome.local placeholder, or device serials. The USB IDs in README are generic CH340 vendor/product IDs."
  - "Earlier findings checked and closed in the current code: S1, S2 (--no-config, --ytdl=no), S3 (hardening), S6 (LogText.Clean), S7 (the lock file is never deleted), Y1, Y2, I1 (getters under _gate), R2-R5, K1, C1. Defaults agree across Options.cs, CommandLine help, README, INSTALL and lucifer.env.example."
blockers:
  - "P3 and P4 need owner decisions: authorize another narrow history rewrite and force-push (P3), and contact GitHub Support or accept that the old SHAs stay visible (P4). The in-repo part of P4 (re-pointing the links) can be done with an ordinary commit, but it is pointless if P3 is fixed first by a rewrite that then has to be repeated, so do both in one pass."

# CODE-REVIEW-3 report

Reviewed: the repository at `0d8d60d` (origin/master). History scan covered origin/master, origin/docs/lucifer-v1-completion and origin/ruach/lucifer-v1-orchestrator (`fb0c9eb`). Personal identifiers are written here as placeholders: `<home>`, `<dev>`, `<tmp>`, `<personal-email>`.

**Blocking findings remain: P3 and P4.** Both are non-public information. The code itself has no blocking issue.

## Non-public information

| Item | Where | Visible on GitHub |
| --- | --- | --- |
| Username and home path | `task-record.md:93,99`, `DELIVERY-4/impl-assignment.md:2`, `CODE-REVIEW-3/review-assignment.md:5` | yes, in current branch tips (P3) |
| Main checkout path, herdr temp dir | `task-record.md:93` (master), `:99` (orchestrator) | yes (P3) |
| Personal email | commit metadata of pre-rewrite `38c01fe`, `e83d774` | yes, by SHA; linked from `docs/reviews/…` (P4) |
| Home and dev paths | tree of pre-rewrite `2f8e4f5` (`task-record.md` and others) | yes, by SHA; named in coordination docs (P4) |
| Navidrome album IDs | `catalogue.example.json`, a test | yes (N1, optional) |
| Commit identities on the current branches | all noreply | n/a |

How P3 happened: after the rewrite the coordinator wrote the real `DOTNET_ROOT` path into the DELIVERY-4 and CODE-REVIEW-3 assignments, and real paths into `task-record.md`. The scrub fixed the past but nothing prevents a repeat. A pre-push grep for the username and home path in the coordinator's checklist would catch it.

## Dimensions

- **Simplicity:** Y3 and R7 (optional). No new over-engineering. The code is the same as at CODE-REVIEW-2.
- **Readability:** C1b, R1 (skipped) and R6 (optional). `Mpv.cs` is still the one large file.
- **Immutability:** no open findings. Options use `private init`, results are records, and the shared state in `SelectionController` and `ReaderWatch` is read under `_gate`.
- **Configurability:** no new findings. All defaults agree across code, help text, README, INSTALL and the env example. The `alsa/default:CARD=Pro` default is documented as unverified.
- **Security:** S1b, S3b, S5 (skipped) and S8 (optional). Credentials stay out of messages: `UrlRedaction` covers the mpv and selection paths, `ToString` is overridden, and userinfo in the URL is refused.
- **Tests:** T1 and TC1 (optional). 399 tests pass and the build has no warnings.
