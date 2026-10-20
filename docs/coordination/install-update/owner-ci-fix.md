# Owner instruction: fix CI

Source: direct owner message in this API session, 2026-10-20 UTC, precise timestamp not supplied.

Exact owner words: "fix the ci"

Context: the preceding report confirmed master and v0.1.1 were pushed; master CI passed, but tag CI and release CI failed two application tests. DEC-010 authorizes investigation and bounded fixes to those failures (test synchronization or actual application defects as evidenced), meaningful verification, independent review and delivery/push to master to resolve CI. This supersedes the earlier stop on application changes only for defects needed to fix CI; unrelated refactors remain outside scope. Existing test expectations must be preserved; do not disable tests or bypass gates. Existing tags remain immutable; no tag move, force push or privileged host/Pi installation. No new version/tag is assigned by this instruction; establish corrected master CI first.
