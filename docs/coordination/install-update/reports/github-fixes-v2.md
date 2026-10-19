---
task: install-update-work-05-fix-v2
role: implementer
assignment: docs/coordination/install-update/assignments/github-fix-v2.md
status: complete
outcome: B1 (truncated pipe) and B2 (HTTPS redirect downgrade) fixed with regressions that fail on the original faults; optional sudo-env docs and checkout persist-credentials done; physical acceptance, remote Actions and a live release download unverified
baseline: 1d63868131e310bb3d86396ac1317ad784e371b8
source_revision: 0a7fe5fd6d8e52910cf7b15e5e164b47fe2edb4d
candidate_revision: e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2
tested_revision: e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2
artifacts:
  - install.sh
  - deploy/lucifer-install
  - deploy/test-lucifer-bootstrap.sh
  - .github/workflows/release.yml
  - docs/install/INSTALL.md
  - docs/coordination/install-update/assignments/github-fix-v2.md
  - docs/coordination/install-update/reports/github-fixes-v2.md
  - branch ruach/lucifer-gh-impl
verification:
  - "git merge --ff-only 0a7fe5f into ruach/lucifer-gh-impl: fast-forward, no conflicts; candidate is source plus one code commit"
  - "bash -n install.sh deploy/lucifer-install deploy/publish.sh deploy/lucifer-maint deploy/test-*.sh: exit 0; YAML parse of ci.yml and release.yml OK"
  - "deploy/test-lucifer-maint.sh: lucifer-maint ok; deploy/test-lucifer-install.sh: lucifer-install ok (0 FAIL); deploy/test-lucifer-bootstrap.sh: lucifer-bootstrap ok (0 FAIL), about 17 s"
  - "B1 mutation (install.sh restored to 5846e54): suite fails, 'cut at N changed the host' for many cut points; restored afterwards"
  - "B2 mutation (--proto/--proto-redir allow http in both scripts): 7 FAIL lines across bootstrap and backend downgrade regressions; restored afterwards"
  - "deploy/publish.sh --rid linux-arm64 and linux-arm --version v0.0.0-fix: exit 0; sha256sum -c OK; packaged bin/lucifer-install byte-identical to deploy/lucifer-install in both"
  - "Application build/408 tests not re-run: git diff 0a7fe5f..HEAD touches no Amp.Nfc or Amp.Nfc.Tests file, so baseline evidence for the unchanged app applies"
  - "No existing assertion weakened: one bootstrap assertion regex now allows indentation (grep '^ *get lucifer-install$') because the call moved inside main(); same invariant"
review: not-run
discoveries:
  - "B1 disposition: all of install.sh now runs inside main(), invoked on the last line as main \"$#\" \"$@\" \"$END_MARK\". main refuses (exit 1, nothing done) unless the last argument is the marker and the leading count equals the number of user arguments, so a cut anywhere defines nothing, hits a syntax error, or reaches main without the complete argument list. The regression pipes 1398 cut points (every 5th byte, every byte of the last 160, every line boundary) with --version v1.0.0 and asserts no host effects, empty temp dir, no HTTP request, and nonzero exit for any cut inside the final invocation; a complete script missing only its final newline installs the requested tag; complete stdin run and --help still work"
  - "B2 disposition: curl uses --proto and --proto-redir '=https' in install.sh and lucifer-install fetch(); '=https,http' only with LUCIFER_INSTALL_TEST_INSECURE=1. Regressions use localhost TLS servers (temporary self-signed certificate, trusted via CURL_CA_BUNDLE) and a plain-http server: HTTPS direct and HTTPS->HTTPS redirects install; TLS->HTTP redirect on the bootstrap download, backend API metadata and backend archive download are each refused with exit 1, no host effects, no temp left, and the http target is never requested"
  - "Optional findings handled: INSTALL.md and install.sh header now show 'sudo env LUCIFER_REPO=owner/name ...' for install and updates; release.yml checkout has persist-credentials: false. Remaining optional findings (latest-consistency between installer and API lookup, full archive download before a refusal or no-op, permission scoping to the release step, update-after-rollback note, grep-only workflow tests) are left as documented limits"
blockers: []
---

# WORK-05 fix v2

Source 0a7fe5f (fast-forward), candidate and tested revision e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2. No push, publication, tag, delivery or cleanup. Fixtures and logs live in temporary directories; tracked paths neutral; no trailers.

## Residual limitations
- The partial-script guard covers a cut of the piped text; a complete script whose later network fetches fail is handled by the existing failure paths. A user who types arguments equal to the end marker is not a supported case.
- Remote Actions never ran; actionlint absent; no real GitHub release exists or was downloaded; TLS behaviour is shown against local servers only; physical Pi acceptance unverified.
- Unauthenticated API rate limits and the checksum-only integrity model are unchanged and documented.
