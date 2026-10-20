# CI fixture plan: normal-path mpv reply deadlines in test rigs (WORK-20)

Inspected source: base `17f84d8`, product identical to published master `e8181b8`. Evidence: CI run 39485124360 attempt 1 (failure text preserved in [reports/ci-delivery.md](reports/ci-delivery.md)), the WORK-09 diagnosis (F2) in retained commit `41ab1c3` (`docs/coordination/install-update/release-recovery-plan.md`), and [reports/ci-implementation.md](reports/ci-implementation.md). Read-only inspection only: no tests, builds, load or prototypes were run for this plan.

## Diagnosis

### Classification: test-rig margin, not a behaviour contract and not a product regression

The failing test `TransportControlTests.Previous_within_three_seconds_selects_the_previous_track(played: 3)` failed in `Rig.Start` (`TransportControlTests.cs:66`), before the behaviour under test ran.

- **Where it failed.** `Mpv.ReplaceQueueAsync` (`Amp.Nfc/Mpv.cs:104-112`) sent `loadfile … replace`, which was answered. Then an `append` at `Mpv.cs:109` was not answered within the rig's `Reply = 500 ms` (`TransportControlTests.cs:14-18`). By policy, that becomes `AlbumIncomplete`: the player is stopped and Failed, and the exception is thrown (`Mpv.cs:114-118`). The reported duration of 1 s fits two 0.5 s reply waits: the append, then the reconciling `stop`.
- **Why it is not a timing contract.** The three-second previous-track boundary uses the fake `time-pos` set by `SetPlace` (`TransportControlTests.cs:152-155, 253`), not wall-clock time. The 0.5 s reply deadline plays no part in the asserted behaviour. No test in `TransportControlTests` relies on a Connect, Send or Reply deadline. Every handler path answers (`TransportControlTests.cs:102-148`, and the default reply in `FakeMpvPeer.cs:92-99`).
- **Why it is not a product regression.**
  - The reply path has not changed since `af61d1a`, which passed: `Mpv.Ipc.cs` `SendAsync`, the read loop and `Handle`.
  - The delivered diff `af61d1a..e8181b8` touches only `Mpv.Dispose`, a socket-ownership check in `Mpv.Launch.cs`, `AlbumPlaybackTests.PlayA` and one new `MpvTests` test.
  - This rig uses `Mpv.ConnectAsync`, which has no child process, so the Dispose change is not on its path.
  - The production reply deadline is 5 s (`MpvTimeouts.Default`, `Amp.Nfc/MpvTypes.cs:17-21`), ten times this rig's value.
- **Mechanism (hypothesis, not proven).**
  - A reply is only observed after two pieces of work are scheduled on the thread pool: the peer's `Serve` continuation writes the reply, and then the `Mpv` read loop completes the `TaskCompletionSource`.
  - On a 4-vCPU hosted runner, xUnit runs test classes in parallel (no runsettings or `xunit.runner.json` exist). Some tests block pool threads, for example `WaitForExit` in `BuiltProcessTests` and `Mpv.StopChild`, and the `Barrier` plus `Dispose` in the new concurrent-dispose test. Under that load, a delay of more than 0.5 s for one reply is plausible.
  - The failure occurred 2.02 s into the run, when parallel start-up load is highest.
  - The new concurrent-dispose test adds blocking pool work, and may have raised the probability of a delay. This is unverified, and it is not a product defect.
- **Same class as before.** WORK-09 F2 diagnosed the same mechanism for `AlbumPlaybackTests.Transport_is_serialized_with_album_replacement`: a 300 ms reply, a deliberate stall, then `Ignored` and an assertion failure. WORK-09 also saw the AlbumPlayback rig setup (`PlayA`) miss the 300 ms reply under one-core stress. The F2 margin change was planned in WORK-09 Package A but never landed. The WORK-17 fix replaced it with the `PlayA` 10 s wait, which cannot help once a reply deadline has cleared the queue.

### Directly related normal-path reply deadlines

| Fixture | Current limit | Tests that need it | Exposure | Action |
|---|---|---|---|---|
| `TransportControlTests` `Fast.Reply` (lines 14-18) | 500 ms | none | Every `Rig.Start` (≥4 round trips). Also the deliberate replacement stalls, `A_control_pressed_during_replacement…` (`Task.Delay(100)`) and `A_press_during_album_replacement…` (`Task.Delay(150)`), which have the F2 shape with only 350–400 ms of margin | **Change** |
| `AlbumPlaybackTests` `Fast.Reply` (lines 21-25), the default in `BuildAsync` (line 133) | 300 ms | Only `Uncertain_outcome_is_reconciled_before_any_new_load_and_never_replayed_blindly` (line 376), which drops replies on purpose | Every `PlayA`/`Present` load. Also `Transport_is_serialized_with_album_replacement` (F2, deliberate stall plus `Task.Delay(100)`) | **Change the default; keep 300 ms for the deliberate test** |
| `MpvTests` `Fast` (lines 9-13) | 300 ms (all fields) | Deadline tests in the same class: `Missing_reply…` (`InRange 250..1500`), `Late_reply…`, the readiness and start-deadline tests, and the stream deadline | Positive IPC tests, about 2–4 round trips each, not seen failing in any CI run | **Leave** (residual R1) |
| `PlaySessionTests` `FastIpc.Reply` | 30 s | n/a | none | none |

