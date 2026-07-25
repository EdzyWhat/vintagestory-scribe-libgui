## Why

Eight per-player Scribe preferences (completion policy, HUD max-rows / anchor / offsets / row-width /
collapse, plus text sizing) can today be changed ONLY by hand-editing client JSON. Two in-game tests are
backlogged waiting on a UI to exercise them — switching `CompletionPolicy` at runtime, and picking
non-default HUD anchor / offsets / width. The rejected path (ConfigLib's ImGui settings panel) is broken
on Apple Silicon, the author's dev machine (`add-pinned-task-hud` design D4), so the settings surface must
be a LibGUI surface. Players also want to resize Scribe's text — both the HUD and the block/item windows —
without leaving the view to edit a file.

## What Changes

- Add an in-mod **Settings view**: a size-agnostic LibGUI widget that a block/item dialog swaps into its
  central content region, opened by a **gear** control. It exposes every per-player preference, grouped
  into two sections (Behavior, Appearance), with per-field tooltip helptext. All controls write through
  instantly (no OK/Cancel), so the HUD and any open dialog update live.
- Add the gear as a new entry point in **two** places: the Lectern dialog chrome (swaps the central
  read/editor region to the settings view and back), and on the **HUD** next to the collapse chevron. The
  settings widget is written host-agnostic so future block/item UIs (Desk, Notebook — a different size)
  reuse it.
- Add two **font-scale multipliers** to player settings: `HudFontScale` and `WindowFontScale`. Both are
  live: changing the HUD scale rebuilds the HUD; changing the window scale rebuilds any open Lectern
  dialog. Multipliers (not absolute point sizes) so they stack correctly on VS's global Interface → GUI
  Scale.
- **BREAKING (internal, no user-facing data loss): Consolidate to a single client-local player config.**
  Fold the still-live fields of `ScribeClientConfig` (`scribe-client-config.json`) into Core's
  `ScribePlayerSettings` (`scribe-hud-config.json`) and **delete `ScribeClientConfig` entirely**. The ~25
  dead native-GUI-era fields (row heights, ruling, affordance colors, inspect overlay, etc.) are removed
  outright; the handful of live layout knobs (row paddings, checkbox size, pinned tint) become code
  constants (not user settings); only the font sizing survives as a user-facing knob (as the two scales
  above). `WindowFontScale` absorbs the old `TextSizeScale`.
- Make the Lectern derive its row style **live per build** from the consolidated settings (it currently
  snapshots it once at open), and compute the `ScribeRowControlNudge` checkbox/grip centering from
  measured heights instead of the font-size-15 constants (required once font size is adjustable).
- Add localization keys for every label, enum option, and helptext string; register a new `scribegear`
  icon asset.

**Follow-up refinements (playtest round 1):**
- **Deferred-send completion with an undoable window.** All completion policies share one ~1.5s HUD
  window (`PinHudWaitMs`): the HUD holds a just-checked completion locally and only sends it to the server
  on expiry, so unchecking within the window is a true undo (nothing was sent) — important for the
  destructive `Unpin`/`Delete` policies. During the window the row animates: `Unpin`/`Delete` fade their
  text out; a `Sink` row mutes and slides toward the bottom. The lectern read-view checkbox stays
  immediate (a different surface, outside the "pin window").
- **New `Keep` completion policy.** A fourth policy: completing keeps the pin AND leaves it in place (does
  not sink it), for players who want a persistent checked record. Server-side it behaves like `Sink` (no
  removal); the HUD difference is ordering only.
- **Numeric fields instead of sliders.** The settings form's numeric preferences (font scale, HUD row
  width, HUD max rows) use numeric-entry controls, not sliders — a slider grabs the scroll wheel and
  overwrites list scrolling. The font scale is entered as a percent (80–120, step 5).
- **Relative HUD offsets.** The HUD X/Y offsets are nudges *relative to* the anchor's built-in pre-baked
  offset (e.g. the top-right minimap clearance), not absolute, and clamp to ±300.
- **Live-scaled settings form + HUD checkbox.** The settings form's own text and checkboxes scale with the
  window font scale; the HUD checkbox scales with the HUD font scale (text + checkboxes only — dropdowns
  keep fixed heights).
- **Chrome polish.** The window title reads "Scribe Settings" while the settings view is shown; the gear
  uses a filled icon; a lock denied while the dialog is open falls back to the read view (so a Back from
  the editor that loses the lock to another player can't strand the dialog).

Explicitly deferred to its own future change: a fully custom SVG check+box (box + background + check,
overlaid and animated on toggle) to replace LibGUI's dot-style checkbox — a visual/animation feature, not
needed here since LibGUI's `Checkbox` already scales via its `size` param.

Explicitly NOT in scope: any server-wide / world balance setting (e.g. requiring a crafted item to enable
the HUD) — those are a separate future server config and never belong in the per-player Settings view; a
ConfigLib panel; a standalone settings dialog or a settings hotkey; a spatial anchor grid (a dropdown is
used); reviving any deleted layout knob as a user control.

## Capabilities

### New Capabilities
- `settings-tab`: an in-dialog, per-player settings surface. A gear control swaps a block/item dialog's
  central region (and offers a second entry point on the HUD) into a two-section (Behavior / Appearance)
  LibGUI form that reads and writes the player's client-local preferences with instant live-preview, and
  that exposes HUD + window font-size scaling. Covers the gear entry points, the host-agnostic settings
  widget, the field-to-control mapping, instant write-through, and per-field helptext.

### Modified Capabilities
- `player-pins`: the per-player preference set is consolidated into a single client-local config and gains
  two font-scale multipliers (`HudFontScale`, `WindowFontScale`); the separate `scribe-client-config.json`
  / `ScribeClientConfig` store is retired, its live font knob folded in and its dead knobs removed.
- `lectern-gui-shell`: the Lectern dialog gains a third central-region view (Settings) reachable by a gear
  in its chrome, and derives its text/row sizing live from the consolidated player settings so a font-size
  change repaints the open dialog.

## Impact

- **Core (`src/Core/`)**: `ScribePlayerSettings` gains `HudFontScale` / `WindowFontScale` (+ clamp
  bounds, normalized in `Normalized()`); optionally absorbs the migrated `RowFontSize`. Unit tests for the
  new clamps. No VS API reference.
- **Mod (`src/Mod/`)**: delete `ScribeClientConfig.cs`; `ScribeRowStyle.FromConfig` reads
  `ScribePlayerSettings`; `GuiDialogScribeLecternLibGui` gains a settings view + gear and live row-style
  derivation; new `ScribeSettingsContent` widget; `HudScribePins` gains a gear and honors `HudFontScale`;
  `ScribeRowControlNudge` becomes computed. New `scribegear` SVG registered via `RegisterCustomIcons`.
- **Assets**: `assets/scribe/lang/en.json` gains settings labels / enum options / helptext keys;
  `assets/scribe/textures/icons/gear.svg` added (flat single-shape, `#000000`, matching the existing
  icon convention — the Bootstrap `bi-gear` source uses two `currentColor` paths and must be flattened).
- **No new dependencies**: LibGUI (`gui`) is the existing hard dep; all controls
  (`Dropdown`, `Slider`, `NumericField`, `Checkbox`, `Tooltip`) already ship in it.
- **Migration**: an existing `scribe-client-config.json` becomes vestigial — Newtonsoft ignores unknown
  keys and absent keys default, so nothing breaks; players keep defaults unless they had hand-edited the
  font scale, which is re-entered once through the new UI.
