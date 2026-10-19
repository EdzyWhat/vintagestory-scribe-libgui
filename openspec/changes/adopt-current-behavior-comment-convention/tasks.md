## 1. Convention and verification tooling

- [x] 1.1 Reword the `CLAUDE.md` "clear, well-explained solutions" guardrail and add the commenting rule (current behavior, no history markers, no restating code); verify by reading the edited guardrail section
- [x] 1.2 Write a verification script outside `.github/` and `Directory.Build.props` (for example `build/verify-comment-only.sh`) that builds a base ref and the working tree to separate output dirs, runs `ilspycmd -il` on `Scribe.dll` and `Scribe.Core.dll`, and exits non-zero on any IL difference; verify it passes on a no-op and fails on a deliberate one-token code edit
- [x] 1.3 Confirm the trial edit to `src/Mod/ScribeReadContent.cs` passes the script and is kept as batch zero; verify script exit 0

## 2. Batch plan

- [x] 2.1 Generate the batch list (about 15-20 batches of ~2.5k lines for `src/Mod`, `HudScribePins.cs` alone, `src/Core` as one, `tests/` as one), keeping related partial classes together; verify every tracked `.cs` file under `src/` and `tests/` appears in exactly one batch
- [x] 2.2 Write the agent brief containing the trim rubric from design.md and the report format (<=15 lines); verify it by running it on one small file and checking the report and IL result

## 3. Sweep

- [x] 3.1 Write the Workflow script: fan out one agent per batch in its own worktree, each runs the verification script against its base commit and reverts any IL-mismatched file, then returns the short report; verify with a dry run on two small batches
- [x] 3.2 Run the sweep over all `src/Mod` batches; verify every report states IL-identical and lists any reverted files
- [x] 3.3 Run the `src/Core` batch and the `tests/` batch; verify IL-identical for `Scribe.Core.dll` and that the test project still builds

## 4. Review and merge

- [x] 4.1 Review the flagged uncertain comments and stale-comment corrections from all reports; verify each is accepted, adjusted, or reverted
- [ ] 4.2 Spot-check at least one file per batch (prioritizing `HudScribePins.cs` and `ScribeDialogBase.*`) by comparing comment claims against the code; verify no live constraint was dropped
- [x] 4.3 Merge the batch branches; verify no conflicts and run the verification script against the original base commit with exit 0
- [x] 4.4 Run `dotnet test tests/Core.Tests` on the merged result; verify all tests pass

## 5. Notes and wrap-up

- [ ] 5.1 Add the relocated API/engine facts collected in the reports to `VSAPI-NOTES.md`; verify each fact appears once and any retained code comment is a one-line pointer
- [ ] 5.2 Measure the result and record it in the archive notes: history-marker grep count in comments (target 0 outside deliberate exceptions) and before/after comment and total line counts; verify the numbers are reported
- [ ] 5.3 Decide whether to add a history-marker lint to `build/verify.sh` as a follow-up change; verify the decision is recorded

## 6. Second pass: restating comments

- [ ] 6.1 Pilot the aggressive rubric on three batches (`hud-pins`, one `mod-*`, one `tests-*`); verify each is IL-identical and inspect the cuts for lost constraints before proceeding
- [ ] 6.2 Run the aggressive pass on the remaining batches; verify every report states IL-identical with no reverted files
- [ ] 6.3 Resolve reviewer concerns from the second pass (restore any lost live constraint); verify the concerns list is empty or each item is dispositioned
- [ ] 6.4 Merge the second-pass branches and run the verification script against the pre-sweep commit plus `dotnet test tests/Core.Tests`; verify IL identical and all tests pass
