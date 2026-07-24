## Context

The lectern dialog (`GuiDialogScribeLectern`) is native OpenGL drawn via `GuiComposer`/
`ElementBounds` — there is no DOM, so a real browser inspector can't attach. Iterating on its
spacing today is a screenshot-and-relaunch loop: pixels don't reveal which `ScribeClientConfig`
field or layout formula drove a given box or gap. The two obvious live-tuning aids are unavailable
here: VSImGui needs OpenGL 4.3 and this machine (Apple Silicon) caps at 4.1 (its `#if DEBUG`
sliders draw nothing and spam GL errors — see `VSAPI-NOTES.md` "VSImGui debug overlay"), and
ConfigLib's panel only *edits* values, never shows which box a value drives.

The enabling find: Vintage Story already ships a GUI bounds-outline system.
`GuiComposer.Outlines` makes the composer call `GuiElement.RenderBoundsDebug()`, which strokes an
outline via `IRenderAPI.RenderRectangle(...)` — a plain `LineStrip` mesh with **no OpenGL 4.3
dependency, so it renders fine on this Mac** (verified: Alt+F10 `cycledialogoutlines` works). The
engine gives outlines for free; it just lacks labels and doesn't name the driving config field.
This change adds those, scoped to the lectern. Full exploration in
`docs/explorations/gui-inspect-overlay.md` (migrates here on completion, then is deleted).

## Goals / Non-Goals

**Goals:**
- See, over the *real* lectern dialog in both views, every composed box outlined and labeled with
  its element key + pixel size, and its driving config field/formula where known.
- Make the *gaps* (padding/spacing that are not elements) visible and labeled with their config field.
- Toggle it live on this Mac via a config field (ConfigLib panel or JSON edit), no relaunch of the
  whole tuning story — reopen the lectern to apply. Ship in Release.
- Zero new dependency; near-zero cost when off.

**Non-Goals:**
- Not a general engine-wide inspector — scoped to the lectern's own composed elements.
- Not live *editing* of values from the overlay (that's ConfigLib's job; the overlay only reveals
  which knob drives a box).
- No hotkey, no `ScribeModSystem` change, no input registration — driven purely by the config field.
- No exhaustive reflection over the composer's internal element dicts on the main path (that's an
  optional, off-by-default, try/catch-guarded fallback).

## Decisions

**Decision: toggle via a `ScribeClientConfig.InspectOverlayMode` int (0/1/2), not a hotkey or
`#if DEBUG`.** Mirrors the engine's own `GuiComposer.Outlines` convention (`0`=off,
`1`=outlines+labels, `2`=outlines only — the escape hatch when labels crowd at small text size).
The dialog re-reads config on every open (`GuiDialogScribeLectern.cs:79`), so toggling it in the
ConfigLib panel (the one live-editing path that works on this Mac) or editing the JSON and
reopening the lectern flips the overlay. *Why not `#if DEBUG`:* the entire point is inspecting on
the Mac, where the Debug/ImGui path is dead — so it must ship in Release. Cost when off is one
int-check per frame. *Why not a hotkey:* a hotkey needs a `ScribeModSystem`/input registration; the
config field rides the existing live-tuning loop with none of that.

