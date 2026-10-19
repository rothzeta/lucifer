---
task: install-update-work-04-delivery
role: implementer
assignment: docs/coordination/install-update/assignments/delivery-v1.md
status: complete
outcome: Reviewed install and update implementation delivered as a local merge on ruach/lucifer-install-update; product identical to the accepted product; no push, host installation or cleanup
reviewed_revision: 8699215e3fb9093bb79e522fb62ac35e1a95fe99
delivered_revision: a64ab733aedecabbd96f800ead09ea23a4c2dca1
destination_before: a6d602bf161531622c24a4748c0d92002fcb9d08
destination: ruach/lucifer-install-update
evidence_revision: 2795aea6e8b05de799ae18d56e044ca3a053652e
tested_revision: 7fe6ddfbbf7a9165dcbe2e6c7a938daf19ab5d74
artifacts:
  - docs/coordination/install-update/reports/delivery.md
  - docs/coordination/install-update/assignments/delivery-v1.md
  - branch ruach/lucifer-install-update
  - branch ruach/lucifer-iu-impl
verification:
  - "Destination before delivery: branch ruach/lucifer-install-update at a6d602bf161531622c24a4748c0d92002fcb9d08, working tree clean (git status empty), as expected"
  - "Source: ruach/lucifer-iu-impl fast-forwarded to 2795aea6e8b05de799ae18d56e044ca3a053652e; reviewed 8699215 is its ancestor; the source and destination share merge base ac9e415"
  - "git merge-tree --write-tree a6d602b 2795aea: no conflicts; git merge --no-ff of 2795aea into the destination completed with no conflict and no edits"
  - "Product equivalence: git diff 7fe6ddf a64ab73 over deploy, docs/install, README.md, .github, Amp.Nfc and Amp.Nfc.Tests is empty, and no file outside docs/coordination differs from the accepted product 7fe6ddf; the delivery merge adds only the destination's own task-record difference relative to the source"
  - "Exact-code verification reused, not rerun: WORK-02 verification-v4 passed on product 7fe6ddf and WORK-03 review-v2 accepted 8699215 with no blockers; no host installer or broad tests were run"
  - "No push, no remote ref merge, no real installation, no cleanup; the stale pr-1 checkout was not touched"
discoveries:
  - "The destination already held coordination records (task-record) absent from the source, so delivery is a merge commit with first parent a6d602b and second parent 2795aea"
  - "Merge author and committer are the neutral Ruach Implementer identity; no co-author trailer"
blockers: []
---

# Delivery report (WORK-04)

Delivered locally to `ruach/lucifer-install-update` in the retained checkout `.worktrees/lucifer-install-update`: merge commit `a64ab733aedecabbd96f800ead09ea23a4c2dca1` (parents `a6d602b` and `2795aea`). The destination was unchanged and clean beforehand. The merge introduced no conflicts and no product edits.

## Usage from the delivered docs

- Build: `deploy/publish.sh --rid linux-arm64 --version 1.0.0` (or `--rid linux-arm`); output `dist/lucifer-<version>-<rid>.tar.gz` plus `.sha256`.
- First install: `sudo deploy/lucifer-install install lucifer-1.0.0-linux-arm64.tar.gz`, or from an unpacked archive `sudo lucifer-1.0.0-linux-arm64/bin/lucifer-install install lucifer-1.0.0-linux-arm64.tar.gz`. Then edit `/etc/lucifer/lucifer.env`, run `sudo -u lucifer /opt/lucifer/current/bin/lucifer-maint check`, and `sudo systemctl enable --now lucifer`.
- Later: `sudo lucifer-install update <archive>`, `sudo lucifer-install list`, `sudo lucifer-install rollback [--adopt] [RELEASE]`.

## Limits

Physical acceptance on a Pi and real systemd, accounts and audio remain unverified; the isolated suite uses stubs. The checksum detects corruption only. A hard kill is recovered by the documented steps, not automatically. Nothing was pushed or installed. This report is a recording commit after the delivered revision.
