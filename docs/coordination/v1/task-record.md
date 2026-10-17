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
- INT-5 handoff valid: SINGLE v1 SOFTWARE CANDIDATE ruach/luc-11-impl@d177fe6 (report b978e9f): 380 tests, build clean, arm64+arm archives + checksums, unit verify OK with ExecStart stubbed, check with example catalogue/mapping OK. No device evidence.
- REVIEW-1: independent Reviewer v1-review-1 (route claude-opus-5.5-medium preferred; wFX, temp <tmp>/ruach-herdr-wfztKp, worktree <worktrees>/v1-review-1, branch ruach/v1-review-1) assigned candidate d177fe6 via docs/coordination/v1/REVIEW-1/review-assignment.md (placed uncommitted in its worktree). Implementer luc-11-impl (wFV) kept for possible fixes.
- REVIEW-1 report valid (ruach/v1-review-1@5be4eb0): candidate d177fe6 NOT accepted. Reviewer reran build + 380 tests twice (green, no flakiness). 1 blocking (F1: mpv child inherits stdout/stderr -> authenticated Subsonic stream URLs reach journal), 7 optional (F2 KillMode vs ordinary stop exit 1; F3 usermod lacks input; F4 stale README; F5 Next/Prev ignored while Loading vs README claim; F6 lucifer-maint shell-sourcing of env file; F7 help/README omissions and shutdown range > TimeoutStopSec; F8 lock keyed by path string + tautological assertions). Reviewer session closed (wFX), worktree v1-review-1 retained (holds report on branch ruach/v1-review-1; branch kept).
- FIX-1 (fix cycle 1 of max 2) assigned to luc-11-impl: F1 blocking + all optionals F2-F8. After it: combined verification then re-review (new reviewer assignment on the new revision, limited to F1-F8 fixes and changed behaviour/tests).
- FIX-1 handoff valid: ruach/luc-11-impl@4bcca29 (report 5dcd4de): F1-F8 claimed fixed, 390 tests, 0 build errors, maint env test, archives, unit verify. Caveats: F2 clean-stop test passes without the guard too (weak); real mpv output behaviour with --no-terminal unverified (LUC-08 must check journalctl). Fix cycle 1/2 used.
- REVIEW-2: fresh Reviewer v1-review-2 (wFY, worktree <worktrees>/v1-review-2, branch ruach/v1-review-2, temp see launch) assigned candidate 4bcca29 (focused re-review of F1-F8 + changed code).
- REVIEW-2 report valid (ruach/v1-review-2@8860cf0): candidate 4bcca29 -- NO BLOCKING FINDINGS. Build 0 errors/2 warnings, 390 tests x2, maint test, arm64 package+checksum verified, KillMode=mixed behaviour confirmed on systemd 257 scratch unit. Optional follow-ups (not blocking, not done): N1 lucifer-maint env parsing differs from systemd EnvironmentFile (backslash/whitespace/quotes/comments); N2 docs wording for KillMode=mixed SIGKILL timing; N3 mpv startup failure cause lost from journal (add INSTALL troubleshooting line or redacted stderr tail); vacuous clean-stop test (PlaySessionTests.cs:406-430; stronger seam test feasible per report); consider mpv --no-config (stray mpv.conf log-file). Device-only unverified: real mpv silence under --no-terminal (check journalctl in LUC-08). Reviewer v1-review-2 closed (wFY).
- DECISION: delivery authorized by AUTH-01/AUTH-03 (reviewed, checks pass). DELIVERY-1 assigned to luc-11-impl: merge luc-11-impl + review branches into ruach/lucifer-v1-orchestrator, source tree must equal 4bcca29's; Coordinator holds off editing that checkout until the delivery handoff.
- DELIVERY-1 handoff valid: delivered_revision 7dd4cf8 on ruach/lucifer-v1-orchestrator (reviewed 4bcca29; source tree identical, `git diff 4bcca29 HEAD -- . ':!docs/coordination'` empty); 390 tests, build 0 errors, maint test OK; destination_before de11d94. Local only; nothing pushed; <repo> still on pr-1.
- Boards updated from evidence: LUC-01..07, 10 done at component level; LUC-09, LUC-11 active (device parts pending); LUC-08, LUC-12 blocked. ACCEPTANCE: 8 automated cases (A01,A04,A07,A09,A12,A13,A14,A17) marked automated pass with fakes/fixtures; 7 mixed cases Partial; device cases Not run; release decision remains not evaluated.

