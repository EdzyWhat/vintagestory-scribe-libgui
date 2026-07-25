# Scribe

A note-taking and light task-management mod for [Vintage Story](https://www.vintagestory.at/).

Scribe helps you remember your goals across Vintage Story's long, branching progression.
Tasks are the priority — dead-easy to view and edit — with immersive notekeeping as a
secondary payoff. Note-keeping tools progress with your tech tree, from a crude clay
tablet in the stone age up to shared bulletin boards, grounded in both the real
archaeology of writing and vanilla game mechanics.

> **Status:** early development. See [`ROADMAP.md`](./ROADMAP.md) for the staged plan.

## Requirements

- Vintage Story **1.22.x** (targets .NET 10)

## Building from source

This is a C# code mod. To build it you need:

- The **.NET 10 SDK**
- A **Vintage Story installation**, with the `VINTAGE_STORY` environment variable
  pointing at it (e.g. on macOS: `export VINTAGE_STORY="/Applications/Vintage Story.app"`).
  The mod references `VintagestoryAPI.dll` from there.

```sh
dotnet test    # runs the game-agnostic Core unit tests (no game install needed)
dotnet build   # compiles the full mod against VintagestoryAPI.dll
```

### Project layout

- `src/Core/` — game-agnostic library (models, rules, serialization). No game references,
  so it is unit-testable anywhere with `dotnet test`.
- `src/Mod/` — the Vintage Story code mod; a thin adapter mapping the game API onto `Core`.
- `tests/Core.Tests/` — xUnit tests over `Core`.
- `tests/Integration.Tests/` — [Atlas](https://github.com/Pixnop/Atlas) integration tests
  that boot a real headless server and exercise the mod's server-side behavior (see below).

> **CI note:** GitHub's cloud runners have no Vintage Story install, so continuous
> integration runs the `Core` suite (with coverage). The full mod and the Atlas integration
> suite run **locally**, gated by the opt-in pre-push hook (see [Development](#development)),
> which uses the game install already on the machine. Running Atlas in cloud CI is technically
> feasible — the server can be downloaded from the official CDN at job time — but is deferred
> for a solo project; see the `improve-testing-and-diagnosis` change design for the revisit
> trigger.

### Running the Atlas suite

[Atlas](https://github.com/Pixnop/Atlas) boots a real headless Vintage Story server inside
`dotnet test`, so `tests/Integration.Tests` can exercise persistence, the network edit
round-trip, and the single-editor lock against the actual engine — not mocks. It needs the
same `VINTAGE_STORY` environment variable as building the mod, plus the .NET 10 SDK.

```sh
dotnet test tests/Integration.Tests --filter "FullyQualifiedName!~FixtureBuilders"
```

**The hard `gui` dependency must be staged.** Atlas boots the server with only the staged
mods on its mod path — it never sees your installed `VintagestoryData/Mods` folder — so
Scribe's hard dependency on the `gui` (LibGUI) mod has to be staged too, or the mod loader
skips Scribe and every scenario fails at `SetBlock` with *"Unknown block code
scribe:scribelectern"*. `Integration.Tests.csproj` handles this by copying the installed
`gui_2.0.0.zip` into the test output directory (from `$(VintagestoryDataMods)`, defaulting to
the macOS install path), and `AssemblyInfo.cs` stages it via `[assembly:
AtlasMods("gui_2.0.0.zip")]`. On a non-macOS machine, or if the mod version changes, override
the path (`-p:VintagestoryDataMods=<your Mods folder>`) and update the `gui_2.0.0.zip`
filename in both places in lockstep.

`FixtureBuilders` is excluded from normal runs: it's a one-time world-builder scenario, not
a pass/fail test. Two prebuilt world saves live under `tests/Integration.Tests/fixtures/`
(both checked in) rather than seeding from an earlier scenario method — Atlas's `RestartWorld`
isolation genuinely restarts the server before a scenario, so cross-scenario seeding would
depend on xUnit's unguaranteed execution order:

- `lectern.vcdbs` — a current (v4) world with a lectern document, a per-player pin, and a
  non-default per-player setting. Booted by `PersistenceScenarios` (restart-persistence).
- `lectern-v3.vcdbs` — a pre-change (v3 codec) world with a lectern whose document used the
  retired shared per-block `pinned` flag. Booted by `MigrationScenarios` to prove the v3→v4
  forward migration (legacy-pin drain + v4 re-save). **Irreplaceable: the codec only writes
  v4 now, so this v3 save can never be regenerated — do not delete or overwrite it.**

If `FixtureBuilders` changes (the v4 fixture needs rebuilding), regenerate the v4 save with
`--force` (the v3 save is not regenerable and must be left alone):

```sh
dotnet build tests/Integration.Tests
atlas fixture tests/Integration.Tests/bin/Debug/net10.0/Integration.Tests.dll \
    --scenario BuildsLecternWithDocumentFixture \
    --out tests/Integration.Tests/fixtures/lectern.vcdbs --force
```

(`atlas` is the Atlas CLI: `dotnet tool install -g Pixnop.Atlas.Cli`.)

### Local verification loop

`build/verify.sh` collapses the full check-and-stage loop into one command: it builds the
mod, runs the Core unit tests, runs the Atlas suite (with the `FixtureBuilders` scenario
excluded), and — only if all of that passes — restages the mod into your Mods folder. It
uses the Vintage Story install and staged mod dependencies already on this machine; nothing
is downloaded.

```sh
./build/verify.sh              # Release build, test, and restage
./build/verify.sh Debug        # same, staging the Debug (VSImGui) build
./build/verify.sh --no-restage # gate only: build + both suites, no restage (what the hook runs)
```

Each stage runs in order and fails fast: on the first failure the script names the failing
stage, exits non-zero, and does **not** restage, so a broken build is never staged.

### Pre-push gate (opt-in)

An opt-in git `pre-push` hook runs the Core tests and the Atlas suite before a push that
updates `main`, and blocks the push if either fails. Git hooks aren't cloned, so install it
explicitly once per clone:

```sh
./build/install-hooks.sh
```

The hook only gates pushes that update `main` (work-in-progress branches push freely) and
reuses `build/verify.sh --no-restage`, so there is one source of truth for the stage list.
To push despite the hook, use the standard escape hatch:

```sh
git push --no-verify
```

### Diagnosing in-game

`build/scribe-log.sh` follows the game logs and filters them to Scribe-relevant lines — the
`[scribe]`-prefixed server trace plus asset/mod-load errors — so you can watch the
server-authoritative flow live during a playtest without hand-building log paths:

```sh
./build/scribe-log.sh          # follow server-main.txt, [scribe] + errors only
./build/scribe-log.sh --client # also include client-main.txt
./build/scribe-log.sh --all    # no filter (raw follow, both logs)
```

A repeatable **dev-world profile** and the fast in-game reload commands are documented in
[`VSAPI-NOTES.md`](./VSAPI-NOTES.md) under "Dev-diagnosis toolkit."

## Development

This project uses [OpenSpec](https://github.com/Fission-AI/OpenSpec) for spec-driven
development — each feature is proposed as a spec before it is implemented. See the
`openspec/` directory.

## License

[MIT](./LICENSE)
