---
task: README-REVIEW
role: reviewer
status: complete
outcome: "README.md at ed75f70 is accurate against the source, deploy files and INSTALL.md; build, tests and the quoted check output were reproduced. No blocking findings: no false status claim, no invented device fact, no secret. Seven optional findings, including personal paths/usernames in docs/coordination that the owner should decide on before the public push."
artifacts:
  - docs/coordination/v1/README-REVIEW/review-report.md
  - docs/coordination/v1/README-REVIEW/review-assignment.md
  - branch ruach/readme-review
reviewed_revision: ed75f702fd5a2ab44b9e0a5c0a57af2c9d886296
verification:
  - "Workspace HEAD f2dca82; README.md identical to ed75f70 (f2dca82 only adds the README-1 report)"
  - "dotnet build (SDK 10.0.110, DOTNET_ROOT=<home>/.dotnet): 0 errors; --no-incremental shows 2 CA1416 warnings in Amp.Nfc.Tests (MpvTests.cs:397, PlaySessionTests.cs:43)"
  - "dotnet test Amp.Nfc.Tests: 390 passed, 0 failed, 0 skipped"
  - "Built Amp.Nfc check with NAVIDROME_URL=http://x:4533, placeholder user/password, LUCIFER_CATALOGUE_PATH=catalogue.example.json: exit 0, output matches README.md:69-76 line for line (README truncates the controls line with ...)"
  - "Amp.Nfc help exit 0; unknown command exit 2 with help on stderr; check with no env exit 1 naming the three NAVIDROME_* fields; input inspect --device rel exit 2; input inspect without device exit 2"
  - "tag write was invoked once by mistake; it failed before any tag I/O with 'Access to the port /dev/ttyUSB0 is denied' (no reader on this host). play, tag read, publish.sh and the Pi collection script were not run (no Pi, reader, mpv, DAC, server)"
  - "Every variable, default and range in README.md:96-114 compared with Options.cs and CommandLine.HelpText; commands/flags with CommandLine.cs; exit codes with Cli.cs; thresholds with ReaderWatch.cs:21-33, InputControls.cs:75, Mpv.cs:153-154,367, InputMapping.cs:15; unit/env/tmpfiles/udev/maint/publish facts with deploy/*; install summary with INSTALL.md sections 1-3, 6, 7"
  - "All 14 relative links and paths in README.md exist; anchor #hardware-and-software-inventory matches its heading"
  - "Inventory 'planned' values traced to BLACKBOARD.md:49, blackboards/ACCEPTANCE.md:11-18, blackboards/DEVICE.md:39, docs/reviews/2026-10-17-v1-review.md:17, TagCommands.cs:70, deploy/99-lucifer-nfc.rules.example"
  - "Privacy scan (grep for <home-root>/, usernames, emails, tokens, keys, credential URLs, /tmp and /opt paths) over README.md and docs/coordination/**"
review:
  - "OPTIONAL F1 docs/coordination/**: local username and home path <home>/.dotnet in 25 files (list in report body); not a secret, owner decision before public push"
  - "OPTIONAL F2 docs/coordination/v1/task-record.md:76: GitHub account rothzeta and git@github.com:rothzeta/lucifer.git; public anyway once pushed, owner awareness only"
  - "OPTIONAL F3 docs/coordination/v1/task-record.md:5-6,33-67 and assignment.md:15-22: machine layout <dev>/... and private temp dirs <tmp>/ruach-herdr-*; not secrets"
  - "OPTIONAL F4 README.md:229: 'Exit code 2 means invalid arguments' is incomplete; play/tag/input inspect also exit 2 on configuration errors (Cli.cs:46-52), and play exits 1 on catalogue/input-map errors"
  - "OPTIONAL F5 README.md:13: 'The build is clean' while a full build shows 2 CA1416 warnings in the test project; and 'local branch' becomes stale once the branch is pushed"
  - "OPTIONAL F6 README.md:57-66: newcomer cannot run 'Amp.Nfc ...' as shown: binary location (Amp.Nfc/bin/Debug/net10.0/Amp.Nfc) and the env needed to get the quoted check output are not stated"
  - "OPTIONAL F7 README.md:209-224: collection script and tag read print USB/tag serial numbers that the README asks to paste into a public file; not secrets, mention it"
