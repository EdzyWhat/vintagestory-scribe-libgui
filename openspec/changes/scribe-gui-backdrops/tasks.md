## 1. ScribeModSystem: self-loaded backdrop bitmap cache

- [x] 1.1 Add a `Dictionary<string, SKBitmap?>` backdrop cache field to `ScribeModSystem` (keyed by the
  asset location string; caching a null result too).
- [x] 1.2 Add `public SKBitmap? GetBackdropBitmap(AssetLocation loc)`: return the cached entry if
  present; otherwise `TryGet(loc, loadAsset: true)`, `SKBitmap.Decode(asset.Data)`, cache and return the
  result. Mirror the self-loading pattern of `RegisterSvgIcon` (~:217) so it survives post-startup asset
  unload.
- [x] 1.3 On an absent/unloadable asset, cache `null` and log **exactly one** warning for that location
  (so repeat opens do not re-warn or re-attempt the failing load every frame).
- [x] 1.4 Dispose every cached `SKBitmap` and clear the cache in `ScribeModSystem.Dispose()`; ensure no
  dialog ever disposes a backdrop bitmap.

## 2. ScribeBackdrop.cs: spec record + per-view specs + Wrap helper

- [x] 2.1 Add `src/Mod/ScribeBackdrop.cs` with `internal sealed record ScribeBackdropSpec(AssetLocation
  Texture)` — no size field, nothing assumes a shared dimension.
- [x] 2.2 Add a `ScribeBackdrops` holder exposing per-item / per-view specs: `LecternPage` (reusing the
  existing `textures/gui/lecternbackdrop.png`) and `LecternSettings` (its own
  `textures/gui/lecternsettingsbackdrop.png`, placeholder until art lands). Leave room for
  Desk/Notebook/Tablet page+settings specs as they ship.
- [x] 2.3 Add `static Widget Wrap(ICoreClientAPI capi, ScribeBackdropSpec spec, Vector4 placeholder,
  Widget child)`: fetch the bitmap via `GetBackdropBitmap`; build a `Container` with
  `new BoxStyle { Texture = bmp }` when non-null, else `new BoxStyle { Color = placeholder }`; return it
  wrapping `child`. Rely on `Container` painting its texture behind the child (no `Stack`).

## 3. Wire Lectern page vs settings backdrops (gated on the toggle)

- [x] 3.1 In `GuiDialogScribeLecternLibGui.Build()`, read `modSystem.MySettings.PixelArtDisplay` fresh
  each build (following the `ScribeRowStyle.FromSettings` per-build precedent ~:845). NOTE: the toggle
  shipped as `PixelArtDisplay` (sibling `scribe-themed-toggle`), not the planned `ThemedBackgrounds`; its
  doc-comment explicitly reserves "illustrated backgrounds" for this change's phase.
- [x] 3.2 When themed mode is ON, wrap the Lectern body (read AND editor) in
  `ScribeBackdrop.Wrap(..., LecternPage, <tan page placeholder>, body)`. DEVIATION: the in-Lectern
  settings view was removed in the 2026-07-25 pivot (the gear now opens the standalone, never-themed
  settings window), so there is no second in-dialog view to carry `LecternSettings`. That spec is defined
  and reserved for a future item's page-vs-settings split; the standalone settings window is deliberately
  not backdrop-wrapped (it follows the global theme). Decided with the user 2026-07-26.
- [x] 3.3 When themed mode is OFF, use each body bare (no `Container` wrap) so the dialog renders the
  plain LibGUI fallback with no backdrop.

## 4. Placeholder-fallback behavior

- [x] 4.1 Confirm a missing/unloadable PNG yields the flat placeholder color path (via cached-null),
  the dialog renders its structure over it, and no exception is thrown.
  - **Confirmed 2026-07-28**: flat tan placeholder rendered, no crash. TESTING.md `3451f8d1`.
- [x] 4.2 Confirm exactly one warning is logged per missing asset across repeated opens.
  - **Confirmed 2026-07-28**: single-warning behavior confirmed alongside 4.1. TESTING.md `3451f8d1`.

## 5. In-game verification

- [x] 5.1 Toggle ON, asset present: open the Lectern → read/editor page shows its backdrop; content is
  legible over the art. (Note: the settings page is the standalone window, not a Lectern view — no
  distinct backdrop spec applies there.)
  - **Confirmed 2026-07-26** (playtest submission 2026-07-26T00-36-12, TESTING.md `a23a57e9`): "Works."
    Backdrop draws in both read and editor views with Pixel-Art ON, text legible.
- [~] 5.2 Toggle ON, settings PNG missing: no crash, one logged warning, structure renders normally.
  - Covered by §4.1/4.2 (same test — the missing-asset fallback is the one remaining in-game check).
- [x] 5.3 Toggle OFF: neither page draws a backdrop; both render as the plain LibGUI fallback.
  - **Confirmed 2026-07-26** (playtest submission 2026-07-26T00-36-12, TESTING.md `da001e4a`): "Works."
- [x] 5.4 Cache/unload: open, close, reopen the Lectern well after startup → backdrop still renders
  (survived asset unload) and is not re-decoded per open.
  - **Confirmed 2026-07-26** (playtest submission 2026-07-26T00-36-12, TESTING.md `ccaee4e2`): "Works."
- [x] 5.5 Record the LibGUI backdrop legibility/behavior verdict during this pass.
  - **Obsolete 2026-07-28** (TESTING.md `0ebd6a06`): user retired this verdict — legibility addressed
    another way. No legibility problem reported over the current art.

## 6. Documentation

- [x] 6.1 Append a LibGUI backdrop lesson to `VSAPI-NOTES.md` (`## LibGUI`): self-load backdrops via
  `TryGet(loadAsset: true)` + `SKBitmap.Decode`; `Container` paints its `BoxStyle.Texture` behind its
  child (no `Stack` needed); `BoxStyle.Texture`/`Image` filter bilinear (soft on pixel upscale, crisp on
  downsample) vs `NineSliceBox` = nearest-neighbor/crisp; cache the bitmap (and null) on the mod system,
  dispose on `Dispose()`, never in the dialog; flat-placeholder-color fallback on a missing PNG.
