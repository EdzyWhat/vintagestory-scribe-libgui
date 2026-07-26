## Why

Scribe dialogs render functional widgets over a transparent window body — only the title bar and
per-row tints paint. The user wants each Scribe window to read as a themed physical object (open
sketchbook, parchment page, clay tablet) via hand-authored art, and wants the mechanism to support
*distinct* per-view illustrations where an item exposes more than one backdrop-bearing view. The sibling
change `scribe-themed-toggle` already delivers the `PixelArtDisplay` client preference (the shipped name
for the preference this originally called `ThemedBackgrounds`) and the light/dark theme split; this change
delivers the reusable backdrop *mechanism* that hangs off that toggle.

> NOTE (2026-07-26, reconciled to shipped): the Lectern — the only item wired here — exposes a SINGLE
> backdrop-bearing body (its read and editor views share one spec). The originally-planned distinct
> settings-page backdrop does not apply: the in-dialog settings view was removed in the 2026-07-25 pivot
> (the gear now opens the standalone settings window, which follows the global theme and is not
> backdrop-wrapped). The per-view capability is retained in the mechanism for a future item; the
> `LecternSettings` spec is defined and reserved.

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
- Wire the Lectern's single dialog body (read + editor) to the `LecternPage` backdrop spec, drawn only
  when `MySettings.PixelArtDisplay` is ON. When the toggle is OFF, the body is used bare (no `Container`
  wrap) under the fallback theme. (The `LecternSettings` spec is defined and reserved for a future item's
  page-vs-settings split; there is no in-dialog Lectern settings view to wire it to after the 2026-07-25
  pivot.)
- Ship no new art in this change: `lecternbackdrop.png` (a flat tan placeholder) already exists and is
  reused as the Lectern body backdrop until real art lands. Append a LibGUI backdrop lesson to
  `VSAPI-NOTES.md`.

## Capabilities

### New Capabilities
- `gui-backdrop`: a reusable, per-item / per-view illustrated backdrop mechanism for Scribe dialogs.
  Any item/view declares its own `ScribeBackdropSpec` (art of any size — nothing assumes a shared
  dimension); a `Wrap` helper draws the texture behind the dialog content via a LibGUI `Container` +
  `BoxStyle.Texture`, or a flat placeholder color when the PNG is missing. Backdrop bitmaps are
  self-loaded (survive VS post-startup asset unload), shared, and cached on the mod system, not
  reloaded per open. Drawing is gated on the `PixelArtDisplay` toggle: OFF draws no backdrop.

### Modified Capabilities
- `lectern-gui-shell`: the single "portrait, custom-drawn backdrop" and its "swappable without a code
  change" requirement are superseded by the general backdrop mechanism — the Lectern's dialog body (read +
  editor) now renders its backdrop via `gui-backdrop`, gated on the `PixelArtDisplay` toggle, with a
  flat-placeholder fallback. (The mechanism supports a distinct per-view backdrop; the Lectern currently
  backs its single body — its in-dialog settings view was removed in the 2026-07-25 pivot.)

## Impact

- **Depends on `scribe-themed-toggle`** (assumed landed): this change reads the `PixelArtDisplay`
  boolean from `ScribePlayerSettings` / `MySettings` to gate whether a backdrop is drawn. It does NOT
  introduce the toggle, the light theme, animated tabs, or the pin editor — those are sibling changes.
- **Mod (`src/Mod/`)**: new `ScribeBackdrop.cs`; `ScribeModSystem` gains `GetBackdropBitmap` + a bitmap
  cache + disposal; `GuiDialogScribeLecternLibGui` wraps its dialog body (read + editor) in
  `ScribeBackdrop.Wrap` (the `LecternPage` spec) when the toggle is ON.
- **Core (`src/Core/`)**: none. Backdrop code is all VS/Skia and lives in `src/Mod/`.
- **Assets**: reuses the existing `assets/scribe/textures/gui/lecternbackdrop.png` as the Lectern body
  backdrop. (The reserved `LecternSettings` PNG is a later, non-blocking art deliverable for a future
  item's settings view — the placeholder color stands in.)
- **No new dependencies**: LibGUI (`gui`, the existing hard dep) already ships `Container` /
  `BoxStyle.Texture`; SkiaSharp `SKBitmap` is already used by the icon loader.
- **Verification is in-game only** (the Core suite cannot reach `src/Mod/` GUI or the VS API).
