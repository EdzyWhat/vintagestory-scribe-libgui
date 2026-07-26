## 1. One-command verify/restage script

- [x] 1.1 Add a `build/` script that runs, in order: build the mod → `dotnet test tests/Core.Tests` → `dotnet test tests/Integration.Tests --filter "FullyQualifiedName!~FixtureBuilders"` → restage (reuse `build/restage.sh`).
- [x] 1.2 Make each stage fail fast: on any failure the script exits non-zero, names the failing stage, and does NOT restage.
- [x] 1.3 Document the script in `README.md` as the one-command local verification loop.

## 2. Opt-in pre-push hook

- [x] 2.1 Add a version-controlled `pre-push` hook (e.g. under `build/hooks/`) that runs the Core tests and the Atlas suite, and blocks the push (non-zero exit) on failure, naming the failing stage.
- [x] 2.2 Have the hook only gate when the push updates `main` (let WIP branches through), and reuse the task-1 script rather than duplicating the stage list.
- [x] 2.3 Add a `build/` installer script that copies/symlinks the hook into `.git/hooks/` (hooks aren't cloned), and document install + the `git push --no-verify` escape hatch in `README.md`.
- [x] 2.4 Install and dogfood the hook on this machine; confirm a deliberately broken Atlas scenario blocks the push and `--no-verify` bypasses it. (Dogfooded: main-push runs the full gate (build+Core+Atlas 23/23, ~60s) and allows; a deliberately-failing test refuses the push naming the stage; non-main branches skip the gate; `--no-verify` is git's built-in pre-push bypass.)

## 3. Core coverage in CI

- [x] 3.1 Enable coverage on the existing Core job in `.github/workflows/ci.yml` using `coverlet.collector` (`--collect:"XPlat Code Coverage"` or equivalent). Do NOT add an integration job.
- [x] 3.2 Publish the coverage output as a build artifact or run summary. (Both: ReportGenerator renders the cobertura into an HTML report uploaded as the `core-coverage` artifact AND a MarkdownSummaryGithub table written to `$GITHUB_STEP_SUMMARY` so coverage is visible in the run itself. Verified the full chain locally.)

## 4. Dev-diagnosis toolkit

- [x] 4.1 Add a documented `build/` log helper that tails + filters `server-main.txt` (and optionally `client-main.txt`) for `[scribe]` lines and asset/mod-load errors, resolving the log path from the standard VintagestoryData location. (`build/scribe-log.sh`; resolves `*-main.log`/legacy `*-main.txt`; filter verified against the real server log.)
- [x] 4.2 Document a "dev world" launch profile (flat creative test world, developer mode + extended debug info + error reporter preset) in `README.md` or a `build/` doc. (VSAPI-NOTES.md "Dev-diagnosis toolkit"; README points at it.)
- [x] 4.3 Assess whether C# Hot Reload works for this compiled-mod setup on macOS, and record the result plus the fast-iterate reload commands (`.reload textures/shapes/shaders/lang`, `CTRL+F1`) in `VSAPI-NOTES.md`. (Verdict: Hot Reload does NOT work — the game's `Vintagestory.runtimeconfig.json` sets `MetadataUpdater.IsSupported: false`, which disables EnC/Hot Reload at the runtime level. Recorded with the `.reload`/`CTRL+F1` commands.)

## 5. Atlas reference material & next-adoption notes

- [x] 5.1 Verify `reference/atlas` + `reference/atlas-wiki` are present and gitignored (clones + `.gitignore` entries already added). (Both dirs present; `git check-ignore` confirms both ignored; `CI-Recipes.md` present.)
- [x] 5.2 Record in `VSAPI-NOTES.md` (or a design note) the not-yet-used Atlas 0.11.0 capabilities worth adopting next — `ExecuteCommand` result assertions and `atlas diff` differential regression — with a one-line rationale each. (VSAPI-NOTES.md "Atlas integration harness — next-adoption notes".)

## 6. Correct docs & record the deferred cloud-CI option

- [x] 6.1 Update `README.md`'s CI note so it does not claim CI can only test Core because the game DLL isn't redistributable; state accurately that CI runs Core (+ coverage) and the Atlas suite is gated locally via the pre-push hook.
- [x] 6.2 Lightly correct the same framing in `openspec/config.yaml` and `CLAUDE.md` so the "cloud can't test the game" belief doesn't silently foreclose the deferred cloud option.
- [x] 6.3 Record in `design.md`'s Open Questions (already drafted) the cloud-CI revisit triggers — a second contributor, or 1.22.x version-matrix regressions — and confirm the note points at `reference/atlas-wiki/CI-Recipes.md` and the headless-DLL probe. (Confirmed present in design.md Open Questions: both triggers, the `CI-Recipes.md` pointer, and the guarded-`Mod.csproj` `cairo-sharp`/`SkiaSharp`/`OpenTK` probe.)

## 7. Verify

- [x] 7.1 Run the one-command script end-to-end on a clean checkout state and confirm all stages pass and restage happens only on success. (build clean, Core 64/64, Atlas 23/23, restage 13 files — restage ran only after all suites passed, ~60s.)
- [x] 7.2 Run `openspec validate improve-testing-and-diagnosis --strict` and fix any issues. ("Change 'improve-testing-and-diagnosis' is valid".)
