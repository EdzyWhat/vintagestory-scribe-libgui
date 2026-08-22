# Scribe

A note-taking and light task-management mod for [Vintage Story](https://www.vintagestory.at/).

Scribe helps you remember your goals across Vintage Story's long, branching progression.
Tasks are the priority — dead-easy to view and edit — with immersive notekeeping as a
secondary payoff. Note-keeping tools progress with your tech tree, from a crude clay
tablet in the stone age up to shared bulletin boards, grounded in both the real
archaeology of writing and vanilla game mechanics.

Three writing tiers ship today: handheld **clay & wax Tablets** (scratch a quick list, let
it dry hard, then re-wet to revise or fire it to keep it forever), the carried **Notebook**
(and its **Clockmaker's** timer variant), and the placeable, shared **Lectern** — all feeding
a per-player pinned-task **HUD** that keeps your goals on screen while you play.

> **Status:** released — v1.3.0, [live on the mod DB](https://mods.vintagestory.at/scribe).
> See [`ROADMAP.md`](./ROADMAP.md) for the staged plan ahead and [`CHANGELOG.md`](./CHANGELOG.md)
> for what has shipped.

## Features

Scribe's writing tools unlock in step with your tech tree — you keep notes the way the era
lets you — and everything you write feeds one always-on task overlay.

### 📿 Tablets — the scratch tier

Cheap handheld scratchpads you can craft in the stone age, each holding a short list (up to
10 tasks, 1 pin). Text is carved in a bespoke **cuneiform** script by default (switch it off
in settings for plain text).

- **Clay Tablet** (red, blue, or fire clay) — fresh clay is wet and editable, dries **hard**
  over ~2 in-game days to lock the writing, then either **re-wet in water** to revise or
  **fire it** like pottery to keep it forever.
- **Wax Tablet** — a beeswax-filled wooden frame that never dries or fires, so it's always
  rewritable.

### 📖 Notebook — the carried document

A leather-bound book that holds your full task checklist and freeform notes in your
inventory, with no editor lock. Each notebook keeps a private, auto-recorded **History** —
crafted, picked up, deaths, PvP and boss kills, temporal storms weathered — so it reads like
the journal of its owner.

- **Clockmaker's Notebook** — a variant that adds a **Timer** tab for real-time and
  in-game-time countdowns; a running timer rides along on the HUD and blinks when it fires.
  Craftable by the Clockmaker class, or by anyone with the trader-sold **Schematic**.

### 📚 Lectern — the shared block

A placeable, multiplayer-safe note board. Anyone in reach can read it and check tasks off;
one player edits at a time (server-authoritative, with a single-editor lock). It logs its
visitors on a **Guest Book** tab — handy for a base or trader stall.

### 🧱 Chalkboard — the wall-hung Lectern

The same shared writing surface as the Lectern, hung on a wall like a painting instead of
stood on the floor. Point at a wall to mount it facing outward. It holds a short list (up
to 10 tasks) and keeps a Guest Book of its visitors. This is **not** a drawable board —
you write tasks on it the same way you write on a Lectern.

### 📜 Scriptorium — the copy desk

A placeable workbench for moving documents around. Drop a Scribe item in and **transcribe** it
onto another — duplicate a checklist, or **append** one document's tasks onto another — with the
result stamped like sealed paper.

- **Import / Export** — two clipboard lanes for getting a document in and out of the game as text:
  - **Copy as JSON** — a complete, human-readable snapshot (every task, note, tracker, and link),
    ideal for backing up or sharing a list verbatim.
  - **Copy as TSV** — a fixed six-column table (`Type · Done · Text · Special · Count · Depth`) that
    pastes straight into Excel or Google Sheets, so you can bulk-edit tasks in a spreadsheet and
    paste the range back. The column set never changes, so old and new exports stay interchangeable.
  - **Import** auto-detects JSON vs. TSV from the clipboard and writes onto the slotted item
    (overwrite or append, same as transcribe). Unknown item/link references land as plain tasks
    rather than failing the whole paste, and **imported tasks are never pinned** — an import brings
    the words, not anyone's HUD state.

### 🔨 Crafting Tasks

Open an item's Handbook page and click **Add Crafting Task** to pin a recipe-bound goal: the
row tracks the output like an Item Tracker, and it builds a shopping list of ingredient
subtasks underneath (liquids as litre trackers). Items with several grid recipes get a
labeled link per variant. Item Trackers and Links are created the same way, from the same
"Add to Scribe" footer.

### 📌 Pinned Task HUD

Pin a task from any Scribe item and it appears on an always-on overlay over the game world —
your goals stay in view without opening anything. Pins are **per-player** (yours don't touch
anyone else's), aggregated across every tablet, notebook, and lectern you use, positioned to
any screen edge, and checked off with a 1.5-second undo window.

### ✍️ Editing that gets out of your way

A shared in-place editor across every writing surface: type directly into rows, **Enter** to
add a task, drag to reorder, full caret/selection/clipboard support (including Mac
conventions), and a **Shift + right-click quick-add** gesture that opens any item straight to
a fresh, focused task. Configurable completion policies decide what a checked task does —
keep, sink to bottom, unpin, or delete.

## Requirements

- Vintage Story **1.22.x** (targets .NET 10)
- **[LibGUI](https://mods.vintagestory.at/libgui)** (`gui` mod, **3.1.0**) — a hard
  dependency; install it alongside Scribe on every client and server.

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
`gui_3.1.0.zip` into the test output directory (from `$(VintagestoryDataMods)`, defaulting to
the macOS install path), and `AssemblyInfo.cs` stages it via `[assembly:
AtlasMods("gui_3.1.0.zip")]`. On a non-macOS machine, or if the mod version changes, override
the path (`-p:VintagestoryDataMods=<your Mods folder>`) and update the `gui_3.1.0.zip`
filename in both places in lockstep.

`FixtureBuilders` is excluded from normal runs: it's a one-time world-builder scenario, not
a pass/fail test. Two prebuilt world saves live under `tests/Integration.Tests/fixtures/`
(both checked in) rather than seeding from an earlier scenario method — Atlas's `RestartWorld`
isolation genuinely restarts the server before a scenario, so cross-scenario seeding would
depend on xUnit's unguaranteed execution order:

- `lectern.vcdbs` — a current (v6 codec) world with a lectern document and a per-player pin.
  Booted by `PersistenceScenarios` (restart-persistence). Regenerate it whenever the document
  codec version bumps, or the stored document drops out of the accepted window and loads empty.
- `lectern-v3.vcdbs` — a pre-change (v3 codec) world with a lectern whose document used the
  retired shared per-block `pinned` flag. Booted by `MigrationScenarios` to prove the v3→v4
  forward migration (legacy-pin drain + v4 re-save). **Irreplaceable: the codec only writes
  v4 now, so this v3 save can never be regenerated — do not delete or overwrite it.**

If `FixtureBuilders` changes, or the document codec version bumps (the current `lectern.vcdbs`
falls out of the accepted window and loads empty), regenerate it with `--force` (the v3 save is
not regenerable and must be left alone):

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
