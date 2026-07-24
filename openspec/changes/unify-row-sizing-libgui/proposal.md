## Why

In the LibGUI lectern, a task rendered in the read view and the same task in the editor
view do not occupy the same vertical space: read rows use font 14 with a static text
widget, while editor rows use font 15 inside a bordered, auto-growing input with its own
internal padding and a 6px inter-row gap. Switching views therefore shifts every task,
which is visually jarring for a dialog whose whole point is a stable, scannable task list.
On top of that, every sizing value is a hardcoded literal — the LibGUI dialog reads
`ScribeClientConfig` zero times (that load path died with the old native GUI), so there is
no way to tune row sizing short of editing source and rebuilding.

## What Changes

- **Unify task-row sizing** so a single-line task occupies pixel-identical vertical space
  in both the read and editor views: same font size (15 — the editor value wins), same
  vertical alignment, same inter-row spacing, and matching padding. The read row gains an
  internal text inset matching the editor field's internal padding (so heights and the
  text's left edge line up) but keeps **no border**. Multi-line parity is best-effort.
- **Make row sizing data-driven.** Introduce the first `ScribeClientConfig` load into the
  LibGUI dialog (loaded fresh per dialog-open in the constructor, matching the existing
  per-open lifecycle and giving an edit-JSON-then-reopen tuning loop). Add a small set of
  clearly-named, LibGUI-semantic **float** sizing fields; thread them through the row
  widgets via one immutable style struct so the widgets stay testable.
- **Lay groundwork for future font/UI scaling.** All scalable values pass through a single
  factory that multiplies by the existing `TextSizeScale` field (default `1f`, so a
  behavioral no-op today). A future scaling change becomes a one-line edit at that
  chokepoint rather than a re-plumbing.
- **Re-add ConfigLib as an optional soft dependency** to expose the new sizing floats in
  ConfigLib's in-game settings panel: a `<Reference>` to the vendored `configlib.dll`
  (`Private=false`) plus a no-code `configlib-patches.json` manifest pointing at
  `scribe-client-config.json`. All exposed settings are **float-typed** (an integer
  ConfigLib setting previously broke the panel). No modinfo hard-dependency — the manifest
  is inert when ConfigLib is not installed.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: the read and editor views must render task rows at a unified,
  config-driven size so switching views keeps each task in place; row sizing is sourced
  from `ScribeClientConfig` (not hardcoded) and optionally tunable via ConfigLib.

## Impact

- **Code:** `src/Mod/GuiDialogScribeLecternLibGui.cs` (config load in ctor; new
  `ScribeRowStyle` struct threaded into the read/editor content widgets and the
  `ScribeReadRow`/`ScribeEditRow` row widgets; editor `Column` spacing → 0; read `ListView`
  `estimatedItemHeight` derived from the style), `src/Mod/ScribeMultilineField.cs`
  (`PadX`/`PadY` promoted from `const` to instance values fed from the style),
  `src/Mod/ScribeClientConfig.cs` (7 new float fields).
- **Assets:** new `src/Mod/assets/scribe/config/configlib-patches.json`.
- **Build:** `src/Mod/Mod.csproj` regains the `configlib.dll` reference (optional, not
  copied into output). No CI impact — CI builds/tests only `Core`, which is untouched.
- **Dependencies:** ConfigLib returns as an optional soft dependency; players without it
  are unaffected. No new hard dependency.
- **Behavior:** no change to persistence, sync, the document model, or Core. Purely a
  client-side presentation/layout change plus a config surface.
