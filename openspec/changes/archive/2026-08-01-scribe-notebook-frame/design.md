## Context

The Lectern dialog (`GuiDialogScribeLecternLibGui`, `src/Mod/`) draws a backdrop via the
`scribe-gui-backdrops` mechanism: `ScribeBackdrop.Wrap` wraps the view body in a `Container` whose
`BoxStyle.Texture` is a self-loaded, cached `SKBitmap` (`ScribeModSystem.GetBackdropBitmap`). It points at
a flat placeholder and the window is 567×520 using LibGUI's stock `WindowFrame`.

Ground-truth rendering facts (verified against `reference/vslibgui/`):
- `BoxStyle.Texture` is **hard-coded stretch-to-fill** — `RenderBox.PaintInternal → DrawMaskedBox → 
  canvas.DrawBitmap(texture, wholeBoxRect, paint)`, no source rect, no `BoxFit` (`CanvasDrawExtensions.cs`).
  A matching box aspect makes scaleX == scaleY → distortion-free.
- `Container{Texture, child}` paints the texture BEFORE its child (`RenderBox.cs:192-221`), so art sits
  behind content with no `Stack`.
- `SizedBox(w,h)` sets tight fixed constraints (`RenderConstrainedBox`); fixed children compose in
  `Row`/`Column` (`RenderFlex`). `Align(Alignment.BottomCenter)` bottom-anchors + centers a child.
  `CrossAxisAlignment.Stretch` on a `Row` makes fixed-width columns take full height.
- Dragging is owned by `GuiBase` via `WindowConfig.DragHandleHeight` (NOT `WindowFrame`); closing is just a
  handler. So a dialog can drag + close without `WindowFrame`.
- `WindowFrame`'s title bar only paints a solid color and sits as a strip above its child — it cannot show
  the backdrop art behind it as the outermost box. Hence we drop it for this dialog.

Real notebook art is 1024×1160 (aspect 0.883). `src/Core/` must not reference the VS API.

## Goals / Non-Goals

**Goals:**
- Render the real notebook art un-distorted as the outermost `OuterArtBox`, framing the functional GUI.
- Express the whole layout proportionally from ONE width `W` ("Pixel Art Size"), readable in the IDE.
- A draggable TitleBar band with title text + SVG buttons (close = delete SVG at 1.4×); a three-column
  SectionInnerBox (side / tasks / side), with the tasks column holding the existing scrolling content and
  the right column a vertical icon-button nav stack (tooltipped).
- `W` is a permanent, live-adjustable preference (300..1000, step 10, default 600).

**Non-Goals:**
- Aspect-fit widgets / painting outside the frame (the aspect-sized OuterArtBox makes them unnecessary).
- The OFF-background reversal and pinned-row contrast bump (separate follow-ups).
- Final tuned ratio constants beyond what's specified here.
- Any `src/Core/` VS-API coupling.

## Decisions

### D1: One driving width `W`, a `LecternLayout` record derives the rest
All dimensions are computed from `W` in a `readonly record struct LecternLayout(float W)`:
`H = W*1160/1024`, `TitleBarH = 0.13H`, `InnerW = 0.9W`, `InnerH = 0.8H`, `SideColW = 0.0675W`,
`TasksColW = 0.765W`, `TitleBtnsW = 0.75W`, `TitleBtnsH = 0.065H`. The tree references `L.TasksColW` etc.,
so it reads as ratios. The three column widths sum to `InnerW` exactly (0.0675+0.765+0.0675 = 0.9) → no
overflow. The ~7% of `H` unused by TitleBar+Inner is bottom margin.
- *Alternative rejected — flex weights for the columns:* 0.0675:0.765:0.0675 → 27:306:27 reads terribly and
  is fragile; absolute `SizedBox` from the known `W` is cleaner (LibGUI supports both).

### D2: Size the dialog to the art aspect; drop `WindowFrame`
`CreateWindowConfig().Size = (W, H)`, `Resizable = false`. Build the tree directly (no `WindowFrame`) so the
backdrop `Container` is the outermost box and the art frames everything. Drag comes from
`WindowConfig.DragHandleHeight = TitleBarH`; close is a close-SVG `IconButton` calling `TryClose()`.
- *Alternative rejected — keep `WindowFrame`:* its bar can't show art behind it and sits as a strip above
  the OuterArtBox, offsetting content and fighting the structure (confirmed in source).

