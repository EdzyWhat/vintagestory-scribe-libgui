## 1. ScribeModSystem: self-loaded backdrop bitmap cache

- [ ] 1.1 Add a `Dictionary<string, SKBitmap?>` backdrop cache field to `ScribeModSystem` (keyed by the
  asset location string; caching a null result too).
- [ ] 1.2 Add `public SKBitmap? GetBackdropBitmap(AssetLocation loc)`: return the cached entry if
  present; otherwise `TryGet(loc, loadAsset: true)`, `SKBitmap.Decode(asset.Data)`, cache and return the
  result. Mirror the self-loading pattern of `RegisterSvgIcon` (~:217) so it survives post-startup asset
  unload.
- [ ] 1.3 On an absent/unloadable asset, cache `null` and log **exactly one** warning for that location
  (so repeat opens do not re-warn or re-attempt the failing load every frame).
- [ ] 1.4 Dispose every cached `SKBitmap` and clear the cache in `ScribeModSystem.Dispose()`; ensure no
  dialog ever disposes a backdrop bitmap.

## 2. ScribeBackdrop.cs: spec record + per-view specs + Wrap helper

- [ ] 2.1 Add `src/Mod/ScribeBackdrop.cs` with `internal sealed record ScribeBackdropSpec(AssetLocation
  Texture)` — no size field, nothing assumes a shared dimension.
- [ ] 2.2 Add a `ScribeBackdrops` holder exposing per-item / per-view specs: `LecternPage` (reusing the
  existing `textures/gui/lecternbackdrop.png`) and `LecternSettings` (its own
  `textures/gui/lecternsettingsbackdrop.png`, placeholder until art lands). Leave room for
  Desk/Notebook/Tablet page+settings specs as they ship.
- [ ] 2.3 Add `static Widget Wrap(ICoreClientAPI capi, ScribeBackdropSpec spec, Vector4 placeholder,
  Widget child)`: fetch the bitmap via `GetBackdropBitmap`; build a `Container` with
  `new BoxStyle { Texture = bmp }` when non-null, else `new BoxStyle { Color = placeholder }`; return it
  wrapping `child`. Rely on `Container` painting its texture behind the child (no `Stack`).

## 3. Wire Lectern page vs settings backdrops (gated on the toggle)

- [ ] 3.1 In `GuiDialogScribeLecternLibGui.Build()`, read `modSystem.MySettings.ThemedBackgrounds` fresh
  each build (following the `ScribeRowStyle.FromSettings` per-build precedent ~:845).
- [ ] 3.2 When themed mode is ON, wrap the read/editor body in `ScribeBackdrop.Wrap(..., LecternPage,
  <page placeholder color>, body)` and the settings body in `ScribeBackdrop.Wrap(..., LecternSettings,
  <distinct settings placeholder color>, body)` — distinct specs and distinct placeholder colors so the
  two pages are distinguishable even with zero art.
- [ ] 3.3 When themed mode is OFF, use each body bare (no `Container` wrap) so the dialog renders the
  plain LibGUI fallback with no backdrop.

## 4. Placeholder-fallback behavior

- [ ] 4.1 Confirm a missing/unloadable PNG yields the flat placeholder color path (via cached-null),
  the dialog renders its structure over it, and no exception is thrown.
- [ ] 4.2 Confirm exactly one warning is logged per missing asset across repeated opens.

## 5. In-game verification

- [ ] 5.1 Toggle ON, asset present: open the Lectern → read/editor page shows its backdrop; switch to
  settings → a *distinct* backdrop; content is legible over the art.
- [ ] 5.2 Toggle ON, settings PNG missing: settings page shows its flat placeholder color, no crash,
  one logged warning; structure renders normally.
- [ ] 5.3 Toggle OFF: neither page draws a backdrop; both render as the plain LibGUI fallback.
- [ ] 5.4 Cache/unload: open, close, reopen the Lectern well after startup → backdrop still renders
  (survived asset unload) and is not re-decoded per open.
- [ ] 5.5 Record the LibGUI backdrop legibility/behavior verdict during this pass.

## 6. Documentation

- [ ] 6.1 Append a LibGUI backdrop lesson to `VSAPI-NOTES.md` (`## LibGUI`): self-load backdrops via
  `TryGet(loadAsset: true)` + `SKBitmap.Decode`; `Container` paints its `BoxStyle.Texture` behind its
  child (no `Stack` needed); `BoxStyle.Texture`/`Image` filter bilinear (soft on pixel upscale, crisp on
  downsample) vs `NineSliceBox` = nearest-neighbor/crisp; cache the bitmap (and null) on the mod system,
  dispose on `Dispose()`, never in the dialog; flat-placeholder-color fallback on a missing PNG.
