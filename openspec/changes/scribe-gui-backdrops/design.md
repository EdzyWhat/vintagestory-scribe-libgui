## Context

The Scribe Lectern dialog (`GuiDialogScribeLecternLibGui`, `src/Mod/`) renders its widgets over a
transparent window body. The user wants each Scribe window to read as a themed physical object via
hand-authored art, with the mechanism supporting *distinct* per-view illustrations where an item exposes
more than one backdrop-bearing view, and wants the whole structure to be exercisable in-game with
flat-color placeholders *before* any art is drawn (art is a later, non-blocking swap).

This change is Phase 2 (workstream A of the approved plan): the reusable backdrop mechanism. It
**depends on the sibling change `scribe-themed-toggle`** (assumed landed), which adds the
`PixelArtDisplay` boolean to `ScribePlayerSettings` (client-local, persisted in
`scribe-hud-config.json`, live-propagated via `UpdateMySettings` → `MyPinsChanged` → `ForceRebuild`)
and the light/dark theme split. Backdrops are gated on that toggle: ON draws art, OFF draws the plain
LibGUI fallback. This change does **not** introduce the toggle, the light theme, animated tabs, or the
pin editor.

> NOTE (2026-07-26, reconciled to shipped): two things landed differently from this design's original
> assumptions. (1) The preference shipped as `PixelArtDisplay`, not `ThemedBackgrounds` (same boolean,
> renamed by the sibling change) — read every `ThemedBackgrounds` below as `PixelArtDisplay`. (2) The
> Lectern's in-dialog settings view was removed in the 2026-07-25 pivot (the gear now opens the standalone
> settings window, which follows the global theme and is not backdrop-wrapped), so the Lectern backs its
> single body (read + editor share the `LecternPage` spec). The distinct-per-view mechanism is retained
> and the `LecternSettings` spec is defined and reserved for a future item; the "distinct read/editor vs
> settings" goal below applies to the mechanism's capability, not to the Lectern as shipped.

Established rendering facts from exploration (ground truth):
- `Container` + `BoxStyle.Texture` (and the `Image` widget) filter **bilinear**
  (`SKFilterQuality.Medium`, `CanvasDrawExtensions` ~:806) — stretches/downsamples smoothly. Great for
  ink/painterly art authored ≥ on-screen size (only ever downsamples = crisp); **blurs upscaled hard
  pixel art**. `NineSliceBox` is the only nearest-neighbor (crisp) path.
- A `Container`/box paints its fill+texture **before** its child (`RenderObject.Paint`) → the texture
  sits behind the content automatically; **no `Stack` needed** for a background.
- **Asset-load trap:** `SkiaAssetLoader.LoadBitmap` and the `Image` widget call `TryGet(loc)` **without
  `loadAsset: true`** → null after startup. The mod must self-load the `SKBitmap` via
  `TryGet(loc, loadAsset: true)` + `SKBitmap.Decode`, exactly like the existing SVG icon loader
  (`ScribeModSystem.RegisterSvgIcon` ~:217).
- The existing flat-tan placeholder `assets/scribe/textures/gui/lecternbackdrop.png` is reusable as the
  Lectern page backdrop until real art lands.

## Goals / Non-Goals

