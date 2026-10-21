task: WORK-22-ci-fixture-review
role: Reviewer
status: complete
outcome: "Acceptance WITHHELD for local/master delivery. Every listed acceptance condition holds at 57d343e (positive replies 5 s, Album Normal 5 s/20 s with Fast unchanged and kept explicitly by the two deadline tests, exact three-second behaviour, the Applied assertion strengthened, PlayA completion wait with a 10 s guard, test-only delta). Required checks pass: fresh build 0, full 409/409, deadline filter 6/6, 700 ms probe 5/5. One blocking finding: four sibling AlbumPlayback normal-path tests still assert completed-apply state after only Present's 100 ms bounded wait, the same defect class PlayA had; two use the stale NextEntryId - 2 entry id. All four fail under the same probe."
artifacts:
  - docs/coordination/install-update/reports/ci-fixture-review-v1.md
  - docs/coordination/install-update/assignments/ci-fixture-review-v1.md
reviewed_revision: 57d343e84b1d7aa82699bfd446bd08d2c621a35c
tested_revision: 57d343e84b1d7aa82699bfd446bd08d2c621a35c
evidence_revision: 469242cb6a4900d272cfcc92960ef50f484e5111
baseline: e8181b8e1effc3cf003c11f89e62054f8fbe64c5
verification:
  - "Scope: git diff e8181b8..57d343e outside docs touches only Amp.Nfc.Tests/AlbumPlaybackTests.cs and Amp.Nfc.Tests/TransportControlTests.cs; 469242c has the same non-docs content as 57d343e"
  - "Fresh dotnet build at the worktree root (source identical to 57d343e): exit 0"
  - "dotnet test --no-build (full): exit 0, 409/409"
  - "Deadline filter Uncertain_outcome|Missing_reply|Late_reply|Accepted_playlist|stream_deadline: exit 0, 6/6 (Uncertain_outcome 917 ms, Album Accepted_playlist 711 ms)"
  - "Probe: a scratch copy (git archive 57d343e) with the 700 ms append delay in both rig loadfile handlers; fresh build exit 0. The focused 5 (TransportControl Previous_within_three_seconds x3, Transport_is_serialized, Last_track_ending) exited 0 with 5/5 passing"
  - "Concern probe on the same build, one run, no retries: the whole AlbumPlaybackTests class exited 1. Four normal-path tests failed (see B1). The two explicit Fast deadline tests also failed, which is expected because the probe exceeds their 300/400 ms deadlines, so that result is not evidence. The scratch copy was then removed"
  - "Reused, not rerun: the four deploy shell suites and the production/packaging review (ci-review-v2.md). No deploy, production or config files differ from e8181b8"
  - "Not run: real CI, a contention campaign, a before-contrast for B1 (the tests are unchanged, and a pre-package Fast rig would fail earlier at the reply deadline)"
review:
  - "Withheld: blocking finding B1; optional O1-O3"
discoveries:
  - "Rig.Present cannot simply be changed to wait for full completion, because Transport_is_serialized and Slow_A_then_valid_B deliberately stall an apply after Present. Any fix needs a per-test wait"
blockers:
  - "B1: Stream_error_on_the_selected_album_is_a_visible_failure, Failing_streams_never_put_authenticated_url_parameters_into_application_output, Replacing_a_paused_A_starts_B_unpaused_with_status_derived_from_mpv_events and Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive keep a 100 ms completion margin on the normal path. Coordinator to dispose: a test-only completion wait, or explicit owner acceptance of the residual risk"

# CI fixture review v1 (WORK-22)

Reviewed: product `57d343e` (the full cumulative package since published master `e8181b8`, recipe ab203ed + D1 3656101 + PlayA 57d343e), on branch `ruach/lucifer-ci-fixture-review` at evidence commit `469242c`. Production code, config and install were not re-audited, per the assignment; their delta is empty.

## Acceptance conditions

| Condition | Result | Evidence |
|---|---|---|
| TransportControl positive Reply gets the shipped 5 s | Met | `TransportControlTests.cs:16-20`, which matches `MpvTimeouts.Default.Reply` (`MpvTypes.cs:20`). Connect/Send stay at 500 ms with the plan's rationale (O2) |
| Album Normal = Reply 5 s / StreamStart 20 s, Fast unchanged | Met | `AlbumPlaybackTests.cs:21-29`. Fast is still 500/500/300/400. Normal derives Connect/Send from Fast and takes Reply/StreamStart from the production defaults |
| Uncertain_outcome and Accepted_playlist explicitly Fast | Met | `BuildAsync(timeouts: Fast)` at lines 383 and 421. Both still enforce 300/400 ms (6/6 deadline filter; probe failures show the deadlines are live) |
| Exact three-second behaviour | Met | Unchanged. The boundary uses the fake `time-pos` (SetPlace), not wall-clock time. Probe and full runs pass |
| Assertions retained or strengthened | Met | The only assertion edit is `await transport` → `Assert.Equal(TransportOutcome.Applied, await transport)`. The PlayA exact queue assertion is kept |
| PlayA completion wait + 10 s guard, no sleeps or performance contract | Met | `AlbumPlaybackTests.cs:215-219`. See the mechanism below |
| Production/install/config preserved | Met | Empty delta outside the two test files and docs |

### Does the completion wait establish settled state?