discoveries:
  - "Pushing ruach/lucifer-v1-orchestrator publishes docs/coordination/** with the local username <user> in home paths (F1); this assignment file itself contains it and was committed unchanged as required."
  - "README-1's own report already records the 2 CA1416 warnings; the README text 'build is clean' predates/ignores that (F5)."
blockers: []
---

# README-REVIEW report

Reviewed: `README.md` at `ed75f702fd5a2ab44b9e0a5c0a57af2c9d886296` (diff `a69401d..ed75f70 -- README.md`), in workspace `ruach/readme-review` at `f2dca82`. Scope: documentation only. Nothing in source or README was modified.

**Blocking findings remaining: none.**

## Results per check

1. **Options, defaults, commands, flags, exit codes, paths, links.** All 17 variables, their defaults and ranges match `Options.cs` and the help text. Commands and flags match `CommandLine.cs`. Exit codes: help 0, invalid command 2, `check` config error 1, which matches `Cli.cs:11-13,50` (see F4 for the incomplete troubleshooting line). Thresholds (20 faults/10 s, 60 s probe, 3 failures, 8 queued controls, 3 s previous-restart, code 0-767) match the source. The unit facts (`SupplementaryGroups=dialout audio input`, `KillMode=mixed`, `TimeoutStopSec=30`, `Restart=on-failure`, `/run/lucifer`), `lucifer-maint` env loading, the `publish.sh` arguments and archive layout, and the INSTALL.md step order all match. Links all resolve. The quoted `check` output was reproduced exactly.
2. **Inventory.** Every *Planned/assumed* value is traced to a repository document and labelled planned, unverified, discussed or "verify". Every *Actual* cell is `_pending (LUC-08)_`. There are no hostnames, IPs, versions, serials or credentials. The udev IDs `1a86:7523` are explicitly marked "assumed, not read from the adapter". The collection script only reads (`cat`, `uname`, `dpkg --print-architecture`, `mpv --version`, `lsusb`, `ls`, `udevadm info`, `aplay -l`, `mpv --audio-device=help`, `id`). It does not read the env file or print credentials (but see F7).
3. **Status.** The README says v1 software is delivered and v1 is not complete. It says device acceptance is open, with nothing run on a Pi and LUC-08/LUC-12 pending, and that automated passes are not device passes. This is consistent with `blackboards/ACCEPTANCE.md`, which records automated passes with fakes and device parts as open or not run. No device pass is asserted. 390 tests was confirmed.
4. **Secrets / personal data.** README.md has no secrets, personal paths or emails. Its `$HOME/.dotnet` example is generic. The only credential-like values are the placeholders `change-me` and `http://x:4533`. `docs/coordination/**` contains no secrets, tokens or authenticated URLs, but it does contain personal machine details (F1-F3).
5. **Setup for a newcomer.** The order is workable: requirements → build/test → commands → configuration → catalogue → controls → packaging (delegated to INSTALL.md, whose section 3 order is sound). There are small gaps in F6.

## Findings (all OPTIONAL)

### F1. Local username and home path in coordination records
- Location: `<home>` appears at these lines:
  - `docs/coordination/v1/assignment.md:42`
  - `task-record.md:19`
  - `LUC-01/impl-1-assignment.md:8`
  - `LUC-02/impl-1-assignment.md:7`, `LUC-03/impl-1-assignment.md:7`, `LUC-04/impl-1-assignment.md:7`, `LUC-05/impl-1-assignment.md:7`
  - `LUC-06/impl-1-assignment.md:8`
  - `LUC-07/impl-1-assignment.md:7`
  - `LUC-09/impl-1-assignment.md:11`
  - `LUC-10/impl-1-assignment.md:7`
  - `LUC-11/impl-1-assignment.md:7`
  - `INT-1/impl-assignment.md:5`, `INT-1/impl-report.md:23`
  - `INT-2/impl-assignment.md:6`
  - `INT-3/impl-assignment.md:6`
  - `INT-4/impl-assignment.md:4`
  - `INT-5/impl-assignment.md:7`
  - `FIX-1/impl-assignment.md:13`
  - `REVIEW-1/review-assignment.md:7`
  - `REVIEW-2/review-assignment.md:6`
  - `DELIVERY-1/impl-assignment.md:10`
  - `README-1/impl-assignment.md:6`, `README-1/impl-report.md:13`
  - `README-REVIEW/review-assignment.md:4`
  
  All paths are under `docs/coordination/v1/`. This report also contains the path, because it quotes the verification commands.
