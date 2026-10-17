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
- LUC-02 handoff valid: ruach/luc-02-impl tested ab472e9 (report 55cc1df), 91 tests. LUC-05 handoff valid: ruach/luc-05-impl tested 46cbd8f (report a489591), 83 tests. Neither exercised hardware/real mpv. Worker luc-02-impl closed (wFM); worktree <worktrees>/luc-02-impl retained until INT-1 merged.
- INT-1 (integration of LUC-02 into LUC-05 branch) assigned to existing worker luc-05-impl (wFN) via docs/coordination/v1/INT-1/impl-assignment.md (placed in its worktree, uncommitted until worker commits it). Then v1-m1 is fast-forwarded to the integrated tip. Next: LUC-03 and LUC-04 (both need LUC-02; LUC-06 needs LUC-03).
- INT-1 handoff valid: combined ruach/luc-05-impl@21b3b3d (report 44abea3), 118 tests pass, merge clean. ruach/v1-m1 fast-forwarded to 44abea3. Workers luc-02/luc-05 sessions closed, worktrees removed (commits on branches luc-02-impl, luc-05-impl, v1-m1).
- LUC-03 and LUC-04 launched in parallel, base 8b535ab on ruach/v1-m1: luc-03-impl (wFP, temp <tmp>/ruach-herdr-p4iZGi), luc-04-impl (wFQ, temp <tmp>/ruach-herdr-BVveEc). Overlap risk: Player.cs (both told to keep edits minimal). Next: integrate, then LUC-06 (needs LUC-03), then LUC-07; milestone-1/2 review afterwards.
- Note: ruach-herdr linked-mode fails here (HERDR_WORKSPACE_ID maps to wrong repo); launches use `env -u HERDR_WORKSPACE_ID` (standalone).
- LUC-03 handoff valid: ruach/luc-03-impl tested d270fef (report a680255), 146 tests. LUC-04 handoff valid: ruach/luc-04-impl tested de88108 (report ef51e13), 151 tests. Both scripted/stubbed only. luc-04-impl session closed (wFQ); worktree retained until merged. Caveat from reports: Subsonic error 70 for missing album assumed from spec (check at LUC-06/12).
- INT-2 assigned to luc-03-impl (wFP): merge LUC-04, resolve Player.cs glue. Then ff v1-m1; then LUC-06.
- INT-2 handoff valid: combined ruach/luc-03-impl@9c5719a (report 219a413), 186 tests, only Player.cs conflicted. ruach/v1-m1 ff to 219a413. luc-03-impl closed (wFP); worktrees luc-03/luc-04 removed.
- COORDINATOR DECISION (scheduling/policy within D07, recorded before acting): authentication failures from Navidrome become a distinct transient-with-backoff outcome (operator can fix credentials without re-presenting the tag); unknown/empty album permanent. Supersedes INT-2 default that all non-70 rejections are permanent. Applied via LUC-06 assignment.
- LUC-08 remains blocked (no Pi). LUC-10 started software-only with substitutes per BLACKBOARD ("Independent software work may continue before physical baseline"); device parts (unplug/replug, systemd) stay pending. LUC-06 and LUC-10 launched in parallel, base 88b276c on ruach/v1-m1: luc-06-impl (wFR, temp <tmp>/ruach-herdr-i5aCJh), luc-10-impl (wFS, temp <tmp>/ruach-herdr-N69qvu). Next: integrate (INT-3), then LUC-07, then LUC-09 software (input adapter needs real event inspection -> device-blocked; implement behind abstraction only if possible), LUC-11, milestone review.
- LUC-06 handoff valid: ruach/luc-06-impl tested 6ddc828 (report 0d0a921), 230 tests; fixtures hand-written (not real Navidrome). Limit: credentials are read at startup, so correcting a wrong password needs service restart + re-presented tag (auth backoff only covers transient window of ~15 s). LUC-10 handoff valid: ruach/luc-10-impl tested eb94586 (report babac7c), 215 tests; Player loop moved to PlaySession.cs; unplug/replug, systemd, real mpv pending on device. LUC-11 unit must keep KillMode=control-group and share LUCIFER_IPC_DIR. luc-10-impl closed (wFS).
- INT-3 (merge LUC-10 into LUC-06 branch) assigned to luc-06-impl (wFR).
- INT-3 handoff valid: combined ruach/luc-06-impl@6a3efd5 (report 08baa27), 259 tests, merge clean. v1-m1 ff to 08baa27. luc-06-impl closed (wFR); worktrees luc-06/luc-10 removed.
- LUC-07 and LUC-11 (packaging/CI/service/docs part only; arch-neutral) launched in parallel, base f87aed9: luc-07-impl (wFT, temp <tmp>/ruach-herdr-bWIpXD), luc-11-impl (wFV, temp <tmp>/ruach-herdr-VsCjoC). LUC-09 follows LUC-07 (software behind input abstraction; real event mapping stays device-blocked on LUC-08).
- LUC-07 handoff valid: ruach/luc-07-impl tested 88e3099 (report d62dfe8), 279 tests (fake mpv peer; real mpv playlist semantics unverified). LUC-11 handoff valid: ruach/luc-11-impl tested 39c7dad (report 36f10fc); arm64+arm archives built here, systemd unit verify OK only with ExecStart stubbed; nothing installed on a Pi. v1-m1 ff to d62dfe8; luc-07-impl closed (wFT).
- Parallel: LUC-09 (luc-09-impl, wFW, temp <tmp>/ruach-herdr-skxxB2, base 6271a4f; no default controller mapping -- device unknown, `input inspect` command for LUC-08) and INT-4 (luc-11-impl, wFV: merge LUC-07 into LUC-11 branch). Later INT-5 merges LUC-09; note service unit needs `input` group (LUC-09 reports line; deploy/ owned by LUC-11 line).
- LUC-09 handoff valid: ruach/luc-09-impl tested c312d7a (report 740a1b9), 380 tests, fake evdev files/FIFOs + fake mpv; no default key codes (example uses KEY_* placeholders); race in pause state found/fixed. INT-4 handoff valid: ruach/luc-11-impl@1d15f7c (report 29177ae), 279 tests, archives for arm64+arm. luc-09-impl closed (wFW).
- INT-5 (merge LUC-09 + packaging glue incl. `input` group in unit) assigned to luc-11-impl (wFV). Then: independent review of combined candidate; then delivery Implementer onto ruach/lucifer-v1-orchestrator.
