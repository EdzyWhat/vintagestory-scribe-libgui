## Why

Refining the lectern's custom-drawn GUI is a slow screenshot-and-relaunch loop: from pixels you
can't tell which `ScribeClientConfig` field or layout formula produced a given gap, padding, or
box size, so diagnosing spacing means reading `RowTextLayout`/`ScribeRowElement` source and
guessing. The usual live-tuning aid is dead on this machine — VSImGui needs OpenGL 4.3 and Apple
Silicon caps at 4.1, so the `#if DEBUG` sliders draw nothing and spam GL errors — and ConfigLib
only *edits* values, never shows which box a value drives. The engine already ships a
macOS-safe bounds-outline primitive (`IRenderAPI.RenderRectangle`, a plain `LineStrip` with no
4.3 dependency), so a browser-style "inspect element" overlay for the lectern is cheap to build
and closes the feedback loop directly on the Mac where the Debug path is unavailable. Full
design rationale lives in `docs/explorations/gui-inspect-overlay.md`.

## What Changes

- Add a config-toggled, in-game **inspect overlay** for the lectern dialog that outlines every
  composed box (rows, columns, checkbox, affordances, controls, viewport, chrome) over the *real*
  dialog, in both read and editor views.
- Each outlined box is labeled with its **element key + pixel size**, and — where known — the
  **driving config field / formula** (e.g. `TextX`, `TopContentGap`, `AffordanceButtonSizeFixed`),
  re-derived live from the same `RowTextLayout`/`ScribeRowElement` calls the compose uses so labels
  can't drift from real layout.
- Draw the **gaps that aren't elements** (`TopContentGap`, `ElementToDialogPadding`,
  `ScaledRowSpacing`, `ListToControlsGap`, `ControlRowGap`) as tinted, labeled bands — the
  "which box drives this padding" answer that's impossible to read from pixels.
- New `ScribeClientConfig.InspectOverlayMode` (`int`, default `0`) mirroring the engine's own
  `GuiComposer.Outlines` convention: `0`=off, `1`=outlines+labels, `2`=outlines only. Ships in
  Release (NOT `#if DEBUG` — the whole point is inspecting on the Mac where Debug/ImGui is dead);
  one int-check per frame when off; no hotkey, no `ScribeModSystem` change.
- Expose `InspectOverlayMode` in the ConfigLib manifest so it's toggleable in-game on this Mac
  (the one live-editing path that works here) — reopening the lectern applies it.

## Capabilities

### New Capabilities
- `debug-inspect-overlay`: a config-toggled, Release-shipping in-game overlay that outlines and
  labels every composed box (and inter-element gap) of the lectern dialog with its element key,
  pixel size, and driving config field/formula, in both views — a native, macOS-safe substitute
  for the dead VSImGui tuning path.

### Modified Capabilities
<!-- None. The overlay is additive: a new config field + a new draw pass + a manifest entry. It
     changes no existing lectern-gui-shell requirement (it draws ON TOP of the real dialog without
     altering its layout or behavior). -->

## Impact

- **New:** `src/Mod/ScribeInspectOverlay.cs` — stateless draw helper (box/category types, color +
  driver tables, label-texture cache + `Dispose`).
- **Modified:** `src/Mod/GuiDialogScribeLectern.cs` — `BuildInspectBoxes()` + `RenderInspectOverlay(dt)`
  called at the end of `OnRenderGUI` guarded by `InspectOverlayMode >= 1`; dispose the label cache in
  `OnGuiClosed`. `src/Mod/ScribeClientConfig.cs` — add `InspectOverlayMode`.
  `src/Mod/assets/scribe/config/configlib-patches.json` — add the toggle.
- **Read-only:** `RowTextLayout.cs`, `ScribeRowElement.cs` — the formulas the driver labels re-derive.
- **No `src/Core/` change** (pure Mod-side — respects the Core-must-not-reference-VSAPI invariant),
  no new dependency (vanilla `IRenderAPI` only), no hotkey, no lang key, no network/persistence change.
- On completion, `docs/explorations/gui-inspect-overlay.md` migrates into this change and is deleted.
