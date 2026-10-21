task: WORK-25-ci-final-plan
role: Architect
status: needs-decision
outcome: "One bounded test-only recipe for B2, three edits in two methods of Amp.Nfc.Tests/AlbumPlaybackTests.cs. E1 adds the reviewed full completion wait to Replacing_A_with_B. E2 adds a wait in Slow_A_then_valid_B for B's own commit (Controller.AppliedRelease == B), bounded by the existing 10 s hang guard. It runs while A is still stalled, so it needs no full idle. E3, which the Coordinator must adopt explicitly, replaces the wall-clock sw < 500 assertion with a deterministic check that StepAsync completed synchronously. No margins, sleeps, Rig.Present or timeout changes. Ready to implement after the Coordinator decides on E3."
artifacts:
  - docs/coordination/install-update/reports/ci-final-plan.md
  - docs/coordination/install-update/assignments/ci-final-plan-v1.md
inspected_revision: 67426dd6946d9df81b56052c73829972face7f4e
evidence_revision: 7b715cd39327811279d5d2786928edd8fa0350d4
verification: not-run
review: not-run
discoveries:
  - "Controller.AppliedRelease is set only after prepared.CommitAsync returns. That is after the replace, every append and the pause=false reply (Mpv.ReplaceQueueAsync). The fake peer records queue changes when it receives a request. So AppliedRelease == B implies QueueIds == [tB1,tB2] and is a B-specific completion event that A's stall cannot block"
  - "Stalled A holds neither SelectionController._commit nor Mpv._operations: it is still inside PrepareAsync, before both. B therefore commits while A is stalled. This is the no-deadlock argument for E2"
  - "The existing sw < 500 assertion cannot catch the regression it names. If StepAsync awaited preparation, A never releases and the test hangs rather than failing. It only catches a slow-but-returning step, which is a wall-clock speed test of the kind the owner questioned"
blockers:
  - "Decision for the Coordinator: adopt E3, the replacement of the sw < 500 assertion at line 472, explicitly before implementation. If it is declined, implement E1 and E2 only, leave line 472 unchanged, and record its timing dependence as an accepted residual risk"

# CI final plan (WORK-25): B2 synchronization recipe

Inspected: `67426dd` test and source. The branch base `7b715cd` has identical non-docs content. Line numbers refer to `Amp.Nfc.Tests/AlbumPlaybackTests.cs` at that revision. I read only the two B2 methods, `Rig.Present`/`Tick`, `Eventually`, `PlayA`, the peer handler in `BuildAsync`, `SelectionController` (`StepAsync`, `StartPreparation`, `PrepareAndCommitAsync`, `WhenIdleAsync`, `AppliedRelease`), `PlayerAlbumApplier.CommitAsync`, and `Mpv.LoadAlbumAsync`/`ReplaceQueueAsync`. I built and ran nothing.

## Completion facts the design relies on (static)

1. `StepAsync` is synchronous and returns `Task.CompletedTask`. `StartPreparation` adds the task to `_running` before `StepAsync` returns. `PrepareAndCommitAsync` starts with `Task.Yield`, so preparation always runs asynchronously (`SelectionController.cs:56-77, 193-208`).
2. The commit order is: `PrepareAsync` → `_commit.WaitAsync` → generation check → `CommitAsync` → `_appliedRelease = release` (`:211-226`). `CommitAsync` → `Mpv.LoadAlbumAsync` holds `_operations` and awaits the replies to the replace, each append and pause=false (`Mpv.cs:85-112`). If any reply fails, it throws, and `AppliedRelease` is not updated.
3. The fake peer changes `rig.Queue` when it receives a loadfile, before it replies (`AlbumPlaybackTests.cs:171-181`). When `CommitAsync` returns, the queue is therefore final.
4. A stale commit returns without touching the player (`:217-219`). After B is committed, nothing in Slow_A can change `AppliedRelease` or the queue before the release.

## Proposed edits (test-only, one file)

### E1: `Replacing_A_with_B_leaves_all_and_only_B_in_order_exactly_once` (B2 site 1)

After line 236 `await rig.Present("04BB", ReleaseB);`, insert:

```csharp
        await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
```

This is the direction already reviewed, and the same pattern as `PlayA` and the B1 waits.

- **Why behavior is preserved:** nothing is stalled in this test. `PlayA` already reached idle, and B's prepare and commit always end, because `PrepareAndCommitAsync` catches every exception. Idle therefore means B's apply has completed, and the unchanged exact assertions at 238-241 observe the final queue and request log.
- **Failure modes:** a failed apply leaves a wrong queue, and the exact assertion shows it. A hang raises `TimeoutException` after 10 s.

### E2: `Slow_A_then_valid_B…`, assertion before the release (B2 site 2)

After line 475 `await rig.Present("04BB", ReleaseB);` and before the unchanged line 476 `Assert.Equal(["tB1", "tB2"], rig.QueueIds);`, insert:

