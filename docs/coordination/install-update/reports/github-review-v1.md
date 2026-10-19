---
task: install-update-work-06
role: reviewer
assignment: docs/coordination/install-update/assignments/github-review-v1.md
status: complete
outcome: "Changes requested: 2 blocking findings (truncated piped install.sh silently drops options and installs; the HTTPS-only rule is bypassed by redirects to http) and 6 optional ones. Local delivery is not accepted until the blockers are fixed and re-reviewed. Physical acceptance unverified"
baseline: 1d63868131e310bb3d86396ac1317ad784e371b8
reviewed_revision: 5846e540b0ded7551e2374ebed26056b94b3ee71
tested_revision: 5846e540b0ded7551e2374ebed26056b94b3ee71
artifacts:
  - docs/coordination/install-update/reports/github-review-v1.md
  - docs/coordination/install-update/assignments/github-review-v1.md
  - branch ruach/lucifer-gh-review
verification:
  - "bash -n install.sh deploy/lucifer-install deploy/publish.sh deploy/lucifer-maint deploy/test-*.sh: all exit 0"
  - "deploy/test-lucifer-maint.sh: exit 0, lucifer-maint ok"
  - "deploy/test-lucifer-install.sh: exit 0, lucifer-install ok"
  - "deploy/test-lucifer-bootstrap.sh: exit 0, lucifer-bootstrap ok, 0 FAIL lines"
  - "Probe P1 (truncated stdin stream, install.sh cut right after '\"$work/lucifer-install\" install', run with --version v1.0.0): exit 0, installed lucifer-v1.1.0 (latest) instead of v1.0.0"
  - "Probe P6 (local HTTPS endpoint, trusted through CURL_CA_BUNDLE, answering 302 to plain http; LUCIFER_INSTALL_TEST_INSECURE unset): install.sh and lucifer-install followed the redirects; exit 0, release installed"
  - "Probe P2 (latest metadata tag_name '../../x' and 'v1.1.0\\\"; x'): exit 1, 'unsupported release tag', no host effects"
  - "Probe P3 (install.sh rerun on an installed host): exit 3 'use lucifer-install update', host tree hash unchanged (full archive downloaded first)"
  - "Probe P4 (update after rollback, latest release already in releases/): exit 3 'already present ... use rollback' (existing semantics)"
  - "Probe P5 (non-root no-archive update): exit 1 'must run as root' before any download"
  - "Probe P7 (superseded test-lucifer-install assertion): the no-archive install really reaches the download (curl connection refused), not an earlier failure"
  - "deploy/publish.sh --rid linux-arm64 --version v0.0.0-rv: exit 0; sha256sum -c OK; packaged bin/lucifer-install byte-identical to deploy/lucifer-install; that real archive installed through piped install.sh --version v0.0.0-rv from the local fake release: exit 0"
  - "Read-only unauthenticated GET api.github.com/repos/cli/cli/releases/latest: 200, pretty-printed JSON with '  \"tag_name\": ' and '      \"name\": \"<asset>\"' lines that the text parser expects; github.com/.../releases/latest/download/<asset> answers 302 to releases/download/<tag>/<asset>"
  - "git diff --name-only 1d63868..5846e54: no application/test project files, so baseline dotnet build/408-test evidence is reused unchanged"
  - "actionlint and shellcheck not installed (not installed globally by this review); release.yml checked by reading it; no remote Actions run, no tag, no release"
review:
  - "Independent review of 1d63868..5846e54: changes requested (2 blockers)"
discoveries:
  - "No real release exists, so a live end-to-end GitHub download was not possible; all download behaviour is from a local fake server, stubs and one read-only API format check on another public repository"
  - "Trust boundary judgment: fetching lucifer-install plus its release checksum, running it, and letting it validate the separate archive before host changes is acceptable for a curl bootstrap. It does not duplicate the transaction engine, and the installer is the same file as bin/lucifer-install in the package. The checksum only detects corruption, as documented"
  - "Tests observe effects (current link, unit text, config/catalogue hashes, host tree hash, stub call log, temp dir empty), not function internals. The workflow section is grep-only and cannot show runtime correctness"
blockers:
  - "B1 install.sh runs its top-level code as it is streamed, so a truncated curl|bash download can run with options dropped (fix: wrap the body in a function that is called on the last line)"
  - "B2 the --proto-redir '=https,http' option in install.sh get() and lucifer-install fetch() lets a redirect go to plain http in production (fix: '=https' unless LUCIFER_INSTALL_TEST_INSECURE=1)"
---

# WORK-06 GitHub Releases independent review v1

Reviewed revision 5846e54 (product fe008d1) against baseline 1d63868. Scope: the whole diff, i.e. install.sh, deploy/lucifer-install download path, deploy/test-lucifer-bootstrap.sh, the two superseded test-lucifer-install.sh assertions, release.yml, ci.yml, README and INSTALL.md. Probes ran from the session scratch directory against a copy of the bootstrap suite's harness (temporary root, stubbed host commands, a 127.0.0.1 fake GitHub). Nothing ran privileged on the host, and nothing was pushed or published.

## Blocking findings

