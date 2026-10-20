task: WORK-20-ci-plan
status: complete
outcome: "Diagnosed the remaining CI failure as a test-rig reply margin (0.5 s normal-path Reply in TransportControl setup), not a behaviour timing contract or product regression; bounded test-only recipe covers it and the same-class AlbumPlayback 300 ms default (WORK-09 F2), keeping explicit deadline tests at their short limits. Ready for one implementation/review cycle."
artifacts:
  - docs/coordination/install-update/ci-fixture-plan.md
  - docs/coordination/install-update/reports/ci-plan.md
  - docs/coordination/install-update/assignments/ci-plan-v1.md
  - https://github.com/rothzeta/lucifer/actions/runs/39485124360
  - "retained WORK-09 plan 41ab1c3:docs/coordination/install-update/release-recovery-plan.md (F2)"
verification:
  - "Read-only inspection of TransportControlTests.cs, AlbumPlaybackTests.cs, MpvTests.cs, PlaySessionTests.cs timeouts, FakeMpvPeer.cs, Mpv.cs, Mpv.Ipc.cs, Mpv.Status.cs, Mpv.Launch.cs, MpvTypes.cs, ci.yml at base 17f84d8"
  - "git diff af61d1a e8181b8 -- Amp.Nfc Amp.Nfc.Tests .github: reply path (Mpv.Ipc.cs) unchanged; only Dispose, launch ownership check, PlayA and one new MpvTests test changed"
  - "Tests, builds, load runs, prototypes, GitHub queries: not-run (failure evidence taken from reports/ci-delivery.md)"
review: not-run
discoveries:
  - "Failure is in Rig.Start before the behaviour under test: append loadfile reply missed Fast.Reply 500 ms -> AlbumIncomplete by product policy; production Reply default is 5 s; no TransportControl test needs a Connect/Send/Reply deadline"
  - "WORK-09 Package A F2 margin change (AlbumPlayback Transport_is_serialized, 5 s reply) never landed; AlbumPlayback BuildAsync default Reply is still 300 ms for all positive tests; the WORK-17 PlayA 10 s wait cannot recover from a reply deadline that already cleared the queue"
  - "TransportControl replacement-stall tests have the F2 shape with 350-400 ms margin; covered by the recipe"
  - "Thread-pool scheduling delay under parallel test classes is the likely mechanism (hypothesis, unproven); the new concurrent-dispose test adds blocking pool work and may raise its probability (unverified, not a defect)"
  - "Residuals left by design: MpvTests positive tests share 300 ms Fast with deadline tests (R1); AlbumPlayback StreamStart 400 ms real-time timer in some positive asserts (R2, optional D1 in plan)"
blockers: []
baseline: 17f84d806f0e91e4d260574bb4bb1648cd59fcec
delivered_revision: e8181b8e1effc3cf003c11f89e62054f8fbe64c5

# CI fixture plan report (WORK-20)

Plan: [../ci-fixture-plan.md](../ci-fixture-plan.md). It contains the diagnosis, a table of related deadlines, the recipe, the verification commands, the residuals and a copyable Implementer assignment.

## Summary

- **Classification.** The failure is a test-setup scheduling margin. The three-second previous-track boundary is driven by the fake `time-pos` and is untouched. No application regression was found: the IPC reply path is unchanged from the passing `af61d1a`, and production uses a 5 s reply deadline.
- **Recipe.** The change is test-only and touches two files.
  - `TransportControlTests` `Fast.Reply` goes from 500 ms to 5 s.
  - `AlbumPlaybackTests` gets a new `Normal = Fast with { Reply = 5 s }`, used as the `BuildAsync` default.
  - `Uncertain_outcome…` explicitly keeps `Fast` (300 ms).
  - `Transport_is_serialized…` gains `Assert.Equal(TransportOutcome.Applied, await transport)`.
  - Connect, Send and StreamStart stay unchanged, as do every assertion, `MpvTests`, `PlaySessionTests` and production code.
- **Evidence plan.**
  - A temporary delayed-peer probe (700 ms on appends, not committed) fails before the change with the CI message and passes after it.
  - Focused tests on the explicit deadlines.
  - The full suite twice (409/409) and the deploy scripts.
  - An optional bounded 2-core contention comparison.
- **Limits.**
  - The scheduling mechanism is a hypothesis.
  - The probe proves that the rig margin is the failing element for a slow peer, not what CI did.
  - One master CI run after delivery is a gate, not proof of stability.
  - Allow at most one implementation and review cycle. After that, any failure with a new mechanism goes to escalation.

## Decisions

There is no owner decision for the recipe. Coordinator option D1 (R2, StreamStart) is not recommended without evidence.

## Handoff validation

The validator was run with `--repo` on this file. Its result is reported in the terminal handoff: it cannot be embedded here without circularity.
