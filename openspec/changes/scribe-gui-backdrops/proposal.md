## Why

Scribe dialogs render functional widgets over a transparent window body — only the title bar and
per-row tints paint. The user wants each Scribe window to read as a themed physical object (open
sketchbook, parchment page, clay tablet) via hand-authored art, and wants the read/editor page and
the settings page of the same item to carry *distinct* illustrations. The sibling change
`scribe-themed-toggle` already delivers the `ThemedBackgrounds` client preference and the light/dark
theme split; this change delivers the reusable backdrop *mechanism* that hangs off that toggle.

The existing `lectern-gui-shell` capability already asserts a single "portrait, custom-drawn backdrop"
that is "swappable without a code change." That was a native-GUI-era, single-image assumption. The new
mechanism generalizes it: a per-item, per-view LibGUI `Container` + `BoxStyle.Texture` backdrop of any
size, self-loaded and cached, that falls back to a flat placeholder color when the PNG is absent — so
the whole structure is testable in-game *before* any art is drawn (the user's flat-color-first build
strategy). Because it supersedes the old single-backdrop requirements, this change modifies
`lectern-gui-shell` and adds a new `gui-backdrop` capability for the general mechanism.

## What Changes

- Add `src/Mod/ScribeBackdrop.cs`: a `ScribeBackdropSpec` record (holds an `AssetLocation`, no size
  assumption), a `ScribeBackdrops` holder of per-item / per-view specs (Lectern page vs Lectern
  settings to start; Desk/Notebook/Tablet added as they ship), and a `ScribeBackdrop.Wrap(capi, spec,
  placeholder, child)` helper that wraps the child in a `Container` whose `BoxStyle` paints the loaded
  texture behind the content — or, when the bitmap is absent/unloadable, a flat placeholder `Color`.
- Add a self-loaded, shared `SKBitmap` cache to `ScribeModSystem`: `GetBackdropBitmap(AssetLocation)`
  loads via `TryGet(loc, loadAsset: true)` + `SKBitmap.Decode` (mirroring the SVG icon loader ~:217),
  caches the result (including a null result) in a `Dictionary`, logs one warning on failure, and
  disposes every cached bitmap in `Dispose()`. One immutable bitmap is shared across every dialog open;
  it is never disposed by a dialog.
- Wire the Lectern's read/editor body and the settings body to **distinct** backdrop specs
  (`LecternPage` vs `LecternSettings`), each drawn only when `MySettings.ThemedBackgrounds` is ON.
  When the toggle is OFF, the body is used bare (no `Container` wrap) under the dark fallback theme.
- Ship no new art in this change: `lecternbackdrop.png` (a flat tan placeholder) already exists and is
  reused; the distinct settings backdrop starts as a missing-asset placeholder color until its PNG is
  drawn. Append a LibGUI backdrop lesson to `VSAPI-NOTES.md`.

## Capabilities

### New Capabilities
- `gui-backdrop`: a reusable, per-item / per-view illustrated backdrop mechanism for Scribe dialogs.
  Any item/view declares its own `ScribeBackdropSpec` (art of any size — nothing assumes a shared
  dimension); a `Wrap` helper draws the texture behind the dialog content via a LibGUI `Container` +
  `BoxStyle.Texture`, or a flat placeholder color when the PNG is missing. Backdrop bitmaps are
  self-loaded (survive VS post-startup asset unload), shared, and cached on the mod system, not
  reloaded per open. Drawing is gated on the `ThemedBackgrounds` toggle: OFF draws no backdrop.

### Modified Capabilities
- `lectern-gui-shell`: the single "portrait, custom-drawn backdrop" and its "swappable without a code
  change" requirement are superseded by the general per-view backdrop mechanism — the Lectern's
  read/editor page and settings page now render *distinct* backdrops via `gui-backdrop`, gated on the
  themed toggle, with a flat-placeholder fallback.

## Impact

- **Depends on `scribe-themed-toggle`** (assumed landed): this change reads the `ThemedBackgrounds`
  boolean from `ScribePlayerSettings` / `MySettings` to gate whether a backdrop is drawn. It does NOT
  introduce the toggle, the light theme, animated tabs, or the pin editor — those are sibling changes.
- **Mod (`src/Mod/`)**: new `ScribeBackdrop.cs`; `ScribeModSystem` gains `GetBackdropBitmap` + a bitmap
  cache + disposal; `GuiDialogScribeLecternLibGui` wraps the read/editor body and the settings body in
  `ScribeBackdrop.Wrap` (per-view spec) when the toggle is ON.
- **Core (`src/Core/`)**: none. Backdrop code is all VS/Skia and lives in `src/Mod/`.
- **Assets**: reuses the existing `assets/scribe/textures/gui/lecternbackdrop.png`; the distinct
  settings backdrop PNG is a later, non-blocking art deliverable — the placeholder color stands in.
- **No new dependencies**: LibGUI (`gui`, the existing hard dep) already ships `Container` /
  `BoxStyle.Texture`; SkiaSharp `SKBitmap` is already used by the icon loader.
- **Verification is in-game only** (the Core suite cannot reach `src/Mod/` GUI or the VS API).
