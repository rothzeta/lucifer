# Lucifer v1 orchestrator assignment

## Objective and authority

Implement Lucifer through v1 using the execution plan in `BLACKBOARD.md`, the twelve tasks in `blackboards/TAGS.md`, `blackboards/PLAYBACK.md`, and `blackboards/DEVICE.md`, and the release gate in `blackboards/ACCEPTANCE.md`.

Owner instruction: “launch a ruach orchestrator to  implement up to v1”. This follows the request to check out PR #1 and create the v1 plan. PR #1 is the documentation baseline; do not treat its acceptance cases as passing implementation evidence.

You are the Ruach Coordinator. Select and load the installed feature workflow, delegate implementation and technical verification to specialists, obtain independent review, and continue through the ordered milestones. The owner authorizes specialist launches, local implementation, meaningful tests, local commits, and integration onto your retained `ruach/lucifer-v1-orchestrator` branch. Use that branch as the delivery target. Keep the original `<repo>` checkout on `pr-1` intact. Remote push, PR merge, and changes to a live Pi or Navidrome installation are not part of this launch authorization.

Proceed autonomously with software work. If target access or an owner decision is required, finish independent work and record the exact remaining blocker. Do not mark v1 complete until all required device evidence exists.

## Canonical resources and routing

- Repository: `<repo>`.
- Orchestrator worktree: `<worktrees>/lucifer-v1-orchestrator`.
- Ruach resources: `<ruach>`, including `agents/` and `skills/`.
- Routing catalogs: `<ruach>/config/agent-routing`.
- Launcher: `<ruach>/skills/ruach-herdr/scripts/worker.ts`.
- Use the catalogs' declared role preferences; alternatives require explicit authorization. Do not invent model fallbacks.
- Use `ruach-herdr` for specialist launches, `herdr agent prompt` for assignments, and named-agent reads/status for monitoring. Preserve user focus.
- Give every launch explicit `--repo`, `--cwd`, `--resources`, and `--catalogs`, with a fresh worker worktree under `<worktrees>/` and a unique branch/name. Use `--base` pointing to the committed revision that contains that worker's prerequisites; a canonical-repository HEAD can lag your integrated branch.
- Use portable `--permissions auto-review`; do not bypass native security dialogs or approval review. Inspect uncertain launches before any retry.

## Scope and sequence

Retain C#, the existing application project, PN532 over USB-to-TTL, the exact-release `amp:album:1:mbid:` NDEF contract, Navidrome, and persistent mpv playback. Use one application and one test project. Preserve the proposed v1 policies and scope in the blackboard; record any necessary decision before acting on it.

1. LUC-08: obtain the target inventory and physical baseline if access becomes available. Do not infer the Pi hostname or use unrelated machines.
2. LUC-01 → LUC-02 → LUC-03: runnable commands, shared codec and verified writes, external catalogue.
3. LUC-05 → LUC-04 → LUC-06 → LUC-07: correct IPC, stable presence and retries, explicit provider outcomes, complete album replacement.
4. LUC-10 → LUC-09: lifecycle/recovery and the selected controls, respecting the task prerequisites.
5. LUC-11: CI, publish package, service/configuration examples, and reproducible install/update/restore instructions.
6. LUC-12: final evidence against one candidate commit and installed package.

Independent software work may continue before physical baseline completion using explicit boundary substitutes. Do not invent controller event mappings, target architecture, compatibility, or physical test results. Displays, enclosure refinement, automatic catalogue import, play counts, and additional providers remain outside v1.

## Verification and review

Use meaningful tests through public command, codec, controller, HTTP, IPC, and process boundaries. Specialists may add and change tests to cover required behaviour; they must not weaken release cases or expectations to obtain a pass. Run build and relevant tests for each change, then independent verification and review of the combined candidate. A passing command reply does not prove playback or audible output.

Planning workspace observation: `<home>/.dotnet/dotnet --list-sdks` reported SDK 10.0.110. `/usr/bin/mpv`, `/dev/ttyUSB0`, and `/dev/snd` were absent. Recheck the actual execution environment. No application build, runtime test, or device acceptance pass is established by the planning changes.

All A01–A23 remain required. Keep mixed-mode cases partial until their device portions pass. Record missing credentials, hardware access, fixtures, and measurements precisely. Keep secrets and authenticated stream URLs out of commits and reports.

## Durable records and delivery

Create and maintain `docs/coordination/v1/task-record.md` as your authoritative coordination record. Preserve this assignment. Put durable specialist assignments and validated `ruach-handoff` reports under `docs/coordination/v1/`; use unique task directories. Disposable logs and scratch material belong in temporary storage. Require workers to validate their handoffs mechanically and report exact revisions and verification evidence.

Update task status and evidence in the child blackboards as work is accepted. Update the acceptance record only to the extent supported by evidence. Deliver reviewed changes through an Implementer onto `ruach/lucifer-v1-orchestrator`, leaving that branch and checkout available for the owner. Clean up finished specialist resources after preserving their work and reports. Retain your own orchestrator checkout for owner review and pending hardware acceptance.

Report the delivered branch/commit, build and test results, independent review outcome, release acceptance status, and remaining device blockers. Continue until the authorized software work is complete and every remaining dependency on the owner or target device is concrete.
