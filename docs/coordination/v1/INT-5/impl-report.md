---
task: INT-5
status: complete
outcome: "LUC-09 merged into the LUC-11 branch (no conflicts) and the packaging glue finished: unit gains the input group, the archive ships input-map.example.json, the env example lists every LUCIFER_* option, INSTALL.md documents input discovery. Combined build is green with 380 tests. Nothing run on a Pi or with a real controller."
artifacts:
  - deploy/lucifer.service
  - deploy/lucifer.env.example
  - deploy/publish.sh
  - docs/install/INSTALL.md
  - docs/coordination/v1/INT-5/impl-report.md
  - docs/coordination/v1/INT-5/impl-assignment.md
  - branch ruach/luc-11-impl
candidate_revision: d177fe674c2080fe4c528b132f8317422be9edb8
tested_revision: d177fe674c2080fe4c528b132f8317422be9edb8
source_revisions:
  - "INT-4 base: 29177ae7b083424c1de73e20442a5d14aed201a8 (combined 1d15f7c042702d578337368f7748fd2516d84f6e)"
  - "LUC-09: ruach/luc-09-impl (candidate c312d7a, report 740a1b9)"
verification:
  - "git merge --no-ff ruach/luc-09-impl: clean, no conflicts (README auto-merged; Packaging and Transport controls sections both present)"
  - "dotnet build: 0 errors, 4 warnings (existing test-project warnings)"
  - "dotnet test: 380 passed, 0 failed; no test changed"
  - "grep of LUCIFER_* names in Amp.Nfc/*.cs against deploy/lucifer.env.example: every option present (input ones and READER_TIMEOUT/ABSENCE_CONFIRM added, commented)"
  - "publish.sh linux-arm64 and linux-arm (0.0.0-test): sha256sum -c OK; unpacked config/ contains catalogue.example.json, input-map.example.json, lucifer.env.example"
  - "systemd-analyze verify on scratchpad copy with ExecStart=/bin/true: exit 0. Unit has SupplementaryGroups=dialout audio input and TimeoutStopSec=30 (default shutdown deadline 10 s)"
  - "built binary: help exit 0; bogus command exit 2; check with catalogue.example.json exit 0 (controls: disabled); check with LUCIFER_INPUT_DEVICE=/dev/null and the unpacked input-map.example.json exit 0 (controls: enabled, 3 keys)"
  - "Not run: Pi, real input device or controller, mpv, reader, DAC, Navidrome, systemd target, GitHub Actions, shellcheck (not installed)"
review: not-run
discoveries:
  - "TimeoutStopSec=30 exceeds the 10 s default but not the configurable maximum of 300 s; INSTALL.md and the unit comment say to raise it if the app deadline is raised."
  - "input-map.example.json contains // comments and the app's loader accepts it (checked via check)."
  - "README and app text say amp-nfc input inspect; the packaged executable is Amp.Nfc, and INSTALL.md uses that name."
  - "Board proposal (not applied): INT-5 is the single v1 software candidate (LUC-01..07, 09..11 packaging) with 380 tests green."
blockers: []
---

# INT-5 integration report

## Resolutions
None needed for the merge. Glue edits: unit gets `input`; env example gets commented LUCIFER_INPUT_DEVICE, LUCIFER_INPUT_MAP, LUCIFER_INPUT_REQUIRED, LUCIFER_INPUT_DEBOUNCE_MS, LUCIFER_READER_TIMEOUT_SECONDS, LUCIFER_ABSENCE_CONFIRM_MS, with no verified key codes; publish.sh ships the input map; INSTALL.md gains a Transport controls section.

## Device-pending (UNVERIFIED-ON-DEVICE)
- Userland architecture and RID choice (LUC-08); install from the archive; native dependencies; boot start.
- Controller identity, `input inspect` on the real device, real key codes, `input` group access, stable by-id path.
- Controls end to end through the service (play/pause, next, previous, repeats).
- Serial and DAC access, udev rule, ALSA identifier; maintenance lock refusal; stop with real mpv and no orphan; update and rollback.
