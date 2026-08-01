## Why

A playtest of the notebook-framed Lectern and its Scribe Settings window surfaced five small
rough edges: the draggable title-bar band isn't discoverable, the settings numeric fields snap
to a bound the instant you clear them (so you can't select-all-and-retype), the settings form is
a flat two-section list floating on a transparent frame, and the buttons that launch settings
only ever open. None are new features — they are polish that makes the existing chrome read as
intentional and behave the way players expect.

## What Changes

- **Title-bar drag-grip affordance.** Add a drag-grip icon (the already-registered `scribegrip`
  SVG) to the LEFT of the close button in the Lectern's `TitleTextButtons` row, so the fully-
  draggable art title-bar band is visually discoverable.
- **Numeric-input clamp on unfocus, not per-keystroke.** Rework the settings numeric fields
  (`ScribeNumericField` via the `NumericField`/`IntField`/`FontScaleField` helpers) so a value is
  clamped to its valid range on UNFOCUS (blur), not on every keystroke — today, clearing the field
  mid-edit snaps it to min/max immediately, so you can't select-all and type a new value. Add
  error/helper text that tells the player a value was clamped and states the valid range. This is a
  PRE-EXISTING issue from `add-settings-tab`, surfaced by the new Pixel Art Size field.
- **Settings: two sections become three.** Split the current Behavior + Appearance sections into
  **Mod Behavior**, **Window Appearance**, and **HUD Appearance**, re-sorting each control into the
  right section, with horizontal dividers between the three.
- **Settings default background.** Paint the LibGUI theme's default surface color behind the
  settings form so its inputs sit on a real window panel instead of a fully transparent frame.
- **Settings buttons toggle open AND closed.** The Lectern's right-column gear nav button and the
  HUD gear SHALL toggle the settings window — closing it if it is already open — rather than only
  opening it.

## Capabilities

### New Capabilities
<!-- None. All five requests refine behavior in two existing capabilities. -->

### Modified Capabilities
- `settings-tab`: numeric-entry controls clamp on unfocus (not per-keystroke) and surface clamp
  feedback text with the valid range; the surface is grouped into THREE sections (Mod Behavior /
  Window Appearance / HUD Appearance) separated by dividers; the surface paints the theme's default
  surface background; and the gear controls that open it toggle it closed when it is already open.
- `lectern-gui-shell`: the title bar's button row gains a drag-grip icon left of the close button,
  marking the draggable band.

## Impact

- **Mod (`src/Mod/`)**:
  - `ScribeNumericField.cs` — clamp-on-blur: accept a clamp callback + valid-range descriptor, apply
    the clamp on focus-loss (and surface helper/error text when a value was clamped) instead of the
    caller clamping on every `onChanged`.
  - `ScribeSettingsContent.cs` — three sections with dividers; re-sort controls; wire the fields'
    clamp callbacks to the Core `Clamp*` statics + range text; helper-text rendering.
  - `ScribeSettingsDialog.cs` — paint the theme surface color behind the form.
  - `GuiDialogScribeLecternLibGui.cs` — add the `scribegrip` icon left of the close button in
    `BuildTitleBar`'s row.
  - `ScribeModSystem.cs` — `OpenSettings()` becomes a toggle (close if `IsOpened()`).
  - `assets/scribe/lang/en.json` — new section titles, clamp/range helper strings, grip tooltip.
- **Core (`src/Core/`)**: no new fields. Existing `Clamp*` statics + `Min*/Max*` consts on
  `ScribePlayerSettings` are the range source the Mod-layer fields read; clamp-on-blur TIMING stays a
  Mod UI concern (Core stays VS-API-free and unit-testable).
- **No new dependencies.** Vanilla `VintagestoryAPI` + the `gui` hard dep only. Verification is
  in-game (Core suite can't reach the VS GUI).
- **Out of scope**: any redesign of the numeric widget beyond clamp timing + feedback; new preferences.