Connect and Send (500 ms) stay unchanged. A unix-socket connect to a listening socket, and a write into the kernel buffer, complete without the peer being scheduled. Only Reply depends on the peer and the read loop being scheduled. Neither of the first two has failed in CI.

## Recipe (one package, test-only)

Owned files:

- `Amp.Nfc.Tests/TransportControlTests.cs`: the `Fast` field only.
- `Amp.Nfc.Tests/AlbumPlaybackTests.cs`: the timeouts fields, the `BuildAsync` default, the `BuildAsync` call in `Uncertain_outcome…`, and one added assertion in `Transport_is_serialized…`.

Protected: all of `Amp.Nfc/**` (production `MpvTimeouts.Default`, `Mpv*.cs`), `FakeMpvPeer.cs`, `MpvTests.cs`, `PlaySessionTests.cs`, workflows, `deploy/`, install and update, and every other test and assertion.

1. **TransportControlTests.cs:14-18.**
   - Change `Reply: TimeSpan.FromMilliseconds(500)` to `Reply: TimeSpan.FromSeconds(5)`.
   - Keep Connect, Send and StreamStart as they are.
   - Add a one-line comment: no test here exercises the reply deadline, and normal replies get the production allowance, so a slow scheduler cannot turn setup into an album-load failure.
2. **AlbumPlaybackTests.cs:21-25 and 133.**
   - Keep `Fast` exactly as it is; it is the deadline configuration.
   - Add `private static readonly MpvTimeouts Normal = Fast with { Reply = TimeSpan.FromSeconds(5) };` with a short comment on the same point.
   - Change the `BuildAsync` default from `timeouts ?? Fast` to `timeouts ?? Normal`.
3. **AlbumPlaybackTests.cs:378** (`Uncertain_outcome…`).
   - Change `BuildAsync()` to `BuildAsync(timeouts: Fast)`.
   - Its dropped-reply behaviour, its 300 ms timing and every assertion stay identical. This is the only explicit reply-deadline test in the class.
4. **AlbumPlaybackTests.cs** `Transport_is_serialized…` (lines 554-588). Change `await transport;` to `Assert.Equal(TransportOutcome.Applied, await transport);`. This was recommended in WORK-09 F2: it makes the failure self-explaining and tightens the assertion. Nothing else changes.

### Why this preserves the tested behaviour

- 5 s is the shipped reply deadline, so the rigs now model production rather than a stricter-than-production deadline.
- Every positive-path assertion is unchanged, including:
  - the boundaries at 3.0, 2.9, 0.0 and 3.5 s;
  - the stall and serialization ordering, `MaxInFlight == 1`, and the `IsCompleted == false` checks during stalls;
  - the order and debounce checks.
- These assertions never depended on the reply deadline. The deadline only controls how long a missing reply may take before it is declared a failure.
- Explicit reply and deadline contracts keep their short limits:
  - `Uncertain_outcome…` (300 ms, now explicit);
  - all `MpvTests` deadline tests (unchanged file);
  - `Accepted_playlist…` (StreamStart 400 ms, unchanged).
- Cost on failure only: a genuinely missing reply in a positive test now fails after 5 s instead of 0.3–0.5 s. Passing tests are not slowed, because replies arrive in milliseconds.
- This is not a blanket increase or a retry:
  - only the Reply field in two rigs changes;
  - only in classes where no test needs a short positive-path reply deadline, or where the one test that needs it keeps it;
  - parallelism is unchanged.

## Verification plan (for the Implementer; not run by the Architect)

Run from the worktree root. Keep logs and TRX files outside the repository. Record exit codes from files, not from pipes. Bound each `dotnet test` at 300 s.

1. **Deterministic delayed peer (before and after; temporary and not committed).**
   - In the TransportControl rig handler's `loadfile` case, temporarily add `if (req.Command[2].GetString() == "append") await Task.Delay(700);` before `return false`.
   - Run `dotnet test --no-build --filter "FullyQualifiedName~TransportControlTests.Previous_within_three_seconds"`.
   - Expected with the old 500 ms: all 3 cases fail with `mpv did not reply to loadfile within 0.5s` from `Rig.Start`, which is the CI message.
   - Expected with step 1 applied: 3 pass.
   - Repeat the same probe in the AlbumPlayback handler, with the filter `Transport_is_serialized|Last_track_ending`. Expected: fail with 300 ms, pass with `Normal`.
   - Remove the probe and confirm `git diff` shows only the recipe.
   - This proves that the rig margin is the failing element for a slow peer. It does not prove that CI scheduling was the cause.