```csharp
        // A is still stalled, so full idle is impossible here; wait for B's own commit instead.
        Assert.Equal(ReleaseId.Parse(ReleaseB),
            await Eventually(() => rig.Controller.AppliedRelease, r => r == ReleaseId.Parse(ReleaseB), ms: 10_000));
```

**Predicate:** `Controller.AppliedRelease == ReleaseB`. This uses existing public observable state and the existing `Eventually` helper. `ReleaseId` is a `record struct`, so the nullable equality is lifted.

**Placement:** strictly after the B `Present` and strictly before `releaseA.SetResult()` at 478. Lines 476 and 478-486, including the B1 idle wait at 479 and the post-release assertions, stay exactly as they are.

**Why this predicate instead of partial queue readiness:** the reviewer suggested `q.Length == 2`. That condition becomes true as soon as the peer receives the tB2 append, before the reply and before pause=false. It only stands in for queue state. `AppliedRelease == B` is the event the test's intent names: B is committed while A is still blocked. By fact 3 it implies the full queue, and the unchanged exact assertion at 476 still checks content and order. It also tightens the test: a B commit that fails after mutating the queue would pass the length predicate but fails E2.

**Why there is no deadlock while A is stalled:** E2 polls a value and does not await `_running`. A is blocked in `PrepareAsync` (the HTTP stub ignores cancellation), which is before `_commit` and `_operations` are taken. B's task can therefore prepare, acquire both, commit and set `AppliedRelease` without A making any progress. If B cannot commit (a reply is missing or fails), `Eventually` returns the actual value after 10 s, and `Assert.Equal` fails with `null` or the actual release, without hanging.

**The bound:** `10_000` reuses the suite's existing 10 s hang guard (lines 218, 254, 479). It is not a speed requirement. The helper's default of 3 s is shorter than one normal 5 s Reply, so it would itself be an unjustified margin. That is why the bound is passed explicitly. No timeout definitions change.

### E3 (Coordinator adoption required): `sw < 500` at lines 467-472

**Assessment:** the assertion tests speed. It times two synchronous calls with a wall-clock stopwatch, so a 500 ms scheduler or GC pause on a CI runner fails it even when the code is correct. It also cannot detect the regression it names. If `StepAsync` awaited preparation, it would wait on A, which is never released before line 478, so the test would hang rather than fail.

The property under test is this: a scan step finishes without depending on preparation progress. That can be shown deterministically, because the controller's contract is that `StepAsync` returns an already-completed task.

**Replacement:** replace lines 467-472:

```csharp
        var sw = Stopwatch.StartNew();
        rig.Reader.Current = ReaderObservation.Present("04AA", () => new(TagReadKind.Release, ReleaseId.Parse(ReleaseA)));
        await rig.Controller.StepAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(150));
        await rig.Controller.StepAsync(CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds < 500, "scanning must not wait for preparation");
```

with:

```csharp
        rig.Reader.Current = ReaderObservation.Present("04AA", () => new(TagReadKind.Release, ReleaseId.Parse(ReleaseA)));
        var first = rig.Controller.StepAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(150));
        var second = rig.Controller.StepAsync(CancellationToken.None);
        // A is stalled until releaseA is set, so a step that waited for preparation could not be complete here.
        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, "scanning must not wait for preparation");
```

Leave line 473 (`AlbumRequests.Count == 1`) unchanged. `using System.Diagnostics` stays, because `Eventually` uses `Stopwatch`.

**Why responsiveness is preserved, or made stronger:**

- E3 checks the scan-step contract through completion state rather than time. A step that awaits A now fails immediately at this line, where before it would hang.
- E2 then proves end-to-end responsiveness by ordering, not by speed: a new tag (B) is read, prepared and committed while A is still blocked.
- The existing post-release checks still prove that late A cannot replace B.

**Behavior preserved:** current code returns `Task.CompletedTask`, so the assertion passes for the same code that passes today.

**Static limit:** a hypothetical bounded synchronous wait inside `StepAsync`, for example `Wait(2s)` on the preparation, would return a completed task and pass E3, while today it fails `sw < 500`. Detecting that case needs a time threshold, which is exactly what the owner questioned. No such wait exists in the code. The Coordinator should accept this trade-off explicitly when adopting E3.

## Unchanged

- `Rig.Present`/`Tick` and their 100 ms bounded wait
- every other test and assertion
- `Fast`/`Normal` and every timeout definition
- the B1 waits
- the pre-existing `Task.Delay(200)` and `Ticks(3)` in Slow_A
- `Amp.Nfc/**`, `FakeMpvPeer` and all other files

`Failed_preparation_of_B` (361) is out of scope: the assignment names only the two sites, and the reviewer saw it pass under the probe.

## Package

There is one package with one Implementer, because all edits are in one file. Owned: the two method bodies above in `Amp.Nfc.Tests/AlbumPlaybackTests.cs`. No support, docs or production files are touched. There is no serial dependency except the Coordinator's E3 decision. No ports, profiles or databases are needed.

