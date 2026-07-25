## Why

Scribe has three verification layers — ~70 Core unit tests, ~23 Atlas headless-server
integration scenarios, and the manual in-game playtest loop — but the loop that ties them
together is entirely manual. The Atlas suite (persistence, single-editor lock, edit
round-trip, v3→v4 migration) is our highest-value regression net, yet it runs **only when
someone remembers to run it**. Nothing makes it run at the one moment it matters — before
code reaches `main`.

Cloud CI could gate it, but for a solo, single-install project the unique benefit of a cloud
runner (proving versions you don't have installed; blocking a *teammate's* bad PR) is small
today, while the cost is high — the headless runner has none of the game DLLs, mod
dependencies, or install that this machine already has, so it would need CDN downloads,
dependency staging, and a guarded `Mod.csproj` probe. The far better trade is to lean into
**local-machine refinement**: on this machine the install, the mod zips, and the DLLs all
already exist, so the same suite that passes by hand can run automatically at push time with
none of that machinery.

Separately, the local iterate/diagnose loop is our weakest link — especially for GUI work
on Apple Silicon, where the interactive tuning paths (VSImGui sliders, ConfigLib panel) are
dead. We lean on full restage-and-relaunch cycles and ad-hoc log reading when faster,
documented tools exist (`.reload` commands, `CTRL+F1` world reload, a `[scribe]`-log tail
helper, developer-mode debug settings).

## What Changes

- **Add a local pre-push gate.** A git `pre-push` hook (opt-in, installed via a `build/`
  script) runs the Core tests and the Atlas integration suite before a push to `main` is
  allowed — turning "remember to run the suite" into "can't forget," using the game install
  and dependencies already on this machine. No cloud infra, no downloads.
- **Add a one-command verify/restage script.** A single `build/` entry point that
  builds → runs Core tests → runs the Atlas suite → restages the mod, collapsing the
  multi-step loop currently run by hand and shared by the pre-push hook.
- **Establish a documented dev-diagnosis toolkit**: a log tail/filter helper for the
  `[scribe]` trace and `server-main.txt`/`client-main.txt`; a documented "dev world" launch
  profile with developer-mode debug settings preset; and captured notes on the fast-iterate
  commands (`.reload textures/shapes/shaders/lang`, `CTRL+F1`, Hot Reload viability on Mac).
- **Add Core coverage reporting** to the existing cloud CI Core job using the already-present
  `coverlet.collector`, so there is a signal for what Core logic is untested. (This is the
  one cheap cloud win — the Core job already runs there and needs no game install.)
- **Vendor Atlas reference material** (source + wiki cloned to `reference/atlas`,
  `reference/atlas-wiki`, gitignored — already done) and record which Atlas 0.11.0
  capabilities we should adopt next (`ExecuteCommand` assertions, `atlas diff` differential
  regression), so the harness we already pay for is used more fully.
- **Record the deferred cloud-CI option.** Capture in the design *why* running the Atlas
  suite on cloud runners was deferred and *when* to revisit it (if a version matrix across
  1.22.x patch releases starts catching regressions this machine can't) — including the
  correction that it is technically feasible via a CDN server download, so the outdated
  "CI can't test the game at all" framing does not silently foreclose it later.

Non-goals: running the Atlas suite on cloud runners now; a self-hosted runner; building the
mod zip in CI (release packaging stays local per `release.yml`); any change to `src/Core` or
runtime mod behavior.

## Capabilities

### New Capabilities
- `local-verification-workflow`: the local, on-this-machine gate and iterate loop — the
  opt-in pre-push hook running Core + Atlas, and the one-command build/test/restage script
  that backs it.
- `dev-diagnosis-toolkit`: the local diagnosis tooling and its documentation — log
  tail/filter helper, dev-world launch profile with debug settings, fast-reload workflow
  notes, and vendored Atlas reference material + next-adoption notes.

### Modified Capabilities
<!-- No existing spec's requirements change. Core-coverage is an additive tweak to the
     existing cloud CI job, captured as a task rather than a spec requirement change; the
     deferred cloud-Atlas option is documented in design.md, not a spec. -->

## Impact

- **New tooling** (`build/`): a `pre-push` hook + installer, a one-command verify/restage
  script, and a `[scribe]` log tail/filter helper. No changes to runtime mod behavior or
  `src/Core`.
- **CI / build**: `.github/workflows/ci.yml` gains only Core coverage collection (additive,
  no game install needed). `Directory.Build.props`, `Mod.csproj`, and the integration csproj
  are **not** touched.
- **Docs / agent framing**: `README.md` (local verify loop + accurate CI note),
  `VSAPI-NOTES.md` (debug-settings + reload notes, Atlas next-adoption note),
  `openspec/config.yaml` / `CLAUDE.md` (light correction so the "cloud can't test the game"
  framing does not foreclose the deferred option). `.gitignore` (Atlas reference clones —
  already added).
- **Local-only dependency handling is unchanged**: the pre-push hook and Atlas suite use the
  mod zips and game install already present on this machine (`src/Mod/lib/` extraction per
  its existing `README.md`); nothing is downloaded or committed.
