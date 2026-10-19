# Install/update task record

Coordinator: current owner session. Workflow: canonical ruach-workflow-feature.
Source and retained owner checkout: task checkout on `ruach/lucifer-install-update`.
Base: `origin/master` resolved to `452637b20863d6db8f993bc8b1674aaa44534ad6`.
The unrelated source checkout remains on stale `pr-1`; no historical branch integration.
Durable evidence: this directory; assignments and reports committed and retained.

## Authority

- DEC-001 (accepted; owner prompt and [raw assignment](assignment.md), 2026-10-19 UTC, time unspecified): "create an install script and an update command" and "launch an orchestrator with --approve for me to do it". Authorizes bounded implementation and specialist coordination; native Coordinator uses `--approve-for-me`, workers portable `--permissions auto-review`.
- DEC-002 (accepted; owner prompt, 2026-10-19 UTC): "Deliver reviewed local commits onto ruach/lucifer-install-update. Do not push, merge remote refs, or run a privileged installation on this host or a live Pi." Conditional local delivery authority is active after independent acceptance. No remote or live installation authority.
- DEC-003 (accepted; owner prompt): "Preserve configuration and prior releases, implement usable installation/update/rollback, test in isolation, update package contents and docs, and keep physical acceptance unverified." Applies to every work unit.
- DEC-004 (accepted; owner prompt): "Continue through reviewed local delivery." Owner checkout and branch are retained for review; specialist resources are released when no pending work needs them.

## Startup recovery

Owner reports the initial native command exceeded the terminal canonical line limit before execution. Parent confirmed only bash was running, cancelled incomplete shell input, and executed identical prepared argv from `<temporary-root>/lucifer-install-update-native-start.sh` in the same pane. This Coordinator did not repeat that startup or alter unrelated workspaces.

## Plan and state

Phase: understand/plan; required source files, installation documentation, README and CI read before assignment.
Existing release layout and maintenance environment reader are the integration boundary.
WORK-01 implementation/integration: queued; Implementer owns deploy scripts, isolated tests, packaging, README/install docs and CI; no application refactor. WORK-02 combined verification: queued after WORK-01. WORK-03 independent review: queued after combined checks. WORK-04 local delivery: gated by successful review and DEC-002.
Use preferred routes: Implementer `claude-sonnet-5.5-medium`, Reviewer `claude-opus-5.5-medium`; canonical resources and routing catalogs supplied by owner. Default two failed fix/review cycles before escalation; no automatic alternate routing.
Coordinator alone edits this record. Workers commit their unchanged assignments and mechanically validated handoffs. No ports, profiles or databases needed; isolated temporary fixtures only.

## Resources and evidence

- Retained Coordinator checkout: active; kept by owner instruction. Caller pane and unrelated sessions protected.
- Planned worker `lucifer-iu-impl`, branch `ruach/lucifer-iu-impl`, fresh sibling task worktree: reserved for implementation, integration and later fixes/delivery.
- Launcher/handoff dependency install attempts returned EEXIST against existing dependency directories; existing launcher offline resolution succeeded. No dependency or harness configuration changed.
- Candidate/tested/reviewed/delivered revisions: none yet. Reports observed/handled: none. Blockers: none.

Next: commit intake, launch preferred Implementer with explicit base and auto-review, write assignment in its worktree and submit through named Herdr agent commands. Then meaningful combined verification, independent review, bounded fixes if needed, separate local delivery assignment and resource cleanup.

WORK-01 launch prerequisite: intake commit `ac9e415` (descends directly from requested base), with fresh branch/worktree checked by launcher before mutation. Canonical launcher dependencies already resolve; reuse existing pinned installation after EEXIST without replacing it. Launch phase begins; no product acceptance yet.

Launch recovery event: WORK-01 launcher exited 4 at worktree creation, submission not made. Git confirms no worker branch/worktree; named agent absent; Herdr lists no task worker workspace. Linked parent metadata points at a source workspace currently rebound to another repository. Do not modify it. Operational choice: retry same preferred role/model/permissions using launcher's supported standalone worktree mode by omitting parent workspace context for that subprocess. This changes topology only, not routing; owner explicitly authorized fresh worker worktrees. No partial work or temporary directory exists from failed launch.

WORK-01 active: preferred Claude Sonnet 5.5 medium, auto-review. Standalone workspace `wJS`, pane `wJS:p1`, branch `ruach/lucifer-iu-impl`, worktree `<repo>/.worktrees/lucifer-iu-impl`, private launch directory `<temporary-root>/ruach-herdr-eYdjxK`. Ready confirmed by launcher. Assignment `assignments/implement-v1.md` written in worker worktree; checklist checked (role/scope/authority/base/dependencies/verification/evidence/protected paths). Session/worktree reserved for integration, review fixes and separate delivery. Coordinator owns cleanup.

Assignment transport correction: empty assignments/reports directories were absent from the committed base, so the first file write failed and the first short prompt referred to a missing path. Created directories and wrote the initial v1 assignment unchanged before re-notifying the worker; no prior assignment body existed to supersede. No product work authorized beyond the now-written v1.

Operational plan refinement: WORK-02 uses a separate Implementer on the same preferred route for meaningful independent verification. Preparation of observable acceptance cases can run alongside WORK-01; execution on the combined candidate is gated on WORK-01 handoff and an explicit pinned revision. WORK-02 owns only its assignment/report and disposable isolated probes; no production edits, so no overlap with WORK-01. Reserved worker `lucifer-iu-verify`, branch/worktree of the same name under task worktrees, prerequisite intake `ac9e415`. Standalone topology avoids the known stale linked-parent metadata.

WORK-02 launched ready: workspace `wJT`, pane `wJT:p1`, standalone `<repo>/.worktrees/lucifer-iu-verify`, private temp `<temporary-root>/ruach-herdr-Mzxz4u`, preferred Sonnet 5.5 medium auto-review. Assignment `assignments/verify-v1.md` written in worker checkout and checklist checked. Preparation running; execution explicitly waits for pinned candidate amendment. No test/production overlap permitted.

WORK-02 preparation observed done via named agent: read required existing deploy/CI scope and prepared isolated adversarial/effects-order cases; no verification executed. Worker retained idle for the concrete pending candidate pin. No completion report yet, and no acceptance inferred. WORK-01 still working.
