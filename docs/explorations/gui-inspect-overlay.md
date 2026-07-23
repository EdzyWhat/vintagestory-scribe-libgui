# Exploration: GUI "Inspect Element" overlay for the lectern

**Status:** exploring (not yet an OpenSpec change). Created 2026-07-22.

> **Temporary holding pen.** OpenSpec has no native "exploration" artifact, so this is a plain
> repo doc kept only while the design is still fluid (mirrors `lectern-row-list-rework.md`). When
> the `add-gui-inspect-overlay` change is opened via `openspec-propose`, this file's content
> migrates into that change's `proposal.md` / `design.md` (Decisions + Open Questions) and **this
> file is deleted**. Do not treat it as a durable spec.

## Context — why this exists

The author wants a browser-style "Inspect Element" for the lectern's custom UIs: point at a box
and see what drives its size/padding. Today that's invisible — you can't tell from pixels which
config field or formula produced a given gap, and diagnosing it means reading `RowTextLayout` /
`ScribeRowElement` source and relaunching.

Two constraints kill the obvious answers:

- **There is no DOM.** The lectern GUI (`GuiDialogScribeLectern`) is native OpenGL drawn via
  `GuiComposer`/`ElementBounds`. A real browser inspector cannot attach.
- **The existing ImGui tuning path is dead on this machine.** VSImGui needs OpenGL 4.3; macOS
  Apple Silicon caps at 4.1, so the `#if DEBUG` sliders (`RegisterDebugSliders`) draw nothing and
  spam per-frame GL errors (see `VSAPI-NOTES.md` "VSImGui debug overlay"). ConfigLib's panel works
  but only *edits* values — it never shows which box a value drives.

**The find that makes this cheap:** Vintage Story already ships a GUI bounds-outline system.
`GuiComposer.Outlines` (public `static int`, 0/1/2) makes the composer call
`GuiElement.RenderBoundsDebug()` on every element, which strokes an outline via
`IRenderAPI.RenderRectangle(...)` — a plain `LineStrip` mesh with **no OpenGL 4.3 dependency, so
it renders fine on this Mac.** The engine gives us outlines for free; it just lacks *labels* and
doesn't name the *driving config field*. This change adds those.

Chosen direction (confirmed with the author): **an in-game inspect overlay toggled by a
`ScribeClientConfig` value (no hotkey)**, so it rides the ConfigLib panel — the one live-editing
path that works on this Mac. The dialog re-reads config on every open (`GuiDialogScribeLectern.cs:79`),
so toggling the value in the ConfigLib panel (or editing `scribe-client-config.json`) and reopening
the lectern shows/hides the overlay — the same loop as the existing live tuning. Ships in Release
automatically (it's just a config field; no `#if DEBUG`, no hotkey).

## Intended outcome

A config field — **`InspectOverlayMode` (int, default `0`)**, mirroring the engine's own
`GuiComposer.Outlines` convention: `0`=off, `1`=outlines+labels, `2`=outlines only (the escape hatch
for when labels crowd at small text size). When `>= 1`, the overlay renders over the *real* lectern
dialog in both views:

- Outlines every composed box (rows, columns, checkbox, affordances, controls, viewport, chrome).
- Labels each with its **element key + pixel size** (always), and its **driving config field /
  formula** (e.g. `TextX=64`, `TopContentGap=20`, `AffordanceButtonSizeFixed`) where known.
- Draws the **gaps** that aren't elements at all (`TopContentGap`, `ElementToDialogPadding`,
  `ScaledRowSpacing`, `ListToControlsGap`, `ControlRowGap`) as tinted bands labeled with their
  config field — this is the "which box drives this padding" answer that's impossible visually.

Off by default (`InspectOverlayMode == 0`), ships in Release (not `#if DEBUG` — the whole point is
inspecting on the Mac where Debug/ImGui is dead), one int-check per frame when off, vanilla API only
(no new dependency). Toggled via ConfigLib panel / config JSON, applied on lectern reopen.

