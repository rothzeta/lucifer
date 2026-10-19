---
task: install-update-work-06-v2
role: reviewer
assignment: docs/coordination/install-update/assignments/github-review-v2.md
status: complete
outcome: "Accepted for local delivery only: B1 and B2 are fixed and independently verified, no blocking findings remain, optional items are recorded. Live one-liner availability, remote Actions and physical acceptance remain unverified"
baseline: 1d63868131e310bb3d86396ac1317ad784e371b8
source_revision: 0a7fe5fd6d8e52910cf7b15e5e164b47fe2edb4d
reviewed_revision: abb2f1176973a7f992cf781bee599ad8b81d284d
tested_revision: e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2
artifacts:
  - docs/coordination/install-update/reports/github-review-v2.md
  - docs/coordination/install-update/assignments/github-review-v2.md
  - docs/coordination/install-update/reports/github-review-v1.md
  - branch ruach/lucifer-gh-review
verification:
  - "git merge --ff-only abb2f11 on ruach/lucifer-gh-review: fast-forward from 0a7fe5f; HEAD abb2f1176973a7f992cf781bee599ad8b81d284d; delta 0a7fe5f..abb2f11 = install.sh, deploy/lucifer-install, deploy/test-lucifer-bootstrap.sh, release.yml, INSTALL.md + fix assignment/report"
  - "bash -n install.sh deploy/lucifer-install deploy/publish.sh deploy/lucifer-maint deploy/test-*.sh: exit 0; YAML safe_load ci.yml and release.yml: ok"
  - "deploy/test-lucifer-maint.sh: exit 0 lucifer-maint ok; deploy/test-lucifer-install.sh: exit 0 lucifer-install ok 0 FAIL; deploy/test-lucifer-bootstrap.sh: exit 0 lucifer-bootstrap ok 0 FAIL (truncated pipes 1398 cut points)"
  - "Independent every-byte sweep (all 5651 prefixes of install.sh piped with --version v1.0.0 --start into an isolated root against the fake server): 0 host changes, 0 temp leftovers, 0 HTTP requests from any truncated prefix; the only requests came from the full-length run"
  - "v1 P1 re-run (cut after 'main \"$#\" \"$@\"'): exit 1 'the script was cut off or is incomplete; nothing was done', no host effects; cut inside '\"$END_MARK' gives a syntax error (exit 2)"
  - "Complete script: no args installs latest (v1.1.0) exit 0; --version v1.0.0 installs v1.0.0; missing only the final newline installs v1.0.0; --help exit 0; user args 'x __LUCIFER_BOOTSTRAP_COMPLETE__' exit 2 unknown argument (no marker spoofing past parsing)"
  - "v1 P6 re-run (TLS endpoint trusted by CURL_CA_BUNDLE, 302 to plain http, test hook unset): bootstrap exit 1 curl 'Protocol http not supported or disabled', backend API exit 1, no host effects, 0 requests reached the http server; backend initial http URL exit 2, ftp URL exit 2"
  - "curl call sites: install.sh get() (installer + checksum) and lucifer-install fetch() (API metadata, archive, archive checksum) are the only ones; all use --proto/--proto-redir '=https' unless LUCIFER_INSTALL_TEST_INSECURE=1"
  - "Independent mutations in scratch copies from git archive HEAD (worktree untouched): install.sh reverted to 0a7fe5f -> bootstrap suite exit 1, 7 FAIL (truncation and network checks); backend proto default '=https,http' -> exit 1, 4 FAIL (backend API/archive downgrade checks)"
  - "deploy/publish.sh --rid linux-arm64 and --rid linux-arm --version v0.0.0-rv2: exit 0 both; sha256sum -c OK both; packaged bin/lucifer-install byte-identical to deploy/lucifer-install in both"
  - "Application build/408 tests not re-run: 1d63868..abb2f11 touches no application or application test file, so baseline evidence applies unchanged"
  - "actionlint/shellcheck not installed; release.yml read semantically; no remote Actions run, no tag or release created, no host install"