### D3: The widget tree
```
Container(style:{Width=W, Height=H, Texture=bmp || Color=placeholder},   // OuterArtBox, PixelArtDisplay-gated
  child: Column(crossAxisAlignment: Center)[
    SizedBox(W, TitleBarH,                                               // TitleBar band = drag zone
      child: Align(BottomCenter,
        child: SizedBox(TitleBtnsW, TitleBtnsH, child: TitleTextButtonsRow))),
    SizedBox(InnerW, InnerH,                                             // SectionInnerBox
      child: Row(crossAxisAlignment: Stretch)[
        SizedBox(SideColW, child: SectionLeftCol),
        SizedBox(TasksColW, child: LecternTasksBox),                     // existing scrolling content
        SizedBox(SideColW,  child: SectionRightCol)])])])                // Column of nav IconButtons
```
`TitleTextButtonsRow` = `Row(spaceBetween)[ Text(title, window-text×1.1, left), Row[ …SVG IconButtons, close ] ]`
with a tooltip per button. `SectionRightCol` = `Column[ Settings(gear SVG), Read, Edit(feather SVG),
Pinned(pin SVG) ]`, tooltipped. Icon choices: Settings = existing `scribegear`; Edit = existing `scribeedit`
(feather); Pinned = existing `scribepin`; **Read = a plain "R" text button for now** (placeholder — to be
replaced with the checkbox's check SVG later). `LecternTasksBox` hosts the current `BuildCentralRegion()`
scroll content unchanged (it gains height for free from the taller box).

### D4: `PixelArtDisplay` gate + placeholder fallback preserved
When `PixelArtDisplay` is OFF, the OuterArtBox `Container` is used bare (no texture) — the existing gate.
When the art PNG is missing/unloadable, the flat placeholder `Color` is used (existing fallback). The gate
and fallback move to this root `Container`.

### D5: `PixelArtSize` (`W`) — one permanent live preference, read fresh
Add `int PixelArtSize` to `ScribePlayerSettings` (default 600) with `Min=300`, `Max=1000`,
`ClampPixelArtSize` (clamp; snap to the nearest 10), wired into `Normalized()`, mirroring the
`HudRowWidth`/`ClampHudRowWidth` precedent. Surface it in `ScribeSettingsContent`'s Appearance section as an
`IntField` (step 10) labeled "Pixel Art Size". `Build()` reads `MySettings.PixelArtSize` fresh each pass
(like `PixelArtDisplay`/`RowStyle`), so `UpdateMySettings → MyPinsChanged → ForceRebuild` re-lays-out the
open Lectern live. Because the window `Size` derives from `W`, changing it also resizes the dialog on the
rebuild.
- *Note:* unlike the earlier draft's offset/padding knobs, `W` is a PERMANENT preference, not a temporary
  tuning aid.

## Risks / Trade-offs

- [At `W`=300 the tasks column is ~230px — cramped for editing] → Accepted; the {300,1000} range is the
  user's call and 300 is a hard floor, not the default (600).
- [Dropping `WindowFrame` means reimplementing drag/close/minimize ourselves] → Drag is `GuiBase`/
  `DragHandleHeight` (free); close is an `IconButton` → `TryClose()`. Minimize is dropped unless needed.
  Verify drag works over the TitleBar band in-game.
- [Art distortion if the dialog aspect drifts] → `Resizable=false` removes the only drift path; keep `Size`
  in sync with any future art re-crop.
- [`W` out of range or hand-edited] → `ClampPixelArtSize` + `Normalized()` bound and snap it on load.
- [Window `Size` must track `W` at open, but `CreateWindowConfig` runs once per open] → read `W` in
  `CreateWindowConfig` from `MySettings`; a live `W` change re-lays-out content via `ForceRebuild`, and the
  window resizes to match on the next open (document any in-session resize limitation during verification).

## Migration Plan

Additive. The new `PixelArtSize` key defaults from its initializer when absent, so existing
`scribe-hud-config.json` files load cleanly. Rollback = revert the mod changes + art swap. The reserved
`lecternbackdrop.png` stays in place.

## Open Questions

- Does the window need to resize mid-session when `W` changes, or is "content re-lays-out live, window size
  applies on next open" acceptable? (Resolve in verification; `CreateWindowConfig` timing dictates this.)
- Read-view nav icon is a plain "R" text button for now (placeholder), to be replaced with the checkbox's
  check SVG later; Settings/Edit/Pinned reuse the existing `scribegear`/`scribeedit`/`scribepin` SVGs.