### B1. A truncated piped install.sh runs with options dropped
- Location: `install.sh` (whole file, top-level statements; the final line is `"$work/lucifer-install" install ${args[@]+"${args[@]}"} </dev/null || rc=$?`).
- Problem: with `curl ... | sudo bash`, bash runs each statement as soon as it arrives. If the stream is cut partway, bash runs whatever it has received, and the curl error goes unnoticed because bash is at the end of the pipe. Cut just after `install`, the installer runs without the user's arguments.
- Impact: a host change the user did not ask for. `--version v1.0.0` turns into "latest", and `--target-rid` is silently dropped. The run still exits 0. This goes against the acceptance condition "truncation failures before host changes" for the piped entry point.
- Evidence: probe P1 cut the script at that point and piped it with `--version v1.0.0`. Result: rc=0, current=lucifer-v1.1.0-linux-arm64.
- Direction: put the body in `main() { ...; }` and make `main "$@"` the last line, so a partial stream defines nothing and runs nothing. Add a bootstrap test that pipes a truncated copy and expects no host effects.

### B2. Redirects can downgrade to plain http in production
- Location: `install.sh` get(), `deploy/lucifer-install` fetch() (`--proto '=https,http' --proto-redir '=https,http'`); check_https and the GH_DL check only look at the first URL.
- Problem: without the test hook, only the starting URL has to be https. Any redirect may go to `http://`. Docs and help text say downloads are "over HTTPS" and that non-HTTPS URLs are refused.
- Impact: the HTTPS guarantee does not hold across redirects. Every real asset download redirects (github.com to the asset host), so this is the main path, not an edge case. Today it needs a misbehaving or compromised origin, or an overridden endpoint, so this is a hardening gap rather than a known exploit. It is still an explicit acceptance item ("HTTPS, redirects").
- Evidence: probe P6. A local https server (trusted through CURL_CA_BUNDLE) answered 302 to `http://127.0.0.1:<port>/...`. With LUCIFER_INSTALL_TEST_INSECURE unset, both scripts followed it and the install completed (rc=0).
- Direction: use `--proto =https --proto-redir =https` by default, and allow http only when LUCIFER_INSTALL_TEST_INSECURE=1. Add a regression test for an https-to-http redirect.

## Optional findings
1. Latest consistency (`install.sh` latest/download vs the API call in `download_release`): the installer comes from `releases/latest/download`, but the backend looks up "latest" again through the API. If a release is published in between, the installer from release N installs release N+1. Low impact because the engine is the same. Direction: accept and document it, or resolve the tag once (for example from the `latest/download` redirect) and pass `--version`.
2. Docs (`INSTALL.md` section 0, install.sh header): `LUCIFER_REPO=owner/name` is dropped by sudo's env_reset in `curl | sudo bash`. It has to go after sudo (`sudo LUCIFER_REPO=... bash`), and later `lucifer-install update` calls need it again. Direction: correct the documented form.
3. Efficiency (`cmd_install`, `cmd_update`): a rerun of install on an installed host (P3) and a no-op update both download the full archive before refusing or reporting "up to date". Direction: check for `current` / compare `lucifer-<tag>-<rid>` before the archive download.
4. UX (`cmd_update` after rollback, P4): plain `update` exits 3 when the latest release is already in releases/. This is existing semantics and the message is clear. Optionally mention it in INSTALL.md section 8.
5. release.yml hardening (not blocking):
   - `contents: write` applies to the whole job, and checkout keeps its credentials, so the test step runs with a write-capable token in `.git/config` (the code comes from the tag itself). Consider `persist-credentials: false` and keeping write only for the release step.
   - A rerun for a tag that already has a release fails at `gh release create`. This is acceptable, but the docs could say so.
   - I believe `gh release create` with files keeps the release as a draft until all uploads finish. This was not run.
   - `fetch-depth: 0` is unnecessary.
   - Semantics I checked and found correct:
     - TAG goes through `env` and is quoted, so there is no shell interpolation of the input.
     - The tag regex runs before checkout of `refs/tags/$TAG`.
     - The workflow regex is a subset of the backend and publish.sh regexes.
     - `-` marks a prerelease, which GitHub's "latest" excludes.
     - Asset globs match exactly what publish.sh writes (`lucifer-<tag>-<rid>.tar.gz(.sha256)`).
     - `sha256sum lucifer-install` output matches install.sh's regex.
     - The installer asset is `deploy/lucifer-install`, identical to `bin/lucifer-install` in the package.
6. Tests: the workflow contract in the bootstrap suite only greps the YAML. It checks names, not workflow behaviour; actionlint would add a little.

## Areas checked without findings
- Piped help/options/root check, default inactive service and placeholder config, no restart loop.
- Userland RID from dpkg with an aarch64 `uname` stub.
- Tag regexes on input and on metadata (P2).
- Text-only metadata parsing, which matches real GitHub formatting.
- No eval/source of downloaded data and no secret printing.
- Private 0700 temp dirs, removed on every failure path covered.
- Failures before host changes: 404, 403/429/500, missing assets, malformed or mismatched checksums, truncated archive, RID mismatch, traversal member.
- Root check before any download (P5).
- No-archive update keeps config, catalogue and the previous release, keeps the inactive/active state, restores on config-check and start failures, and is a no-op when current.
- Named rollback; the offline ARCHIVE path is unchanged.
- The two superseded assertions now really reach the download (P7). No other old assertion was weakened.

## Not verified
Remote Actions, a real GitHub release download, actionlint/shellcheck, armhf package build in this review (the arm64 build was rebuilt; the implementer's report covers both), physical Pi acceptance.
