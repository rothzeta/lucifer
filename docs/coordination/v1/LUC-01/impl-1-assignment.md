# Assignment LUC-01/impl-1 (role: implementer)
Task id: LUC-01-impl. Workspace: this worktree, branch ruach/luc-01-impl, based on the orchestrator branch commit that contains this file.

Outcome: LUC-01 "Make one runnable command surface" as specified in `blackboards/TAGS.md` (read it, plus `BLACKBOARD.md` "First implementation change" and `docs/reviews/2026-10-17-v1-review.md`). One executable (`Amp.Nfc/Amp.Nfc.csproj`) with `play`, `tag read`, `tag write --release <uuid>`, `check` and help; validated options model; reader-ownership enforcement; one test project.
Binding decisions: D01, D04, D10 in BLACKBOARD.md (C#, PN532 over USB-TTL, one app project + one test project, keep Navidrome credentials from environment).

Scope: `Amp.Nfc/**`, root `Program.cs` (fold into the application, remove duplicate), a new test project (e.g. `Amp.Nfc.Tests/`), solution file if needed, README usage section. Do NOT edit `BLACKBOARD.md`, `blackboards/**`, `docs/coordination/v1/task-record.md`, or `docs/reviews/**`; propose board updates in the report. Do not start LUC-02+ work (no codec/catalogue/playback fixes).
Environment: dotnet at <home>/.dotnet/dotnet (SDK 10.0.110). No mpv, reader or DAC exist here; use boundary substitutes.

Acceptance (A01, A02 automated parts):
- Application builds; all modes appear in help.
- Help, unknown command, missing write argument finish promptly, nonzero for invalid, before any reader access (test through the built process boundary).
- Writer modes require no Navidrome credentials. Options model validates blank values and malformed server URLs, reports errors by field, never prints credentials or stream URLs.
- A second reader owner is refused with a useful error (lock/ownership boundary test).
- Explicit maintenance workflow documented (stop service, write, start service).

Verification: write failing tests first; run `dotnet build` and `dotnet test`; run the built binary for the command checks. Do not weaken tests to pass; report conflicts. Keep disposable files outside the repository. No network, no pushes.
Scope changes: stop and report if work outside scope seems necessary.

Handoff: write `docs/coordination/v1/LUC-01/impl-1-report.md` with the ruach-handoff leading block (list tested revision, commands, results, and A01/A02 coverage notes), run `bun <ruach>/skills/ruach-handoff/scripts/validate.ts <report> --repo .`, commit code, this assignment unchanged, and the report. Report the final commit SHA.