- Problem: the local account name `<user>` would be published with the branch.
- Why it matters: it is personal or host information in a public repository. It is not a secret.
- Suggested direction: the owner decides whether to accept it. If not, rewrite the paths to `$HOME/.dotnet` in a separate docs commit before pushing. That needs owner authority, because historical reports would change.

### F2. GitHub account in the task record
- Location: `docs/coordination/v1/task-record.md:76` (`git@github.com:rothzeta/lucifer.git`, the account `rothzeta`).
- It is the public repository's own owner and remote, so it adds no exposure once pushed. Listed for completeness. No change needed unless the owner prefers.

### F3. Machine layout and temp directories
- Location: `task-record.md:5-6,33,35,40,46,50,52,56,67` (`<dev>/...` worktrees and `<tmp>/ruach-herdr-*`). `assignment.md:15-22` has `<dev>/...` paths.
- Not secrets. Low value to the public. Same owner decision as F1.

### F4. Troubleshooting overstates the meaning of exit code 2
- Location: `README.md:229`.
- Problem: it says exit code 2 means invalid arguments. But `Cli.cs:46-52` also returns 2 (`Usage`) for configuration errors in `play`, `tag` and `input inspect`. A catalogue or input-map error in `play` returns 1 (`Cli.cs:76-95`), and the README frames exit 1 only as an mpv or reader fault.
- Why it matters: under systemd a bad env file shows as status 2, and the user is pointed to "arguments" rather than configuration. The line is incomplete rather than wrong about the cases it names.
- Evidence: reproduced `check` with no env, which returned 1 (check is the exception). The other modes follow the code path above.
- Direction: "2 = invalid arguments, or invalid configuration (except `check`, which exits 1); 1 = catalogue/input-map error at start-up or a runtime mpv/reader fault".

### F5. Status wording
- Location: `README.md:13`.
- Problem 1: "The build is clean". `dotnet build --no-incremental` reports 0 errors and 2 CA1416 warnings (`Amp.Nfc.Tests/MpvTests.cs:397`, `PlaySessionTests.cs:43`). README-1's own report records them.
- Problem 2: "local branch `ruach/lucifer-v1-orchestrator`" will be stale once that branch is pushed as authorized by AUTH-04.
- Direction: "builds with no errors (two test-project analyzer warnings)". Drop "local", or say "branch".

### F6. Newcomer gap in running the built program
- Location: `README.md:57-66`.
- Problem 1: the examples call `Amp.Nfc ...`, but nothing says the binary is at `Amp.Nfc/bin/Debug/net10.0/Amp.Nfc` after `dotnet build`.
- Problem 2: the quoted `check` output needs `LUCIFER_CATALOGUE_PATH=catalogue.example.json` plus the `NAVIDROME_*` variables. Without them, `check` fails on the `catalogue.json` default.
- Evidence: the output was reproduced only with those variables set.
- Direction: add the binary path, or reuse the `dotnet run` form, and show a one-line env example for `check`.

### F7. Device identifiers headed for a public file
- Location: `README.md:217` (`udevadm ... | grep ... serial`), `README.md:203` (`ls -l /dev/serial/by-id/`), `README.md:204` (`tag read` prints the tag UID), `README.md:224` (paste into `blackboards/ACCEPTANCE.md`).
- Problem: the line warns about credentials only. Adapter serial numbers and tag UIDs would also be published.
- Why it matters: these are low-sensitivity device identifiers, not secrets.
- Direction: add "serial numbers and UIDs may be shortened or omitted".

## Not verified
- Behaviour on a Pi, the real reader, mpv, DAC, Navidrome or systemd. This includes the collection script itself, `publish.sh`, `play`, `tag read`, `tag write` beyond its argument and permission path, and every INSTALL.md step.
- Policy statements in README.md:158-183 were checked only at the constants above. The full control and supervisor behaviour was not re-derived.
