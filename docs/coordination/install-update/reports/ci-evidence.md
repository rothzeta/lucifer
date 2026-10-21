task: WORK-24-ci-evidence-v1
status: complete
outcome: "The Last_track_ending probe failure is valid evidence of a real PlayA sequencing defect, not an invalid acceptance condition. PlayA returns when the fake peer has queued three entries, which is before the apply has finished, so the apply's trailing set_property pause request can arrive after the test has sampled its request count. Recommended now: one test-only edit to PlayA (wait for Controller.WhenIdleAsync, the apply-completion signal, then assert the queue), with deterministic before/after probe verification."
artifacts:
  - docs/coordination/install-update/reports/ci-evidence.md
  - docs/coordination/install-update/assignments/ci-evidence-v1.md
verification:
  - "Read-only source analysis at 3656101114f7b0be5ac4bedbe9af79b5f7268663 (the source tree is identical at b449b08): AlbumPlaybackTests.cs BuildAsync handler, PlayA, Last_track_ending and Transport_is_serialized; FakeMpvPeer.cs Serve; Mpv.cs ReplaceQueueAsync; Mpv.Status.cs entry filtering; SelectionController.cs WhenIdleAsync and PrepareAndCommitAsync; PlayerAlbumApplier.cs CommitAsync"
  - "Not run: no build, test, probe or runtime check. The mechanism is derived from code and matches the recorded probe result (Expected 5, Actual 6), but no experiment has confirmed it"
review: not-run
discoveries:
  - "The rig handler adds a loadfile to rig.Queue before the peer sends its reply. So Queue.Length == 3 means the third append was received, not that it was answered. The pause request comes only after that reply is processed"
  - "The race window exists without the probe: one reply round trip plus one send, which depend on the same thread-pool scheduling as the CI failure. The probe widens the window; it does not create it"
  - "With the probe, PlayA's start-file uses NextEntryId - 2 = 0 (stale) and is filtered out. Last_track_ending's 'last' may be 2 rather than 3. Ended can then be reached on a stale entry. This is inference: the precondition 'A applied, first track playing' did not hold"
  - "The original plan's prediction (Last_track_ending passes with Normal) was wrong because the reply margin does not affect this ordering"
blockers: []
baseline: b449b0865afe29745fa56f65dd45f02b7cde544c
candidate_revision: 3656101114f7b0be5ac4bedbe9af79b5f7268663

# CI evidence disposition: Last_track_ending delayed probe (WORK-24)

Inspected revision: product `3656101` (on report base `b449b08`; the only changes between them are docs). This is design and evidence analysis only. I ran nothing, so none of this is acceptance or runtime verification.

## Disposition

**The probe is relevant evidence, and the failure shows a defect that should be fixed now.** Do not reclassify it as an invalid acceptance condition.

`Last_track_ending_stops_and_issues_no_repeat` samples `before = Requests.Count` straight after `PlayA`. That only works if `PlayA` returns after the apply has finished. The test is right to rely on this: the helper's comment says it "wait[s] for the observable queue instead of assuming it is done". The current wait does not deliver that postcondition.

## Mechanism (in observable events and state)

1. `Mpv.ReplaceQueueAsync` (`Amp.Nfc/Mpv.cs:104-112`) sends `loadfile replace`, then `loadfile append` twice, then `set_property pause false`. Each request is sent only after the previous reply has been processed.
2. The rig handler (`AlbumPlaybackTests.cs`, the `loadfile` case in `BuildAsync`) adds the URL to `rig.Queue` and returns `false`. Only then does `FakeMpvPeer.Serve` increment `NextEntryId` and write the reply.
3. `PlayA` polls `rig.QueueIds` until it has three entries. That condition becomes true when the third append has been **received**, which is before it has been answered and before `pause false` has been sent.
4. `PlayA` then sends `start-file` and `file-loaded`. `file-loaded` has no entry id, so it is accepted unfiltered and the status becomes `Playing`. The test then samples `before`. This gives 5 requests: the two connect-handshake requests (`get_property mpv-version` and `observe_property pause`, `Mpv.Launch.cs:197-198`), replace, and two appends. The pending `pause false` arrives later, so the count is 6. That matches the recorded failure (Expected 5, Actual 6). A settled `PlayA` gives `before = 6`.

