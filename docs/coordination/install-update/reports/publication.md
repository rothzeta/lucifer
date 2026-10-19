---
task: install-update-work-08
role: implementer
assignment: docs/coordination/install-update/assignments/publication-v1.md
status: blocked
outcome: master fast-forwarded to 91c264d and tag v0.1.0 pushed, but release workflow failed twice in the Test step (flaky-looking app test), so no GitHub Release or assets exist and live asset/installation verification was not possible
reviewed_revision: abb2f1176973a7f992cf781bee599ad8b81d284d
tested_revision: e2ee0f4a4d7d76fb3b7237fd6ffb5f8cb2371bd2
published_revision: 91c264d80208242f304e3b1ab496921a8efb81cc
remote_master_before: 452637b20863d6db8f993bc8b1674aaa44534ad6
tag: "v0.1.0 (annotated object 2bd917a8ccc6eed492e605da900749895886a705, peeled to 91c264d)"
artifacts:
  - docs/coordination/install-update/assignments/publication-v1.md
  - docs/coordination/install-update/reports/publication.md
  - "https://github.com/rothzeta/lucifer/actions/runs/39337214259"
  - "https://github.com/rothzeta/lucifer/actions/runs/39337214285"
  - "https://github.com/rothzeta/lucifer/actions/runs/39337191858"
  - branch ruach/lucifer-publish
verification:
  - "Preconditions passed: git diff e2ee0f4 91c264d excluding docs/coordination empty; ed5ab05 and 452637b are ancestors of 91c264d (fast-forward); worktree clean; origin git@github.com:rothzeta/lucifer.git; ls-remote master exactly 452637b; no v0.1.0 tag and zero releases; no forbidden trailers in 452637b..91c264d; raw publication-authorization.md re-read"
  - "git push origin 91c264d:refs/heads/master (non-force): 452637b..91c264d; ls-remote then showed master 91c264d"
  - "Annotated tag v0.1.0 created with neutral identity on 91c264d and pushed (new tag); ls-remote peeled value 91c264d"
  - "Release run 39337214259 (event push, tag v0.1.0) attempt 1: failure in Test step; dotnet test Failed 2 of 408 (AlbumPlaybackTests.Transport_is_serialized_with_album_replacement, PlaySessionTests.Pending_work_that_ignores_cancellation_is_abandoned_at_the_deadline_with_a_failure)"
  - "Tag ci run 39337214285: failure, 1 of 408 failed (the PlaySessionTests deadline test)"
  - "Master ci run 39337191858 (same commit): success"
  - "Single allowed retry of 39337214259 (attempt 2, same tag and commit): failure again, 1 of 408 failed (the PlaySessionTests deadline test)"
  - "Public master install.sh fetched over HTTPS from raw.githubusercontent.com is byte-identical to 91c264d:install.sh"
  - "https://github.com/rothzeta/lucifer/releases/latest/download/lucifer-install returns HTTP 404; gh release list is empty"
  - "Not run: archive/checksum/layout verification, latest and explicit-tag installs, curl-piped bootstrap (no release assets exist; bootstrap needs a release)"
review: not-run
discoveries:
  - "Timing-sensitive test PlaySessionTests.Pending_work_that_ignores_cancellation_is_abandoned_at_the_deadline_with_a_failure failed in 3 of 4 CI runs on this commit (both tag-triggered ci/release attempts and one master run) but passed once on master; it and AlbumPlaybackTests.Transport_is_serialized_with_album_replacement are likely load-sensitive on GitHub runners. Not clearly transient after the retry, so it needs a test/product fix outside this assignment"
  - "Master is published at 91c264d; the pushed tag v0.1.0 points to a commit whose release workflow cannot pass as-is. No force or tag replacement was done. The tag is not yet a release; deleting and re-pointing it is not authorized and was not done"
  - "Fix options for Coordinator: stabilise the two tests (material fix, needs combined verification and independent review and an authorization for a new tag/commit publication), or re-run the workflow again if the owner accepts flakiness"
blockers:
  - "Release workflow Test step fails on the tag commit; no release, no assets; live verification (assignment item) blocked"
  - "Any fix changes published code; needs review and a decision on how to move or replace tag v0.1.0 (not authorized for replacement)"
---

# WORK-08 publication

Published master 91c264d (non-force) and tag v0.1.0, triggering release run 39337214259. The run failed twice (attempt 1 and the one allowed retry) at `dotnet test`, before packaging, so no release was created. Nothing else was mutated: no installs, no settings, no cleanup.

Limits: stub evidence is not physical acceptance; release assets, install flows and checksums remain unverified.