**Goals:**
- A reusable, per-item / per-view backdrop mechanism: any item/view declares its own art of any size.
- Draw a backdrop behind dialog content via `Container` + `BoxStyle.Texture` when themed mode is ON.
- A flat-placeholder-color fallback when the PNG is missing, so the full structure is testable in-game
  before any art exists (the user's flat-color-first build strategy) — no crash, one logged warning.
- Support for distinct per-view backdrops (a per-view spec) where an item exposes more than one
  backdrop-bearing view. (The Lectern as shipped backs its single body — see the reconciliation note.)
- Self-loaded, shared, cached bitmaps that survive VS post-startup asset unload and are not reloaded
  per open; disposed on mod-system dispose.
- No backdrop drawn (plain fallback) when themed mode is OFF.

**Non-Goals:**
- The `PixelArtDisplay` toggle itself and the light theme (sibling `scribe-themed-toggle`).
- Animated navigation tabs (sibling `scribe-animated-tabs`) and the slide-out pin editor
  (sibling `scribe-pin-editor`).
- Shipping final art. Only the reused flat placeholder and placeholder colors are in scope.
- Any `src/Core/` change — backdrop code is all VS/Skia and stays in `src/Mod/`.
- Crisp pixel-art backdrops via `NineSliceBox` — that path is for framed pixel chrome (tabs), not
  full-spread backdrops, and is out of scope here.

## Decisions

### D1: `Container` + `BoxStyle.Texture`, not the `Image` widget or `NineSliceBox`
The backdrop is composed by wrapping a view's content in a `Container` whose `BoxStyle.Texture` is the
loaded bitmap. `Container` paints its texture before its child (`RenderObject.Paint`), so the art sits
behind the content with no `Stack` layer.
- *Why not the `Image` widget:* `Image` / `SkiaAssetLoader.LoadBitmap` call `TryGet(loc)` without
  `loadAsset: true`, so the bitmap is null after VS unloads assets post-startup — the backdrop would
  silently vanish in normal play. Self-loading + feeding the bitmap to `BoxStyle.Texture` sidesteps
  this.
- *Why not `NineSliceBox`:* it is the only nearest-neighbor (crisp) path, meant for framed pixel-art
  chrome (the tab chrome in the sibling `scribe-animated-tabs`). Full-spread backdrops are authored as
  ink/painterly art ≥ on-screen size, where bilinear only ever downsamples (crisp). `NineSliceBox`
  would also impose a fixed nine-slice frame model that a full illustration does not want.
- *Bilinear is acceptable here* precisely because the art contract (D4) is "author large, ink-style" —
  bilinear only blurs *upscaled hard pixel art*, which backdrops are not.

### D2: Self-load the bitmap with `loadAsset: true`, mirroring the SVG icon loader
`ScribeModSystem` gains `GetBackdropBitmap(AssetLocation)`: `TryGet(loc, loadAsset: true)`,
`SKBitmap.Decode(asset.Data)`, returning the decoded bitmap or null. This mirrors the existing
`RegisterSvgIcon` precedent (~:217) that already documents and defeats the post-startup unload trap.
- *Why:* the `Image`/`LoadBitmap` path is null after startup (D1); self-loading with `loadAsset: true`
  re-loads an unloaded asset on demand (`if (!value.IsLoaded() && loadAsset) value.Origin.TryLoadAsset`).
- *Alternative rejected:* using the `Image` widget for convenience — it fails in exactly the normal
  post-startup case, which is when the dialog is actually used.

### D3: Cache on the mod system, not the dialog
The decoded bitmap (and a null result) is cached in a `Dictionary<AssetLocation-or-string, SKBitmap?>`
on `ScribeModSystem`. One immutable bitmap is shared across every dialog open; the cache — including
null entries, so a missing asset logs its warning once, not per open — is disposed in
`ScribeModSystem.Dispose()`. A dialog NEVER disposes a backdrop bitmap.
- *Why the mod system, not the dialog:* the bitmap is immutable, reused across many opens of the same
  and different dialogs, and outlives any single dialog. Caching per-dialog would reload/re-decode on
  every open and risk a dialog disposing a bitmap another open still references. The mod system already
  owns comparable long-lived client resources (registered icons) and has a `Dispose()` seam.
- *Cache the null result too:* so an unloadable asset warns exactly once and repeat opens don't retry
  the failing load every frame — satisfying the "one logged warning" requirement.

### D4: Flat-placeholder-color fallback so structure is testable before art
`ScribeBackdrop.Wrap(capi, spec, placeholder, child)` asks the mod system for the bitmap; if it is
null, the `Container` uses `new BoxStyle { Color = placeholder }` instead of `{ Texture = bmp }`. The
whole dialog structure is therefore visible and testable in-game before any PNG is drawn — the user's
explicit flat-color-first request. Each view passes its own placeholder color, so read/editor and
settings look distinct even with zero art.
- *Why:* the build strategy is "structure now, art later, non-blocking." A missing PNG must never
  block; it must degrade to a visible flat panel, not an exception or an invisible body.
- *Alternative rejected:* drawing nothing (bare content) on a missing asset — that hides which view is
  which and makes the "distinct per-view" behavior untestable until art exists.

### D5: Gate on `PixelArtDisplay`; bare body when OFF
`ScribeBackdrop.Wrap` is called only when `modSystem.MySettings.PixelArtDisplay` is ON (read fresh
each `Build()`, following the `WindowFontScale` → `ScribeRowStyle.FromSettings` precedent at
`GuiDialogScribeLecternLibGui.cs` ~:845). When OFF, the body is used bare — no `Container` wrap — under
the plain LibGUI fallback theme, so the mod is fully usable with zero art.
- *Why read fresh each build:* `UpdateMySettings` already fires `MyPinsChanged` → the open dialog
  `ForceRebuild`s, so a toggle flip repaints backdrops live for free; no new event or subscription.

## Risks / Trade-offs

- **[Risk] Bilinear blurs upscaled hard pixel art.** → Mitigation: the art contract requires ink-style
  illustration authored ≥ on-screen (ideally ~2× logical) size, where `BoxStyle.Texture` only ever
  downsamples (crisp). Crisp pixel chrome uses `NineSliceBox` in the sibling tabs change, not here.
- **[Risk] Art can overwhelm the writing zone, hurting text legibility.** → Mitigation: the art
  contract reserves a calm, light writing zone in the central ~70% (below the transparent title bar);
  if a backdrop still fights the text in-game, apply a `textColor` override or a semi-opaque content
  panel over the art. Center legibility over decorative fidelity.
- **[Risk] Asset-unload trap — a backdrop loaded via the naive path is null in normal play.** →
  Mitigation: self-load with `loadAsset: true` (D2); the icon loader already proves this survives
  unload.
- **[Risk] A missing PNG could spam warnings or crash.** → Mitigation: cache the null result (D3) so
  exactly one warning is logged and the fallback color is used thereafter (D4); no crash.
- **[Trade-off] Backdrop bitmaps live for the client session.** Accepted: they are few, immutable, and
  shared across every open; the cache is disposed on mod-system dispose. This matches the icon loader's
  lifetime.

## Art-authoring contract

Deliver each PNG to `assets/scribe/textures/gui/` (i.e. `src/Mod/assets/scribe/textures/gui/`); the
structure works with flat placeholders until then.
- **Native size:** author full page/settings spreads as ink/painterly art at **~2× the logical window
  size** so `BoxStyle.Texture`'s bilinear filter only ever downsamples (crisp). Report the PNG's native
  size back when delivered — nothing in code assumes a shared size.
- **Writing zone:** reserve a **calm, light writing zone** in the central ~70% of the spread (where
  text renders, below the transparent title bar); decorative detail lives in the margins. Warm limited
  palette (cream parchment center, sepia ink, leather/wood margins per the desktop references).
- **Per-view distinctness:** where an item exposes both a page and a settings view, they are *separate*
  PNGs (distinct specs). For the Lectern as shipped there is one body: the reused `lecternbackdrop.png`
  stands in until its final art is drawn. The reserved `LecternSettings` PNG is a placeholder color until
  a future item's settings view exists to draw it.
- **Legibility check:** on delivery, verify text reads clearly over the art in-game; if not, apply a
  `textColor` override or a semi-opaque content panel.

## Verification (in-game only)

The Core suite cannot reach `src/Mod/` GUI or the VS API. Verify in-game:
1. **Present asset (toggle ON):** open the Lectern → the dialog body (read and editor views) shows its
   backdrop (the reused flat-tan `lecternbackdrop.png` or its final art); art reads correctly over/through
   the content.
2. **Missing asset (toggle ON):** with the body PNG temporarily absent → the body draws its flat
   placeholder color, the dialog does not crash, and exactly one warning is logged.
3. **Toggle OFF:** neither view draws a backdrop; both render as the plain LibGUI fallback.
4. **Caching / unload:** open, close, and reopen the Lectern well after startup → the backdrop still
   renders (survived asset unload) and is not re-decoded per open.
5. Record the LibGUI backdrop lesson in `VSAPI-NOTES.md`.
