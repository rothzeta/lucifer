# WORK-01 amendment v2: fix independent verification findings

Implementer scope/restrictions from implement-v1.md continue; this amendment adds bounded fixes. Owner DEC-003 requires preservation and tested failure behavior, so Coordinator disposition is FIX all F1-F4, not accept as limitations. First verification/fix cycle (default max two unsuccessful cycles).

Dependency: WORK-02 report is committed at `8e87033400e116a3a1371f9599f5412d3698ab37`, tested `e2790b6`. Fast-forward your own branch to this evidence revision first; source is unchanged. Read reports/verification.md, preserve it and prior implementation report unchanged. Own deploy code/tests/docs plus new report `docs/coordination/install-update/reports/fixes-v2.md`. Task record/owner assignment/other worker artifacts protected. Commit this amendment unchanged with report.

Fix and add observable regression evidence for:
- F1: explicit rollback must protect operator-edited unit/tmpfiles as update does. Refuse unchanged by default; deliberate adoption must preserve originals and be documented/help-aligned. Don't silently lose custom support files.
- F2: catchable-signal failure must restore usable prior state AND allow practical retry without an unexplained leftover release collision. Define/document uncatchable kill/power-loss limits; do not claim crash atomicity unsupported by tests.
- F3: install --start must validate safe usable config before enabling/starting, including CRLF placeholder, whitespace/missing password and check failure. Do not source env or print credentials. Preserve explicit service intent and safe default instructions.
- F4: malformed package (including overlong components) must be rejected/staged before host account/directory/support changes. Eliminate writable-source checksum-to-extraction gap: all privileged consumption must use the same verified private package snapshot/stage. Choose the smallest sound implementation; use Architect escalation if design is blocked. Checksum remains corruption evidence, not publisher identity.

Run isolated deployment/maintenance suites, syntax and new regression cases. Confirm combined integration source revisions/conflicts and exact candidate/tested_revision. Application code unchanged: reuse earlier build/408-test evidence with explanation unless packaging changes warrant rerun. Republish/check both RID entry points/help if script/package changes. No real host install/accounts/systemd/Pi. Neutral paths and author, no private identities or forbidden trailers; no delivery/cleanup.

Write new ruach-handoff report fixes-v2.md with exact checks/results, F1-F4 dispositions and residual limits. Run canonical handoff validator with --repo, commit unchanged amendment/report after product revision, return both SHAs. Independent reverification and review follow; no acceptance may be prefilled.