In normal runs the window is one reply round trip plus one send, so it is usually closed before the 10 ms poll fires. Its width depends on the same thread-pool scheduling the CI diagnosis blames. The 700 ms probe only widens it. A passing normal run does not show that the window is absent.

## Recommended fix (one region, test-only)

`Amp.Nfc.Tests/AlbumPlaybackTests.cs`, `PlayA` only. Replace the `Eventually` queue wait and its comment with an apply-completion wait, followed by a direct assertion:

```csharp
await rig.Present("04AA", ReleaseA);
// Present waits only a bounded 100 ms; wait for the apply itself to finish (its last request is
// pause=false) so callers observe a settled queue and request log.
await rig.Controller.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
Assert.Equal(["tA1", "tA2", "tA3"], rig.QueueIds);
```

Leave the rest of `PlayA` unchanged.

- **Semantics.** `SelectionController.WhenIdleAsync` (`SelectionController.cs:80-90`) completes only when every `PrepareAndCommitAsync` has finished. `PrepareAndCommitAsync` awaits `CommitAsync`, then `ApplyAlbumAsync`, which includes the `pause false` reply. So the trigger is an observable completion event, not elapsed time.
  - The 10 s `WaitAsync` only guards against a hang. It replaces the existing 10 s `Eventually` bound, so no new threshold is introduced.
  - `Transport_is_serialized…` already uses `WhenIdleAsync()` this way.
- **Side effects.** Once the apply has finished, `NextEntryId` is 3. `start-file` then names entry 1, the real first track, and `last` in `Last_track_ending` is 3. Both values are now correct by construction.
- **Not changed.** No assertion in any test, no other helper, the production code, `FakeMpvPeer`, or the timeouts.
- **Callers.** `PlayA` has 9 callers. They all gain a stronger precondition and none loses one. If an apply ever fails, `WhenIdleAsync` still completes, and the queue assertion then fails with a clear message, as it does today.

## Deterministic verification (for the Implementer)

Use the same temporary probe as in WORK-21. Put `if (req.Command[2].GetString() == "append") await Task.Delay(700);` before `return false` in the AlbumPlayback `loadfile` case. Do not commit it. Do a fresh build before each run.

1. **Before** (`3656101` plus the probe), filter `Last_track_ending|Transport_is_serialized`. Expected: `Last_track_ending` fails with Expected 5, Actual 6, and `Transport_is_serialized` passes. This reproduces the v2 evidence on the same build procedure.
2. **After** (fix plus the probe), same filter. Expected: both pass.
3. **Order check** (optional, temporary, not committed). In `Last_track_ending`, assert that `before` already includes a `set_property pause false` request. Expected: it fails before the fix and passes after. This tests the mechanism itself, not only the count.
4. Remove the probe. `git diff` must show only the `PlayA` region.
5. Run the deadline filter (unchanged from WORK-21), then the full suite twice (409/409), then the four `deploy/test-lucifer-*.sh` scripts.

Acceptance: step 1 fails as described, step 2 passes, and steps 4 and 5 are clean. No assertion is removed or loosened, and no time threshold is added.

## Residual risk and limits

- The mechanism is inferred from code and matches the recorded counts. Step 1 together with step 2 or step 3 confirms it. If step 2 still fails, stop and report: the cause would then be something else, and this disposition would be wrong.
- I did not audit other tests that sample state right after `Present` without `PlayA`. That is outside this narrow scope.
- The `MpvTests` reply residual (R1 in the plan) is unchanged.

## Handoff validation

The validator result is returned in the terminal handoff.