Copyable first assignment (Coordinator to fill IDs, base, branch and E3 disposition):

```markdown
# WORK-26 v1 (role: Implementer): B2 completion waits
Task install-update / WORK-26. New worktree, new branch <checked unused>, base <commit containing 67426dd source>. Owner DEC-010 "fix the ci"; owner concern about hard response times. Design: reports/ci-final-plan.md. E3 disposition: <adopted | declined>.

Outcome: B2 sites wait for actual completion; no speed requirement remains in Slow_A <if E3 adopted>.
Scope: Amp.Nfc.Tests/AlbumPlaybackTests.cs, only Replacing_A_with_B_leaves_all_and_only_B_in_order_exactly_once and Slow_A_then_valid_B_late_A_cannot_replace_B_and_scanning_stays_responsive, applying E1, E2 <and E3> exactly as written in the plan. If an edit cannot be applied as written, stop and report; do not adapt its semantics.
Protected: everything else, including Rig.Present/Tick, the timeout definitions, the B1 waits, other tests, Amp.Nfc/**, FakeMpvPeer, workflows/config, the task record and old reports.

Acceptance and verification: the matrix in reports/ci-final-plan.md, rows M1-M7, run once each, bounded at 300 s per build/test, with real exit codes and fresh builds. Apply the stop condition exactly. No retries, skips, margins or parallelism changes.
Restrictions: probes and mutations only in disposable scratch copies outside the repo. No push, CI, delivery or remote actions. Neutral commits, no AI trailers or private paths.
Handoff: docs/coordination/install-update/reports/ci-b2.md with the ruach-handoff leading block. Run the validator with --repo and commit this assignment unchanged with the report, separately from the product commit. Independent review follows.
```

## Acceptance matrix

Probes go in a scratch `git archive <candidate>` copy, with a fresh build (exit 0) before each probe run. "Replace delay" means the reviewer's v2 probe: 700 ms before the reply to `loadfile … replace` in the Album rig handler. "Append delay" means the v1/v4 probe: 700 ms before the reply to `append`. Use filter `R` = `Replacing_A_with_B|Failed_preparation_of_B|Slow_A_then_valid_B`, and filter `B1` = `Stream_error_on_the_selected_album|Failing_streams_never_put|Replacing_a_paused_A|Slow_A_then_valid_B`.

| # | Condition | Before (evidence) | After (required) |
|---|---|---|---|
| M1 | Replace delay, filter R | Reused, not rerun: at 67426dd, exit 1. Line 238 and line 476 fail with "differ at position 1"; Failed_preparation passes (ci-fixture-review-v2) | Exit 0, 3/3 |
| M2 | Append delay, filter R + B1 | B1 4/4 at 67426dd (review v2) | Exit 0, all pass |
| M3 | E3 only: mutation `StepAsync` returns `WhenIdleAsync()` instead of `Task.CompletedTask`, filter `Slow_A_then_valid_B` | Not run: on the current test this mutation hangs, which is the defect E3 removes | Exit 1, failing at the E3 assertion with "scanning must not wait for preparation". No hang: the test ends at that line |
| M4 | Probe and mutation removed (grep shows no probe text), fresh `dotnet build` | — | Exit 0 |
| M5 | `dotnet test --no-build`, full, unmodified candidate | 409/409 at 67426dd | Exit 0, 409/409 |
| M6 | Deadline filter `Uncertain_outcome\|Missing_reply\|Late_reply\|Accepted_playlist\|stream_deadline` | 6/6 at 57d343e (source of the deadline tests unchanged since) | Exit 0, 6/6 |
| M7 | `git diff <base>..<candidate> --stat` outside docs | — | Only AlbumPlaybackTests.cs: E1 +1, E2 +3, E3 net about 0 lines (6 lines replaced) |

If E3 is declined, drop M3, and M7 expects E1 and E2 only.

Reused without rerun, with rationale: shell suites and packaging, because no production, deploy or config file changes. No contention campaign and no real-CI reproduction are required. The probes show completion-based synchronization under a slow peer. They do not reproduce CI scheduling.

**Stop condition:** if any after-cell fails on its single run, stop. That includes:

- M1 or M2 not exiting 0
- M3 passing, or hanging past the 300 s bound
- M5 not 409/409
- M6 not 6/6
- any extra file in M7

Then:

- Preserve the output.
- Do not retry, add margins, move assertions, or edit outside the two methods.
- Report to the Coordinator as blocked.

A third withheld cycle on this class means escalating to the owner for explicit risk acceptance, not another patch.

## Static inference limits

The following are inferred from reading the code, not observed:

- that E2 cannot deadlock
- that `AppliedRelease == B` implies the final queue
- that A holds no lock
- that E3 passes on current code

M1-M5 are the checks that confirm them. I assumed the HTTP client does not abandon the stalled handler on cancellation. If it did, A would end early, which only makes E2 easier. No reviewer acceptance is claimed; an independent Reviewer is still required on the implemented candidate.