**Decision: render as a separate draw pass at the end of `OnRenderGUI`, not as composed child
elements.** `RenderInspectOverlay(dt)` runs after `base.OnRenderGUI(...)`
(`GuiDialogScribeLectern.cs:251`), guarded by `InspectOverlayMode >= 1`, drawing with
`IRenderAPI.RenderRectangle` at `z≈600` (above the dialog's ~500). *Why not child elements:* a
composed child gets torn down on every recompose and, worse, clipped by the row-list scissor — the
overlay must draw *outside* that clip to label the viewport and chrome. A separate `GuiDialog` is
also wrong (it wouldn't share the dialog's private state). Reading `SingleComposer`/
`rowListContentBounds` **live each frame** (never caching references across frames) makes the
overlay self-heal after a recompose.

**Decision: enumerate the known keys the dialog itself composes; reflection is an optional
fallback.** Resolve fixed keys (`"rowListScrollbar"`, `"rowEditInput"`, `"switchModeButton"`,
`"textSizeSlider"`, `"toolPanelToggleButton"`, add-task) and per-row keys via
`ScribeBlockRowCell.PinKey(i)/DeleteKey(i)/DragHandleKey(i)/ToggleKey(i)/TextKey(i)` through
`SingleComposer.GetElement(key)?.Bounds`. *Why the base `GetElement`, never `GetSwitch`/
`GetToggleButton`/…:* the kind-specific getters throw on the wrong element kind (VSAPI-NOTES);
`?.` cleanly skips keys absent in the current view. The composer's `staticElements`/
`interactiveElements` dicts are internal — full enumeration needs reflection, kept as an
off-by-default, try/catch-guarded `IncludeUnknownElements` fallback, not the main path.

**Decision: driver labels re-derive from the same layout calls the compose uses.** A small static
`key-pattern → driver-string` table in `ScribeInspectOverlay`; per-row keys format their driver
live from the same `RowTextLayout.For(...)` / `ScribeRowElement.*Fixed(...)` calls, so labels
can't drift from real layout. Every label always shows `key + WxH` off `Bounds.OuterWidth/
OuterHeight`; the driver string layers on top and degrades to key+size when a key has no table entry.

**Decision: draw gaps from config values + neighboring bounds.** Gaps aren't elements (no `Bounds`):
`TopContentGap` band below the title bar; `ElementToDialogPadding` inset between dialog bounds and
`bgBounds`; `ScaledRowSpacing` between consecutive enumerated rows; `ListToControlsGap`/
`ControlRowGap` between controls — each labeled with its config field. Precedent:
`ScribeRowElement` already tints pad bands with Cairo rects; this is the same idea at screen level
via `RenderRectangle`.

**Decision: color by category, not depth.** chrome=white, viewport/content=cyan, rows=green,
affordances=orange, controls=magenta, gaps=yellow+faint fill. Labels via
`capi.Gui.TextTexture.GenTextTexture(...)` → cached `LoadedTexture` → `Render2DLoadedTexture`, with
an opaque `TextBackground`; affordance labels stagger to the box bottom to reduce overlap.
`InspectOverlayMode == 2` skips labels entirely.

**Decision: the `ScribeInspectOverlay` helper is stateless.** Pure draw functions taking `capi`,
the box list, and the mode — no static toggle, no input registration. Its only state is the label-
texture cache (keyed by string), which the dialog owns and disposes in `OnGuiClosed`.

## Risks / Trade-offs

- **Label overlap** at small text size / dense rows → opaque label background, category-based
  stagger, and the `mode == 2` outlines-only escape hatch. Not fully eliminable.
- **Texture leak** — `GenTextTexture` returns GL textures → cache by string, regen only on change,
  and dispose the whole cache in `OnGuiClosed` plus any superseded entries.
- **Z-order vs hover tooltips** — overlay at `z≈601` sits above the composer; a tooltip could hide a
  label or vice versa → bump z if a tooltip hides labels; the overlay hiding a tooltip is acceptable
  for a debug tool.
- **Recompose churn** — the dialog recomposes often → read `SingleComposer`/`rowListContentBounds`
  live each frame, never cache references across frames, so the overlay self-heals.
- **`rowEditInput` is null** unless a row is focused → `?.` handles it; annotate the focused row
  (which suppresses its static label and hosts the input) specially to avoid confusion.
- **Reflection fragility** — only if `IncludeUnknownElements` is enabled → keep try/catch + off by
  default; the known-keys path is reliable.
- **ConfigLib manifest `"integer"` setting broke the Mod Settings window** (playtest 2026-07-22T17-45-13)
  → REMOVED the manifest entry; toggle `InspectOverlayMode` by editing `scribe-client-config.json`
  directly instead. Adding the setting as the manifest's first-ever `"type": "integer"` entry (the other
  four are `"float"`) made ConfigLib's *entire* settings window fail to open — and it stayed broken
  across a full relaunch until the on-disk value was reset. ConfigLib parsed the patch without a logged
  error (`[Config lib] Configs loaded: 1`), so the failure is in how its ImGui-based `ConfigWindow`
  builds/draws the integer control (`DrawIntegerMinMaxSetting`), not in parsing. The dialog re-reads
  config on every open, so a plain JSON edit + reopen is a complete, lower-risk toggle path — and it
  doesn't depend on ConfigLib's ImGui window at all (which is itself the dead-on-this-Mac tech). The
  overlay feature is unaffected; only its in-panel toggle was dropped.

## Migration Plan

Additive and reversible: a new config field defaulting to `0` (off), a new draw pass gated on it,
and a new manifest entry. No data/persistence/network change, nothing to roll back beyond reverting
the code. Verified in-game per the exploration doc's verification steps (restage Release → set
`InspectOverlayMode = 1` in the ConfigLib panel → reopen lectern → outlines+labels appear; set `0` →
gone). On completion, `docs/explorations/gui-inspect-overlay.md` content is folded in and the file
deleted (it's an explicit temporary holding pen).

## Open Questions

- **Why does a `"type": "integer"` manifest entry break ConfigLib's window** while `"float"` entries
  don't? (See the Risks entry above.) Not blocking — the JSON-edit toggle sidesteps it — but if we ever
  want the setting back in the panel, the likely candidates are (a) the array-form + integer-type combo
  is an under-tested ConfigLib path, or (b) an `ImGui.SliderInt`/`DragInt` call rejecting something on
  the OpenGL 4.1 path. Would need a ConfigLib-side repro to confirm.
- Companion (separate, optional change `add-configlib-expand-knobs`): the manifest exposes only ~4
  of ~30 knobs today; the overlay *reveals* which knob drives a box, ConfigLib *edits* it. Left out
  of this change to keep scope tight.
