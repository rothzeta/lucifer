# WORK-02 amendment v2: execute on pinned combined revision

This adds the missing execution pin to verify-v1.md; all scope, restrictions, report path and unchanged-assignment commit requirements continue. Received preparation is complete; execute now.

WORK-01 is complete. Candidate: `e2790b6fb295818d2de604760b0ef1a94a825dc8` on `ruach/lucifer-iu-impl`; tested product revision `697b072c9161dc8ccb7256f89996c8ae30b07872`. The pin adds only implementation assignment/report to product revision. Fast-forward your own verification branch to this exact candidate (`git merge --ff-only <pin>`) without editing source, tests or existing reports. Preserve your uncommitted verify-v1.md and this amendment. No owner-target integration or delivery.

Read `docs/coordination/install-update/reports/implementation.md` at the pin, then run combined verification and your independent probes. Report exact tested_revision, commands/results and concrete blockers. Do not treat the existing passing report as acceptance. Focus failure ordering and recovery, unsafe archives, secret/config preservation, prior support file restoration, inactive service, existing installations, placeholder defaults, and real package contents/help. Reuse exact-code build/package evidence only with an explicit rationale, otherwise run relevant checks. No real installation or physical acceptance.

Write and mechanically validate reports/verification.md as in v1. Commit both assignments unchanged and your report with neutral identity, no forbidden trailers. Return report-creating commit and outcome. Wait for Coordinator; cleanup remains mine.
