## Why

Diagnosing GUI layout bugs in the lectern dialog (row-list culling, dialog width, icon
scaling) currently means edit `ScribeClientConfig`'s on-disk JSON → fully quit/relaunch the
client → observe → repeat. This is slow enough that it discourages the kind of exploratory
"nudge a number, watch what happens" investigation needed to root-cause subtler rendering
issues — like the currently-open question of why only the last composed row in editor view
renders its full chrome (drag handle, checkbox, bordered text input) while rows above it
render as bare text. A live-tunable overlay turns a multi-minute edit/relaunch loop into a
sub-second slider drag, and is the most direct way to test a hypothesis like "the overflow
follows the window's bottom edge."

## What Changes

- Add **VSImGui** (`mods.vintagestory.at/imgui`) as a **debug-only** mod dependency, gated
  behind `#if DEBUG` so it never ships in a Release build or affects players. Wire
  `DebugWidgets.FloatSlider`/`IntSlider` bound directly to the row-list/culling-related
  `ScribeClientConfig` fields (`VisibleListHeight`, `RowSpacing`, `TopContentGap`,
  `ReadListWidth`/`EditorListWidth`, `RowListCullBuffer`-equivalent if it becomes
  tunable), paired with a forced recompose each frame the values change, so dragging a
  slider visibly moves the dialog's layout in real time.
- Add **ConfigLib** (`mods.vintagestory.at/configlib`) as the **planned permanent**
  optional soft dependency (already anticipated in `ROADMAP.md`'s parked section), gated
  by `IsModEnabled` per existing project convention. Expose the full `ScribeClientConfig`
  field set through ConfigLib's in-game settings GUI via the no-code
  `configlib-patches.json` manifest pointing at the existing config file — no code
  restructuring needed since the file already exists.
- Both are **additive, non-breaking, and off by default in the absence of the other mod**
  — `ScribeClientConfig`'s existing JSON-file-based tuning path is unaffected either way.

## Capabilities

### New Capabilities

- `debug-live-tuning`: a debug-only (`#if DEBUG`) live GUI-tuning surface backed by
  VSImGui, letting a developer drag `ScribeClientConfig` values and see the lectern
  dialog react in real time without a client relaunch. Never present in a Release build.

### Modified Capabilities

(none — `ScribeClientConfig`'s own fields/semantics are unchanged; this only adds two new
ways to *edit* them, matching the file it already reads/writes)

## Impact

- `src/Mod/Mod.csproj`: two new `<Reference>` `HintPath` entries against the locally
  installed game mods' extracted DLLs (neither VSImGui nor ConfigLib has a usable NuGet
  package at the version actually installed — see design.md Context/Decision 2). VSImGui's
  reference `ItemGroup` is conditioned on `Configuration == 'Debug'` so it never
  contributes to a Release build artifact. ConfigLib's is unconditional but soft
  (`IsModEnabled`-gated at runtime, per existing convention).
- New `src/Mod/assets/scribe/config/configlib-patches.json` manifest.
- `src/Mod/ScribeModSystem.cs` (or a new small debug-only class): registers the ImGui
  debug window when the mod loads in a Debug build.
- No `Core` changes — this only touches `Mod`-layer config wiring and debug tooling.
- CI impact: none expected — CI only builds/tests `Core`; if a Release-configuration CI
  build of `Mod` exists, it must be confirmed to not require VSImGui at all (design.md to
  address).
