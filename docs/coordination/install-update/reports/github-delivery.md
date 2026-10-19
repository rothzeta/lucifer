---
task: install-update-work-07
role: implementer
assignment: docs/coordination/install-update/assignments/github-delivery-v1.md
status: complete
outcome: Reviewed GitHub Releases installer merged locally into ruach/lucifer-install-update; no push, publication, tag, release, remote workflow, host or Pi installation; the one-line install works only after install.sh and a release are published
baseline: 1d63868131e310bb3d86396ac1317ad784e371b8
reviewed_revision: abb2f1176973a7f992cf781bee599ad8b81d284d
tested_revision: e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2
evidence_revision: ed5ab055c439a81aca9deac03363ed915ff45642
destination_before: b9535b6815ddc05b85b7aef2bde9c93ea60279cf
delivered_revision: 9951c826f4c8b4a9526d86bbfb9dfe0b36f2ae7a
target_branch: ruach/lucifer-install-update
artifacts:
  - docs/coordination/install-update/assignments/github-delivery-v1.md
  - docs/coordination/install-update/reports/github-delivery.md
  - branch ruach/lucifer-install-update
  - branch ruach/lucifer-gh-impl
verification:
  - "Own branch fast-forwarded abb2f11 to evidence pin ed5ab05 (adds only github-review-v2 assignment and report); assignment kept untracked until this commit"
  - "Preconditions: destination worktree clean, HEAD exactly b9535b6, contains baseline 1d63868; merge-base with ed5ab05 is 1d63868; git merge-tree reported no conflicts; no conflict resolution or product change was needed"
  - "git merge --no-ff ed5ab05 into the destination: delivered_revision 9951c82, parents b9535b6 and ed5ab05; destination clean afterwards"
  - "Exact-code equivalence: git diff e2ee0f4 9951c82 excluding docs/coordination is empty, so delivered product equals accepted product; the destination's only previous difference from baseline was the Coordinator task-record, unchanged by the merge"
  - "No tests re-run: the delivered non-coordination content is identical to e2ee0f4, whose verification (maint, install, bootstrap suites, both-RID packaging) is in reports/github-fixes-v2.md and was re-reviewed in reports/github-review-v2.md"
review:
  - "Independent re-review of abb2f11 accepted with no blockers (evidence ed5ab05); this task performed delivery only"
discoveries:
  - "Local-only: source pr-1, historical and remote refs untouched; nothing pushed. Commits use the neutral author and no forbidden trailers"
  - "The report and this assignment reach the target through a second evidence-only merge of ruach/lucifer-gh-impl after this report is committed; that merge changes only docs/coordination"
  - "Remote state last observed (earlier, read-only): no release or tag exists and install.sh is not on the remote default branch"
blockers: []
---

# WORK-07 local delivery

Delivered 9951c82 onto ruach/lucifer-install-update (before b9535b6). Both checkouts clean after the evidence merge.

## Usage (works only after install.sh is on the default branch and a v* release is published)
- Install: `curl -fsSL https://raw.githubusercontent.com/rothzeta/lucifer/master/install.sh | sudo bash` (specific release: `| sudo bash -s -- --version v1.2.3`)
- Update: `sudo lucifer-install update` (or `--version TAG`); rollback: `sudo lucifer-install rollback [RELEASE]`; offline archives still work.

## Limits
Remote publication, the release workflow run, real downloads and physical Pi acceptance are unverified.
