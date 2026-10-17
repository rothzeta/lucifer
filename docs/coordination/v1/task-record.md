# Lucifer v1 task record

Workflow: ruach-workflow-feature. Coordinator: orchestrator session in this checkout.
Destination: `ruach/lucifer-v1-orchestrator` (local only; no push, no PR merge, no live Pi/Navidrome changes).
Retained checkout: `<worktrees>/lucifer-v1-orchestrator`. `<repo>` stays on `pr-1`.
Resources: `<ruach>`; catalogs `<ruach>/config/agent-routing`. Escalation: 2 failed fix/review cycles.

## Authority and decisions (recorded before dependent actions)

| ID | Source | Exact words | Scope |
| --- | --- | --- | --- |
| AUTH-01 | Owner, launch instruction (docs/coordination/v1/assignment.md) | "launch a ruach orchestrator to  implement up to v1" | Specialist launches, local implementation, tests, local commits, integration and delivery onto `ruach/lucifer-v1-orchestrator`. Excludes remote push, PR merge, live Pi/Navidrome changes. |
| AUTH-02 | Owner, folder-trust prompt | "Approve trust and start" | Claude Code folder trust for this orchestrator only. |
| AUTH-03 | Assignment, delivery | "Deliver reviewed changes through an Implementer onto `ruach/lucifer-v1-orchestrator`" | Delivery target authorized, subject to review passing. |

Proposed v1 decisions D01–D11 in BLACKBOARD.md are used as written; none changed.

## Environment (observed)
dotnet SDK 10.0.110 at <home>/.dotnet/dotnet; no mpv; no /dev/ttyUSB0; /dev/snd has only seq/timer. No Pi access: LUC-08 is device-blocked.

## Units

| Unit | Status | Branch / revision | Notes |
| --- | --- | --- | --- |
| LUC-08 | blocked (no Pi access, hostname unknown; not to be inferred) | - | Needs owner: Pi host/SSH access or operator-run inventory |
| LUC-01 | assigning | - | first; base 3f09c09 |
| LUC-02..07, 09..12 | todo | - | per BLACKBOARD order |

## Next action
Launch LUC-01 implementer; then independent review; deliver; proceed LUC-02.

## Log
- LUC-01: worker luc-01-impl launched (workspace wFK, worktree <worktrees>/luc-01-impl, branch ruach/luc-01-impl, base ad2f93e, temp dir <tmp>/ruach-herdr-aMeBMp). First attempt in linked mode failed with `worktree_uncertain` before creating anything (HERDR_WORKSPACE_ID resolved to unrelated repo tehom-brainlab); verified nothing existed; relaunched standalone with HERDR_WORKSPACE_ID unset. Assignment: docs/coordination/v1/LUC-01/impl-1-assignment.md. Awaiting report docs/coordination/v1/LUC-01/impl-1-report.md.
- LUC-01 DONE-PENDING-REVIEW: report valid; candidate ruach/luc-01-impl@74159f4 (report at 8b5e17d); 56 tests pass; no hardware. Worker luc-01-impl closed (wFK), worktree removed, temp dir removed. Decision (Coordinator scheduling, not owner decision): chain tasks on integration line `ruach/v1-m1` (worktree <worktrees>/v1-m1, from 8b5e17d) and run independent review per milestone on the combined revision rather than per task; delivery to orchestrator branch only after milestone review.
- LUC-02 and LUC-05 launched in parallel (base 34786e4 on ruach/v1-m1): luc-02-impl (wFM, temp <tmp>/ruach-herdr-SruBjL), luc-05-impl (wFN, temp <tmp>/ruach-herdr-T35zLf). Reports expected at docs/coordination/v1/LUC-0{2,5}/impl-1-report.md.
- LUC-01 proposal from report: service unit and maintenance runs must share LUCIFER_IPC_DIR for reader lock (carry to LUC-10/11).
