# Owner instruction: fix CI

Source: direct owner message in this API session, 2026-10-20 UTC, precise timestamp not supplied.

Exact owner words: "fix the ci"

Context: the preceding report confirmed master and v0.1.1 were pushed; master CI passed, but tag CI and release CI failed two application tests. DEC-010 authorizes investigation and bounded fixes to those failures (test synchronization or actual application defects as evidenced), meaningful verification, independent review and delivery/push to master to resolve CI. This supersedes the earlier stop on application changes only for defects needed to fix CI; unrelated refactors remain outside scope. Existing test expectations must be preserved; do not disable tests or bypass gates. Existing tags remain immutable; no tag move, force push or privileged host/Pi installation. No new version/tag is assigned by this instruction; establish corrected master CI first.

## Follow-up questions, 2026-10-21 UTC

Exact owner words: "status?"

Exact owner words: "what is the ci, error and why after so many hours is it not fixed?"

Exact owner words: "why do you add test for hard response time when test are notorious for running in environmnet where response time shouldnt be testes?"

These request explanation and challenge timing-sensitive behavior tests. They do not authorize a provider switch, broader timing refactor or test-gate bypass. Coordinator explained existing fixture margins, the temporary diagnostic delay, and the separation of normal behavior tests from intentional deadline tests. Keep the repair bounded to the current fixture package; no committed performance requirement or artificial-delay assertion is requested.