2. **Deadline tests still bite.** Run `dotnet test --no-build --filter "FullyQualifiedName~Uncertain_outcome|FullyQualifiedName~Missing_reply|FullyQualifiedName~Late_reply|FullyQualifiedName~Accepted_playlist|FullyQualifiedName~stream_deadline"`. Expected: all pass. Record the duration of `Uncertain_outcome`; it should stay at about 1 s, not about 15 s.
3. **Full suite.**
   - Run `dotnet build`, then `dotnet test --no-build`. Expected: exit 0, `Passed: 409, Failed: 0`. Run twice.
   - Run `deploy/test-lucifer-{maint,command,install,bootstrap}.sh`; each should exit 0.
4. **Optional bounded contention.** This reuses the WORK-09/WORK-17 recipe: 2 busy loops pinned with `taskset -c 0,1`, reaped by a trap, then 10 runs of `taskset -c 0,1 dotnet test --no-build --filter "FullyQualifiedName~TransportControlTests|FullyQualifiedName~AlbumPlaybackTests"`, before and after. Report the failure counts as observed. A before-run that happens to pass is not counted as evidence. This is not a stress campaign.

**Acceptance:**

- Step 1 shows fail-before and pass-after.
- Steps 2 and 3 pass.
- The diff is confined to the four regions above.
- No assertion is removed or loosened, and no test is skipped or disabled.
- Then: independent review, then delivery and one master CI run. If master CI fails again on a *different* mechanism, that is a new quality escalation, not another margin patch.

## Residual risks and decisions

- **R1, MpvTests positive tests** use a 300 ms Reply. It is the same class in principle, but they share one `Fast` with the explicit deadline tests and have never failed in CI. Splitting them means classifying about 25 tests, which is out of scope for a minimal package.
  - Recommendation: leave it.
  - If one fails in CI, the fix is the same pattern: a `Normal` used by the `Connected(peer)` and `StartAsync` callers in positive tests only.
- **R2, AlbumPlayback StreamStart 400 ms** is a real-time timer, armed by `BeginSelection`. Positive tests that assert `Loading` or an error detail long after a load could see it fire under load:
  - `First_playlist_command_rejected…` after `Ticks(30)`;
  - `Failure_after…`;
  - `Stream_error…`.

  It is not a reply deadline and has not been seen in CI. `PlayA` recovers through the later `start-file` and `file-loaded`.
  - Coordinator option D1: add `StreamStart = TimeSpan.FromSeconds(20)` to `Normal` and pass `timeouts: Fast` in `Accepted_playlist…` (line 416). That is two more one-line edits.
  - Recommendation: do not include it without evidence. Record it as known.
- **R3:** the starvation hypothesis is unproven. The recipe removes the rig's sensitivity to it either way.
- **No production defect found.** No owner decision is needed for the recipe.

## Copyable Implementer assignment

```markdown
# Assignment WORK-21 ci-fixture-v1 (role: implementer)
Task/work: install-update / WORK-21. Workspace: new worktree, new branch ruach/lucifer-ci-fixture (check unused), base <Coordinator: 17f84d8 or its evidence successor>.
Outcome: positive-path TransportControl and AlbumPlayback rigs no longer fail setup or stalled-serialization tests on a slow scheduler, while explicit deadline tests keep their short limits.
Context: docs/coordination/install-update/ci-fixture-plan.md (Diagnosis, Recipe); reports/ci-delivery.md (CI failure).
Scope: Amp.Nfc.Tests/TransportControlTests.cs (Fast field only); Amp.Nfc.Tests/AlbumPlaybackTests.cs (timeouts fields, BuildAsync default, Uncertain_outcome BuildAsync call, Applied assertion in Transport_is_serialized). Protected: Amp.Nfc/**, FakeMpvPeer.cs, MpvTests.cs, PlaySessionTests.cs, all other tests, workflows, deploy/, docs/coordination task record.
Acceptance: plan recipe steps 1-4 exactly; delayed-peer probe fails before and passes after (probe not committed); explicit deadline filter passes with Uncertain_outcome about 1 s; full dotnet test 409/409 twice; deploy test scripts exit 0; no assertion removed, loosened, skipped or disabled.
Verification: plan Verification steps 1-3 (step 4 optional, bounded, reap load with a trap); bound each dotnet test at 300 s; logs outside the repo; exit codes from files. Report any other failure as evidence; do not retry until green.
Restrictions: no push, tag, release, workflow dispatch or global/host actions; neutral commit identity, no AI trailers.
Scope changes: stop and justify in the report; the Coordinator decides.
Handoff: write docs/coordination/install-update/reports/ci-fixture.md with the ruach-handoff leading block (candidate/tested revisions), validate with --repo, commit this assignment unchanged with the report. Independent review follows.
```