review:
  - "Independent re-review of 0a7fe5f..abb2f11 in the context of 1d63868..abb2f11: no blocking findings; accepted for local delivery only"
discoveries:
  - "B1 verified: the whole bootstrap is in main(), called on the last line with the argument count and an end marker. Every prefix either defines nothing, hits a syntax error, or is refused by main"
  - "B2 verified: both scripts allow only HTTPS for the first URL and for every redirect in production. HTTPS-to-HTTPS redirects still work (the suite covers this, both direct and through a redirector)"
  - "New tests assert observable effects (host tree, stub calls, temp dir, request log on the http server, current link); the one changed old assertion only allows indentation for the same get calls; no old acceptance weakened"
  - "sudo env documentation (INSTALL.md section 0, install.sh header, lucifer-install header) is now correct: 'sudo env LUCIFER_REPO=owner/name bash' and the same for later updates"
  - "release.yml checkout now has persist-credentials: false. gh release create still authenticates via GH_TOKEN and reads the repository from the origin remote, which keeps its URL"
blockers: []
---

# WORK-06 v2 independent re-review

Reviewed revision abb2f11; the product commit tested is e2ee0f4. This covers the fix delta since 0a7fe5f, read together with the v1 scope. All runs used isolated temporary roots, stubs, local servers and scratch copies, with bounded timeouts. Nothing was published or installed, and no product file was edited.

## Verdict
No blocking findings remain. I accept abb2f11 for local delivery to ruach/lucifer-install-update only.

## Blocker dispositions
- **B1 fixed.** `install.sh` now does all of its work inside `main()`, called on the last line as `main "$#" "$@" "$END_MARK"`.
  - My every-byte sweep (5651 prefixes) produced no host changes, no leftover temp files and no network requests from any truncated prefix.
  - A cut inside the final call is either refused by `main` (exit 1) or fails as a syntax error (exit 2).
  - Complete scripts, including one missing only the final newline, still install the requested tag, and `--help` still works.
- **B2 fixed.** Every fetch boundary uses `--proto`/`--proto-redir =https` unless the explicit test setting is on.
  - Redirects to plain http are refused, and the http server receives no requests.
  - Initial http and ftp URLs are refused with exit 2.
  - HTTPS-to-HTTPS redirects still install (covered by the suite).

## Optional (not blocking)
1. **Silent no-op on some cuts.** A cut that lands before the final line exits 0 without doing anything and without a message from `install.sh`, because no command has run yet (1776 of 5651 prefixes). The user still sees curl's own transfer error, since `-S` is set, and the host is unchanged. A shell cannot report this case itself, so I'm recording it, not asking for a fix.
2. **Carried over from v1 as documented limits:**
   - The installer comes from latest/download while the backend looks up "latest" through the API, so a release published in between can mix versions.
   - The full archive is downloaded before an install refusal or an update with nothing new.
   - Write permission covers the whole job rather than only the release step.
   - The INSTALL.md note about update after rollback is not added.
   - The workflow checks in the suite are grep-only.
3. `gh release create` would be slightly more robust with `GH_REPO: ${{ github.repository }}` than relying on the checkout's remote. The current form should work; it is not verified remotely.

## Unchanged safety and preservation
I re-ran the full bootstrap and installer suites at the tested revision. They cover:
- the root check before any download;
- the inactive default and no restart loop;
- the userland RID;
- failures before host changes for missing assets, rate limits, truncated downloads, checksum and RID mismatches, and unsafe archive members;
- the no-archive update keeping config, catalogue and earlier releases;
- restoration after a failed update, named rollback, and offline archive mode.

All pass.

## Not verified
Live GitHub download (no release exists), the remote release workflow, actionlint/shellcheck, real TLS to GitHub (only local servers with a temporary certificate), physical Pi acceptance.