## Cleanup record
Closed: Herdr workspaces/sessions of luc-01..11-impl, v1-review-1, v1-review-2 (all workspaces wFK..wFY). Removed worktrees: luc-01..11-impl, v1-review-1/2, v1-m1. Removed private temp dirs of those launches. Branches retained: ruach/luc-01..07,09..11-impl, ruach/v1-m1, ruach/v1-review-1/2 (all reachable from the delivered branch or recorded above). Retained: orchestrator checkout <worktrees>/lucifer-v1-orchestrator (owner review + pending hardware acceptance); <tmp>/ruach-herdr-T9Z41C belongs to this Coordinator session.

## Remaining (needs owner / device)
1. LUC-08: Pi access (host/SSH or operator-run inventory): OS/arch, mpv, PN532 UART mode, tag chip, ALSA id, controller events (`Amp.Nfc input inspect`), Navidrome version, one source-to-audio run. Select linux-arm64 vs linux-arm from it.
2. Real-server checks: Subsonic error codes, discNumber/track metadata, release MBID mappings for the two known albums and a third album; a true multi-disc album and a continuous pair.
3. Device: install package, reboot, A03/A06/A11/A18-A20/A22/A23, mpv journal silence (no stream URL), systemd recovery, reader unplug/replug, 20 placements.
4. Optional follow-ups: REVIEW-2 N1-N3, weak clean-stop test, mpv --no-config.

## Follow-up request (after v1 software delivery)
- AUTH-04 Owner, exact words: "have a readme written and pushed including setup and the inventory details". Scope: README rewrite (setup + hardware/software inventory), then push of `ruach/lucifer-v1-orchestrator` to `origin` (git@github.com:rothzeta/lucifer.git) as a new remote branch of the same name. NOT authorized by this: pushing master/pr-1/docs branches, opening or merging a PR, force-push, touching the Pi. Push follows review of the README.
- README-1 launched: readme-1 (wFZ, temp <tmp>/ruach-herdr-hV5zH1, branch ruach/readme-1, worktree <worktrees>/readme-1).
- README-1 handoff valid: ruach/readme-1@ed75f70 (report f2dca82): README rewritten (setup, config table, commands, inventory with pending Actual cells); 390 tests, claims checked against help/check output; inconsistency noted: help text says amp-nfc, binary is Amp.Nfc. readme-1 closed. README-REVIEW launched (reviewer readme-review, branch ruach/readme-review, base f2dca82).
- README-REVIEW report valid (ruach/readme-review@6355d39): README ed75f70 no blocking findings; optional F1-F7 (F1-F3 owner-awareness: username/home paths <home>, GitHub account, /opt and /tmp paths in docs/coordination -- left as is, low sensitivity; F4-F7 README wording to be fixed in DELIVERY-2). Reviewer closed, worktrees readme-1/readme-review removed. DELIVERY-2 (delivery-2, wG1, temp <tmp>/ruach-herdr-bCav5G, base d563f58): README fixes F4-F7, merge into destination, push ONLY ruach/lucifer-v1-orchestrator to origin per AUTH-04. Material change is wording only; no re-review.
- DELIVERY-2 handoff valid: README delivered (delivered_revision 7d196d6; report 5b95b1d). README fixes F4-F7 applied; 390 tests; non-board/README source diff vs 4bcca29 empty. PUSHED `ruach/lucifer-v1-orchestrator` to origin (git@github.com:rothzeta/lucifer.git) as a new branch at 5b95b1d; `git ls-remote` confirmed remote ref == local HEAD; master and docs/lucifer-v1-completion unchanged; no PR, no force. delivery-2 closed (wG1), worktrees removed; branches ruach/readme-1, readme-review, delivery-2 retained.
