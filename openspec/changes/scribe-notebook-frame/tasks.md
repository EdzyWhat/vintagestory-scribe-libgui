## 1. Art asset + backdrop spec

- [x] 1.1 Copy `~/Desktop/Lectern/Scribe-Notebook-1024.png` (1024×1160) into
  `src/Mod/assets/scribe/textures/gui/scribe-notebook.png`.
- [x] 1.2 In `src/Mod/ScribeBackdrop.cs`, repoint `ScribeBackdrops.LecternPage` to
  `textures/gui/scribe-notebook.png` (leave `lecternbackdrop.png` as the reserved placeholder;
  `Wrap` / `GetBackdropBitmap` unchanged).

## 2. Pixel Art Size preference (Core)

- [x] 2.1 In `src/Core/ScribePlayerSettings.cs`, add `int PixelArtSize` (default 600) with `MinPixelArtSize`
  (300) / `MaxPixelArtSize` (1000) consts and a `ClampPixelArtSize` static that clamps AND snaps to the
  nearest 10, mirroring the `HudRowWidth`/`ClampHudRowWidth` precedent. No VS API.
- [x] 2.2 Wire `PixelArtSize = ClampPixelArtSize(PixelArtSize)` into `Normalized()`.

## 3. LecternLayout record + window sizing

- [x] 3.1 Add a `readonly record struct LecternLayout(float W)` (in `GuiDialogScribeLecternLibGui.cs` or a
  small new file) exposing `H = W*1160/1024`, `TitleBarH = 0.13H`, `InnerW = 0.9W`, `InnerH = 0.8H`,
  `SideColW = 0.0675W`, `TasksColW = 0.765W`, `TitleBtnsW = 0.75W`, `TitleBtnsH = 0.065H`.
- [x] 3.2 In `CreateWindowConfig()`, read `MySettings.PixelArtSize` → `W`, set `Size = new Vector2(W, H)`
  from a `LecternLayout`, `Resizable = false`, and `DragHandleHeight = TitleBarH` so the TitleBar band drags.

## 4. Build() restructure — OuterArtBox tree (no WindowFrame)

- [x] 4.1 Replace the `WindowFrame`-based `Build()` with the OuterArtBox tree: root backdrop `Container`
  (`{Width=W, Height=H, Texture=bmp}`, PixelArtDisplay-gated with the flat-placeholder fallback) →
  `Column(Center)[ TitleBar band, SectionInnerBox ]`. Read `W` fresh each build from a `LecternLayout`.
- [x] 4.2 TitleBar band: `SizedBox(W, TitleBarH, child: Align(BottomCenter, child: SizedBox(TitleBtnsW,
  TitleBtnsH, child: TitleTextButtonsRow)))`. `TitleTextButtonsRow` = title `Text` (window-text ×1.1) on the
  left + a right-aligned `Row` of SVG `IconButton`s; close button reuses the delete SVG at 1.4× the delete
  size and calls `TryClose()`. Tooltip per button.
- [x] 4.3 SectionInnerBox: `SizedBox(InnerW, InnerH, child: Row(Stretch)[ SizedBox(SideColW, LeftCol),
  SizedBox(TasksColW, LecternTasksBox), SizedBox(SideColW, RightCol) ])`. `LecternTasksBox` hosts the
  existing `BuildCentralRegion()` scroll content unchanged.
- [x] 4.4 SectionRightCol: a `Column` of nav buttons, each tooltipped — Settings (`scribegear` → `OpenSettings`),
  Read (a plain "R" text button placeholder for now; swap to the checkbox check SVG later), Edit (`scribeedit`
  feather), Pinned (`scribepin`). Wire handlers to existing view-switch / settings entry points.
- [ ] 4.5 Confirm the functional content stays fully interactive over the backdrop and that drag works over
  the TitleBar band.

## 5. Pixel Art Size in settings form + lang

- [x] 5.1 In `src/Mod/ScribeSettingsContent.cs` `BuildAppearanceSection`, add a `LabeledControl` +
  `IntField` for Pixel Art Size (step 10), reusing the existing keyed-numeric-field helpers.
