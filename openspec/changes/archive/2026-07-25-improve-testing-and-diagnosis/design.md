## Context

Scribe has three verification layers: ~70 Core unit tests (cloud CI), ~23 Atlas integration
scenarios (local-only), and the manual playtest loop (`TESTING.md` + `vs-playtest-checklist`
+ `what-to-test`). Only the Core job gates `main`. The Atlas suite — the layer that exercises
real server behavior — runs by hand, so a regression in the single-editor lock or the
persistence round-trip can reach `main` with CI green.

The obvious fix, "gate Atlas in cloud CI," was explored and **deferred** (see below). For a
solo, single-install project the machinery it demands (CDN server download, run-time staging
of the `gui`/`configlib` mod deps the empty runner lacks, a guarded `Mod.csproj` probe for
client-rendering DLLs) buys little that a local hook doesn't — this machine already has the
install, the mod zips, and the DLLs. So this change invests in **local-machine refinement**.

Current wiring worth knowing:
- `tests/Integration.Tests` needs `VINTAGE_STORY` set and the `gui`/`configlib` deps staged;
  locally these come from the install and the Mods folder, and `src/Mod/lib/README.md`
  documents extracting the DLLs. The documented run is
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName!~FixtureBuilders"`.
- Diagnosis today: a `[scribe]`-prefixed Notification-level server trace, a JSON-toggled
  inspect overlay (ships in Release, macOS-safe), and `#if DEBUG` VSImGui sliders (dead on
  Apple Silicon — OpenGL 4.1 vs 4.3). ConfigLib's panel also freezes on Mac. So interactive
  GUI tuning is unavailable on this machine; the iterate loop is the weak link.

## Goals / Non-Goals

**Goals:**
- Make running the Core + Atlas suites automatic at push time, using only what is already on
  this machine (an opt-in `pre-push` hook).
- Collapse the build/test/restage loop into one command.
- Stand up a documented local diagnosis toolkit (log helper, dev-world profile, reload notes).
- Add a Core coverage signal (the one cheap cloud win — Core already runs in CI).
- Vendor Atlas references and record next-adoption candidates.
- Document, not lose, the deferred cloud-CI option and its trigger condition.

**Non-Goals:**
- Running the Atlas suite on cloud runners now; a self-hosted runner.
- Building/publishing the mod zip in CI (stays local via `release.yml`).
- Any change to `src/Core`, runtime mod behavior, `Directory.Build.props`, or `Mod.csproj`.

## Decisions

**D1 — Local pre-push hook, not cloud CI, as the Atlas gate.** On this machine the install,
mod zips, and DLLs already exist, so the suite that passes by hand runs automatically with
zero download/staging machinery. This captures the ~90% of "gate Atlas" value that actually
applies to a solo dev — "can't forget to run it" — at a tiny fraction of the effort.
- *Alternative — cloud CI (deferred):* technically feasible (Atlas's CI recipe downloads the
  VS server from the CDN; nothing redistributable is committed), but for a solo dev its
  unique benefits — un-bypassable gate, and a version matrix across installs you don't have —
  are low-value today and costly to stand up. Revisit when a 1.22.x patch matrix would catch
  regressions this single install can't. Recorded in Open Questions so the option isn't lost.

**D2 — The hook is opt-in and bypassable.** It is installed by an explicit `build/` script
(git hooks aren't cloned), and `git push --no-verify` remains the documented escape hatch.
This is honest about a local hook's limits (only guards this machine, bypassable) while still
being worth far more than it costs. If a contributor ever joins, that is the trigger to
promote the gate to cloud CI (ties back to D1's revisit condition).

**D3 — One script backs both the hook and manual use.** A single `build/` verify/restage
entry point (build → Core tests → Atlas suite with the `FixtureBuilders` filter → restage) is
what the hook calls and what a developer runs by hand, so there is one source of truth for the
loop rather than steps duplicated between a hook and the README.

**D4 — Core coverage is the only CI change.** Enable `coverlet.collector` (already
referenced) on the existing Core job and publish the result. This needs no game install, so
it is the one cloud improvement that fits the current constraints cleanly. The `integration`
job is explicitly not added.

**D5 — Diagnosis toolkit is documentation + a thin script.** The log helper is a small
`build/` shell script tailing/filtering `server-main.txt`/`client-main.txt` for `[scribe]`
and asset errors. The dev-world profile (developer mode + extended debug info + error
reporter) and the reload/Hot-Reload viability findings are captured in `VSAPI-NOTES.md` /
README, matching the repo's "write down what we learned the hard way" discipline.
`reference/atlas` + `reference/atlas-wiki` are already cloned and gitignored.

## Risks / Trade-offs

- **[A local hook only guards this machine and is bypassable] → accepted, documented.** It is
  the right trade for a solo project; the promote-to-cloud trigger (a contributor, or a
  version matrix need) is written down (D1/D2) so the ceiling is known.
- **[The one-command script hides which stage failed] → D3 requires it to name the failing
  stage and exit non-zero before restaging**, so a broken build is never silently staged.
- **[Hot Reload may not work for this compiled-mod setup on macOS] → the task assesses and
  records the result** rather than assuming; if it doesn't work, the `.reload`/`CTRL+F1`
  commands still shrink the loop and the finding saves the next investigation.
- **[Deferring cloud CI could be forgotten] → D1 records the revisit trigger in Open
  Questions and the proposal**, and the docs correction ensures the "cloud can't test the
  game at all" framing doesn't silently foreclose it.

## Migration Plan

1. Land the one-command verify/restage script (D3) — independently useful immediately.
2. Add the opt-in `pre-push` hook + installer on top of it (D1/D2); install and dogfood it.
3. Add Core coverage to `ci.yml` (D4); verify green.
4. Land the diagnosis toolkit — log helper, dev-world profile, reload notes (D5).
5. Record Atlas next-adoption notes and the deferred-cloud rationale.

Rollback: everything is additive tooling/docs. Uninstalling the hook (or `--no-verify`)
restores the prior manual flow; nothing touches mod behavior.

## Open Questions

- **When to revisit cloud CI for the Atlas suite.** Deferred per D1. Trigger conditions:
  (a) a second contributor joins (a bypassable local hook no longer protects `main`), or
  (b) version-specific regressions across 1.22.x patch releases start appearing that a single
  local install can't catch. If revisited, the approach is the CDN-server-download recipe
  (`reference/atlas-wiki/CI-Recipes.md`) plus run-time staging of `gui`/`configlib`, and a
  first task must probe whether the headless server archive ships
  `cairo-sharp`/`SkiaSharp`/`OpenTK.Mathematics` (guarded `Mod.csproj` territory).
- **Does Hot Reload work for this mod's build on macOS?** Resolved by the toolkit task.