## The box model (reference — what to outline)

Every box is deterministic from `ScribeClientConfig` + a few formulas. Nested outer→inner:

1. **Dialog outer** — `ElementStdBounds.AutosizedMainDialog`, centered.
2. **Dialog padding** — `GuiStyle.ElementToDialogPadding` inset around `bgBounds` *(a gap)*.
3. **Title bar** (engine) + `TopContentGap` band below it *(a gap)*.
4. **Row-list clip viewport** — `RowListWidth` × `VisibleListHeight`.
5. **Each row** — width `RowListWidth`, height `ScribeRowElement.RowHeightFixed(...)`; internal
   `TopPadFixed` + text + `BottomOverheadFixed`; inter-row gap `ScaledRowSpacing` *(a gap)*.
6. **Within-row columns** (`RowTextLayout.For`) — grip → checkbox → `CheckboxTextGap` → text; a
   right-anchored pin/delete square overlay sized by `AffordanceButtonSizeFixed`.
7. **Control rows** — text-size label+slider, collapse toggle, toolbar icons, switch button;
   `ControlRowHeight` boxes advanced by `ControlRowGap`, first offset by `ListToControlsGap`.

Single sources of truth the labels should re-derive from (never hardcode): `RowTextLayout.TextX`,
`ScribeRowElement.RowHeightFixed`, `AffordanceButtonSizeFixed`, `TopPadFixed`/`BottomOverheadFixed`.

## Engine primitives (all verified in the decompiled DLLs)

