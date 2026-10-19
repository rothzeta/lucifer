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