- [x] 5.2 Add `scribe:settings-pixelartsize` + `scribe:settings-pixelartsize-help` and any new nav/close
  button tooltip strings to `src/Mod/assets/scribe/lang/en.json`.

## 6. Build, restage, verify

- [x] 6.1 `dotnet build src/Mod/Mod.csproj` clean; Core test suite green (PixelArtSize clamp/snap).
- [x] 6.2 Restage Debug (`bash build/restage.sh Debug`) and fully relaunch the client.
- [x] 6.3 In-game (Pixel-Art ON): notebook art renders un-distorted as the OuterArtBox; the TitleBar band
  drags the window; close button (1.4× delete SVG) closes it; the three-column SectionInnerBox frames the
  scrolling content; right-column nav icons work with tooltips; text legible. (Confirmed 2026-07-26 —
  "Works! I love it!"; close-button hitbox offset tracked as a bug in group 7.)
- [x] 6.4 In-game: change Pixel Art Size in Scribe Settings → the open Lectern rescales live (note whether
  the window resizes in-session or only on next open); values persist across a relog; clamp/snap on
  hand-edit; window is not resizable. (Confirmed 2026-07-26 — live re-layout works, persists, not resizable;
  the window-canvas-vs-content resize gap is the bug in group 7.)
- [x] 6.5 Update `TESTING.md` with the new in-game items and record any follow-ups (e.g. the deferred custom
  over-art header proposal).

## 7. Bugfix pass (post-playtest 2026-07-26)

- [x] 7.1 **Art canvas tracks live `W`.** The window `Size` (hence the OuterArtBox art `Container`'s `W × H`
  canvas) is set only in `CreateWindowConfig`, which `GuiBase.TryOpen` runs ONCE per open. A live Pixel Art
  Size change re-lays-out the content tree but leaves the window `_layoutSize` at the opened W, so raising W
  clamps the art canvas to the stale size while the inner `SizedBox`es grow → the structures spill past the
  art (repro: open at 600, set 300, relog, then 300→600 — art can't grow past 300 until the next relog).
  FIXED: `OnRenderGUI` now compares the live-`W` `LecternLayout` size to the current `WindowSize` and, when
  they differ, re-applies `WindowSize` + calls the base `SyncLayoutSize()` (documented for a programmatic
  `WindowSize` change) so the root re-lays-out at the new tight constraints. No-op when `W` is unchanged
  (`SyncLayoutSize` early-returns). Awaiting in-game retest.
- [~] 7.2 **Close-button hitbox alignment.** The title-bar close button's clickable area doesn't coincide
  with its drawn border — the hit region sits above/left of the visible button (reported on macOS).
  INVESTIGATED, NO CODE FIX THIS PASS: traced every layer of the button's tree in `reference/vslibgui/` —
  `Align`(`RenderPositionedBox`), `Row`, `SizedBox`, and the `Tooltip`'s `CompositedTransformTarget`
  (`RenderTarget : RenderProxyBox`, a clean 0,0 passthrough). Hit-testing applies each child offset via
  `Element.HitTest` → `RenderObject.GlobalToChild`, matching paint's `PaintChildren` translate — so there's
  no static layout math error to correct. This is the SAME visual-vs-clickable mismatch VSAPI-NOTES already
  records for the NATIVE close button (confirmed via live mouse-coord logging to be a Retina/`GUIScale`
  rendering-vs-hitbox artifact, not a layout bug). Deferred rather than guess-patching LibGUI internals; the
  settling diagnostic is a live hover-coordinate log at 100% GUIScale on a non-Retina display (see
  VSAPI-NOTES).
- [x] 7.3 **Title text padding.** Add 4px `padding-left` to the title `Text` in the TitleTextButtons row so it
  isn't flush against the row's left edge. FIXED: title `Text` wrapped in `Padding(EdgeInsets.Only(left: 4))`.
- [ ] 7.4 Rebuild, restage Debug, and re-verify in-game: raising Pixel Art Size above the opened size grows
  the art canvas live (no spill, no relog needed); the title text has its left padding. (Restaged Debug
  2026-07-26; 7.2 hitbox remains a known deferred item, not part of this retest.)