- `IRenderAPI.RenderRectangle(float x, float y, float z, float w, float h, int color)` — outline
  rect (LineStrip). macOS-safe. `color` = packed RGBA (`ColorUtil.ToRgba`). Draw at `z≈600` (above
  the dialog's ~500).
- `GuiElement.RenderBoundsDebug()` already does exactly this with `Bounds.renderX/renderY/
  OuterWidth/OuterHeight` — mirror its pattern.
- `GuiComposer.GetElement(key)` / `composer[key]` are **public** → `.Bounds` per known key. The
  `staticElements`/`interactiveElements` dicts are **internal** (full enumeration needs reflection —
  avoid on the main path).
- Labels: `capi.Gui.TextTexture.GenTextTexture(text, CairoFont, TextBackground)` → cache the
  `LoadedTexture` → `capi.Render.Render2DLoadedTexture(tex, x, y, z)`. **Dispose textures** in
  `OnGuiClosed` (GL leak otherwise).
- `ElementBounds` public geometry: `renderX`, `renderY`, `OuterWidth`, `OuterHeight`,
  `ChildBounds`, `PointInside`.

## Recommended implementation

**Toggle.** A new `ScribeClientConfig.InspectOverlayMode` (`int`, default `0`) — no hotkey, no
`ScribeModSystem` change. `0`=off, `1`=outlines+labels, `2`=outlines only. The dialog reads
`clientConfig.InspectOverlayMode` live each frame; because config is re-read on every dialog open
(`GuiDialogScribeLectern.cs:79`), toggling it in the ConfigLib panel and reopening the lectern flips
the overlay. Add it to the ConfigLib manifest (see below) so it's editable in-game on this Mac.

**Render site.** A `RenderInspectOverlay(float dt)` method on `GuiDialogScribeLectern`, called at
the **end** of the existing `OnRenderGUI` override (`GuiDialogScribeLectern.cs:251`), after
`base.OnRenderGUI(...)`, guarded by `clientConfig.InspectOverlayMode >= 1`. It needs the dialog's
private state (`SingleComposer`, `clientConfig`, `IsEditorMode`, `focusedEditIndex`,
`rowListContentBounds`) and must draw *outside* the row-list clip — so a separate `GuiDialog` or a
composed child element are both wrong (a child element gets torn down on every recompose and clipped
by the row scissor).

**Enumeration.** Iterate the **known keys the dialog itself composes** — fixed keys
(`"rowListScrollbar"`, `"rowEditInput"`, `"switchModeButton"`, `"textSizeSlider"`,
`"toolPanelToggleButton"`, `addTaskButton`) plus per-row keys via `ScribeBlockRowCell.PinKey(i)/
DeleteKey(i)/DragHandleKey(i)/ToggleKey(i)/TextKey(i)` for each block — resolving via
`SingleComposer.GetElement(key)?.Bounds`. Use the base `GetElement` (never the kind-specific
`GetSwitch/GetToggleButton/...`, which throw on the wrong kind — VSAPI-NOTES). `?.` cleanly skips
keys absent in the current view. Additionally draw the structural bounds the dialog already holds
(`rowListContentBounds`, clip bounds, `bgBounds`). Reflection over the internal dicts is an optional,
try/catch-guarded, off-by-default `IncludeUnknownElements` fallback — not the main path.

**Annotation.** A small static `key-pattern → driver-string` table in a new `ScribeInspectOverlay`
helper. Per-row keys format their driver live from the *same* `RowTextLayout.For(...)` /
`ScribeRowElement.*Fixed(...)` calls the compose uses, so labels can't drift from real layout. Every
label always shows `key + WxH` off `Bounds.OuterWidth/OuterHeight`; the driver string is layered on
top and degrades gracefully to key+size when a key has no table entry.

**Gaps** (not elements — no `Bounds`): draw from the same config values + neighboring enumerated row
bounds. `TopContentGap` band below the title bar; `ElementToDialogPadding` as the inset between
dialog bounds and `bgBounds`; `ScaledRowSpacing` between consecutive rows; `ListToControlsGap` /
`ControlRowGap` between controls. Label each with its config field name. (Precedent: `ScribeRowElement`
lines ~308-325 already tint pad bands with Cairo rects — same idea, at screen level via `RenderRectangle`.)

**Color/labels.** Color by category, not depth: chrome=white, viewport/content=cyan, rows=green,
affordances=orange, controls=magenta, gaps=yellow+faint fill. Label at each box's top-left inside
corner with a small font + opaque `TextBackground`; stagger affordance labels to the box bottom to
reduce overlap. `InspectOverlayMode == 2` skips labels entirely (outlines only) for when they crowd
at small text size. **Cache** label textures by string; regen only on change.

**No hotkey / no ModSystem change.** The overlay is driven purely by `InspectOverlayMode`. The
`ScribeInspectOverlay` helper stays stateless (pure draw functions taking `capi`, the box list, and
the mode) — no static toggle, no input registration.

## Files

| File | Change |
|---|---|
| `openspec/changes/add-gui-inspect-overlay/…` | **First:** propose via `openspec-propose` skill |
| `src/Mod/ScribeInspectOverlay.cs` | **New.** `InspectBox`/`InspectCategory` types, color + driver tables, `Render(...)`, gap helpers, label-texture cache + `Dispose()` (stateless — takes the mode) |
| `src/Mod/GuiDialogScribeLectern.cs` | `BuildInspectBoxes()` + `RenderInspectOverlay(dt)`; call from `OnRenderGUI` (:251) guarded by `InspectOverlayMode >= 1`; dispose cache in `OnGuiClosed` (:1159) |
| `src/Mod/ScribeClientConfig.cs` | **Add** `InspectOverlayMode` (int, default `0`); also the read-only source of field names/formulas the labels report |
| `src/Mod/assets/scribe/config/configlib-patches.json` | **Add** `InspectOverlayMode` as an int/dropdown (0–2) so it's toggleable in the in-game panel |
| `RowTextLayout.cs`, `ScribeRowElement.cs` | Read-only — the formulas the driver labels re-derive |

No `ScribeModSystem` change, no hotkey, no lang key. No `src/Core/` change (pure Mod-side — respects
the Core-must-not-reference-VSAPI invariant).

## Companion (separate, smaller change — optional)

`configlib-patches.json` exposes only 4 of ~30 knobs today. Expanding it is a **no-code** JSON edit
and is the cross-platform live-tuning path now that ImGui is dead here. Keep it as its own change
(`add-configlib-expand-knobs`): the overlay *reveals* which knob drives a box; ConfigLib *edits* it.
Expose the ~15 layout/size knobs first (`TaskRowHeight`, `ToggleWidth`, `CheckboxTextGap`,
`ControlRowHeight`, `ControlRowGap`, `ListToControlsGap`, `ToolbarIcon*`, `SwitchButtonWidth`,
`MinRowHeight`, `MinAffordanceButtonSize`); defer the 12+ RGBA color channels and the
`PinnedIndicatorMode` enum (panel gets long).

## OpenSpec framing

- **Change id:** `add-gui-inspect-overlay`
- **New capability spec:** `debug-inspect-overlay` — scenarios: off by default (`InspectOverlayMode == 0`);
  `InspectOverlayMode >= 1` renders the overlay; outlines every keyed element with a key+size label;
  labels the driving field/formula where known; draws gap bands; works in read + editor views; ships
  in Release; toggle is editable in the ConfigLib panel.
- **Task order:** (1) propose change; (2) add `InspectOverlayMode` to `ScribeClientConfig` +
  `configlib-patches.json`; (3) `ScribeInspectOverlay` types/tables/draw/cache/dispose (stateless,
  takes the mode); (4) dialog `BuildInspectBoxes` + `RenderInspectOverlay` + wire `OnRenderGUI`
  (guard on `InspectOverlayMode >= 1`) / `OnGuiClosed`; (5) per-row driver formatting via
  `RowTextLayout`/`ScribeRowElement`; (6) manual in-game verification.

## Risks / open questions

- **Label overlap** at small text size / dense rows — mitigate with opaque label bg, category
  stagger, and the `mode == 2` outlines-only escape hatch. Not fully eliminable.
- **Z-order vs hover tooltips** — overlay at z≈601 sits above the composer; verify a tooltip doesn't
  hide labels (bump z if so). Overlay may hide a tooltip — acceptable for a debug tool.
- **Texture leak** — `GenTextTexture` returns GL textures; cache + dispose in `OnGuiClosed`, and
  dispose superseded cache entries.
- **Recompose churn** — dialog recomposes often; read `SingleComposer`/`rowListContentBounds` **live
  each frame** (don't cache references across frames) so the overlay self-heals after recompose.
- **`rowEditInput` null** unless a row is focused — `?.` handles it; annotate the focused row (which
  suppresses its static label and hosts the input) specially to avoid confusion.
- **Reflection fragility** — only if `IncludeUnknownElements` is enabled; keep try/catch + off by
  default; known-keys path is reliable.
- **Open question:** does ConfigLib apply an `InspectOverlayMode` change on panel-close, or only on
  the next lectern reopen? (Ties to the still-unchecked openspec tasks 3.4/3.5 on ConfigLib's live
  path.) Either is fine — reopen always works — but worth confirming during verification.

## Verification (in-game, this Mac)

1. `build/restage.sh` (Release is fine — that's the point) → fully quit & relaunch VS.
2. Open a lectern, open the ConfigLib panel, set `InspectOverlayMode = 1`, reopen the lectern →
   outlines + labels appear over every box; set back to `0`, reopen → gone. Confirm one int-check
   cost per frame when off.
3. Check gap bands are labeled with the right config field (`TopContentGap`, `ScaledRowSpacing`,
   `ElementToDialogPadding`).
4. Switch to editor view; focus a row; confirm per-row column labels (`TextX`, affordance square)
   re-derive correctly and the focused `rowEditInput` box is annotated.
5. Scroll a long list and change text size (30%/150%) → outlines track live; confirm
   `InspectOverlayMode = 2` gives clean outlines-only when labels crowd at 30%.
6. Cross-check a couple of labeled numbers against `scribe-client-config.json`; edit one value in the
   ConfigLib panel, reopen the lectern, confirm the overlay reflects the new number (validates the
   whole diagnose-and-tune loop on this Mac).