Yes. `StartPreparation` adds the task to `_running` under `_gate` inside `StepAsync`, before Present returns (`SelectionController.cs:193-204`). `WhenIdleAsync` re-snapshots until `_running` is empty (`:80-90`). `PrepareAndCommitAsync` catches every exception (`:228-245`), so the await cannot throw. When it completes, `ApplyAlbumAsync` has returned, which means every loadfile reply and the trailing pause=false reply have been received. `QueueIds`, `Peer.Requests` and `NextEntryId` are therefore final, and `NextEntryId - 2` is the first entry of A.

There are two failure modes, and both are diagnostic rather than masking:

- A failed apply completes the task as Retrying, and the exact queue assertion then fails with the real state.
- A hang raises `TimeoutException` after 10 s.

The 10 s guard is a hang bound, not a speed requirement. Each failed request already ends within 5 s Reply.

## Findings

### B1 (blocking): sibling tests keep the PlayA defect

**Location:** `AlbumPlaybackTests.cs`:

- `Stream_error_on_the_selected_album_is_a_visible_failure` (439-448)
- `Failing_streams_never_put_authenticated_url_parameters_into_application_output` (665-690)
- `Replacing_a_paused_A_starts_B_unpaused_with_status_derived_from_mpv_events` (245-265, the `Assert.False(rig.Paused)` and unpause-order asserts after `Present`)
- `Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive` (453-482, AppliedRelease)

**Problem:** Each one calls `rig.Present(...)`, which waits for apply completion for at most 100 ms (`Tick`, line 110, swallowing the timeout). The tests then assert or act on completed-apply state. Two of them send `end-file` with `playlist_entry_id = rig.Peer.NextEntryId - 2` straight after `Present`. That is the exact pre-v3 PlayA construction that `ci-evidence.md` showed yields a stale entry id, which `Mpv` then filters out.

**Why it matters:** The owner challenged hard response-time assumptions, and this package's own thesis is that CI scheduling stalls exceed 300-500 ms (the master CI failure was a 500 ms reply miss). A 100 ms completion window is shorter than stalls already observed on that runner. So the package does not remove unjustified short margins from all normal-path fixtures, and master CI can go red again on one of these four tests through the same mechanism.

**Evidence:** With the assignment's unchanged 700 ms append probe and a fresh build, one run of the AlbumPlaybackTests class gave these failures:

| Test | Expected | Actual |
|---|---|---|
| `Stream_error…` | Failed | Loading (stale entry id ignored) |
| `Failing_streams…` | Failed | Loading (same mechanism) |
| `Replacing_a_paused_A…` | `Paused` False | True (unpause not yet sent) |
| `Slow_A_then_valid_B…` | AppliedRelease B | null |

`Replacing_A_with_B…` and `Failed_preparation…` pass only because the fake peer records queue changes when a request is received. The probe widens the window, but the code shows the window exists without it, as `ci-evidence.md` argued for PlayA.

**Suggested direction:** Test-only, mirroring PlayA:

- In the three tests where nothing stalls (`Stream_error…`, `Failing_streams…`, `Replacing_a_paused_A…`), add `await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));` after the relevant `Present`.
- For `Slow_A_then_valid_B…`, wait for completion of B's apply after `Present("04BB")` rather than full idle, because A's stalled task stays in `_running` until it is released. Alternatively, wait for full idle after `releaseA.SetResult()`, before the AppliedRelease assert.
- Leave `Rig.Present` unchanged (see discoveries).
- Verify with a probe before/after on these four tests.

If the Coordinator or owner prefers to accept the residual risk, that is a decision for them, not for this review.

### O1 (optional): name of the Transport timeout set

`TransportControlTests.Fast` now carries a 5 s Reply and a 30 s StreamStart, so the name `Fast` is misleading. The comment covers it, and renaming it (for example to `Normal`) is cosmetic.

### O2 (optional, accepted rationale): Connect/Send at 500 ms

Connect and Send stay at 500 ms on the normal path in both rigs. I accept the plan's rationale (`ci-fixture-plan.md:34`): a unix-socket connect and a kernel-buffer write do not need the peer to be scheduled, and neither has failed in CI. I note it only as a remaining fixed margin.

### O3 (optional, pre-existing, out of package scope)

The MpvTests Fast set is shared by positive and deadline tests (plan residual R1). `WhenIdleAsync` can spin briefly between task completion and its removal continuation; production code, harmless.

## Delivery acceptance

Withheld for `ruach/lucifer-install-update` and for master, pending the disposition of B1. Everything else in the package is acceptable as reviewed. Where it was required, the acceptance above is backed by this review's own runs, not by the Implementer's reports.

## Commands (worktree root unless noted; build/test bounded with `timeout 300`)

- `dotnet build` → 0
- `dotnet test --no-build` → 0 (409/409)
- `dotnet test --no-build --filter "FullyQualifiedName~Uncertain_outcome|…~Missing_reply|…~Late_reply|…~Accepted_playlist|…~stream_deadline"` → 0 (6/6)
- In a scratch `git archive 57d343e` copy with the probe:
  - `dotnet build` → 0
  - focused filter on the 5 cases → 0 (5/5)
  - `--filter FullyQualifiedName~AlbumPlaybackTests` → 1 (failures as listed above)
  - The copy was then deleted
- The logs were disposable scratch files and were not kept; the results are recorded here.

## Handoff validation

`bun <ruach-handoff>/scripts/validate.ts <this report> --repo .` → exit 0, ok true, all four revisions resolved.

## State

No tracked source or test changed. No processes were left running, and no ports, profiles or network writes were used.
