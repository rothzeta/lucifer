# WORK-01 amendment v3: independent review fixes

Implementer role/scope/restrictions from implement-v1 and fix-v2 continue. This is first review-stage fix cycle; prior verification findings resolved. Owner preservation and intended service-state requirements bind. Read `reports/review-v1.md` after fast-forwarding own branch to `c11d21f403959e8a0ef977a81617e2b2b9cfb60a` (all independent verification/review evidence, no product change since c96dc4a). Preserve all prior reports/assignments; no task-record edits.

Fix B1: record and restore enable state plus active/restarting state across failed install --start, including start failure and failed settle after enable. Undo only changes made by transaction; preserve previously enabled/active state. Stop transaction-started work before removing unit/release; don't leave dangling boot enable or misleading output. Add regression asserting state after failure AND after subsequent default install, including originally disabled and enabled state. Keep config/secrets protected, no source/print.

Also address within owner scope:
- O2: preserve complete previously activated releases even after crash+manual rollback sequence. Fix marker lifecycle safely; independent regression must prove old releases stay available without breaking retry for genuinely incomplete releases.
- O3 and N4: document practical uncatchable-crash recovery (including correct unit/adoption/same-version retry and service-state restoration), and the short settle check's limits. Do not claim crash-atomic restoration.
- N1: update/rollback with no installation should refuse before creating host dirs; re-check state under lock for races. Strengthen test beyond only /etc.
- N2: assess whether reapplying restored tmpfiles support is required for supported package changes; fix with a meaningful test or explain deliberate limitation. Avoid unrelated N3 cleanup.

Owned files deploy scripts/tests and install docs/README as necessary, plus unchanged amendment and new `reports/review-fixes-v3.md`. No production/test edits outside scope, no weakened application or release acceptance, neutral paths/authors and no forbidden trailers. Never real host accounts/units/install/Pi. Never approve native dialogs; use guarded cleanup expressions rather than insufficient variable deletion paths.

Verify syntax, existing maint and full isolated installer suite, focused regressions and effect/order assertions. Republish/check entry points/help for both RIDs if script changes, using temp output. Application code unchanged: justify reuse of clean build/408-test evidence unless required new changes. Confirm exact combined candidate/tested_revision, sources/conflicts. No delivery until separate post-review assignment; no cleanup.

Write ruach-handoff report review-fixes-v3.md with B1/O2/O3/N1/N2/N4 disposition, exact checks/results and remaining limitations; run canonical validator with --repo. Commit unchanged amendment and report after product commit, return report-creating SHA and tested product SHA. Reverification and independent re-review follow; do not prefill acceptance.
