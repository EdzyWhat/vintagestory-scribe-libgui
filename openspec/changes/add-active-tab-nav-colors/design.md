## Context

The four sidebar nav buttons are built in `GuiDialogScribeLecternLibGui.BuildRightColNav`
(~line 1504) as `TitleButton(...)` calls, each currently passed the same neutral glyph color
`colors.OnSurfaceVariant`. `TitleButton` wraps a `ScribeRowButton` (the shared button widget), whose
state class `ScribeRowButtonState` (~line 3199) already owns `hovered`/`pressed` and paints the box
fill (`SurfaceHigh` + a hover/press "lift") and the glyph. So both the box fill and the hover hook we
need already exist — this change makes them conditional on an "active" flag and a thematic color.

Two different notions of "active":
- **Read / Edit / Pinned** are lectern views tracked by one field, `viewMode`
  (`enum ScribeLecternView { Read, Editor, Pinned }`, line 96–98). The active button is a pure
  function of `viewMode`.
- **Settings is not a lectern view.** The gear calls `modSystem.OpenSettings` (line 1521), which
  toggles the standalone `ScribeSettingsDialog` tracked by `modSystem.settingsDialog?.IsOpened()`
  (`ScribeModSystem.cs` line 162–167). Nothing currently tells the lectern when that window
  opens/closes, so the gear can't repaint live without a new notification.

## Goals / Non-Goals

**Goals:**
- Active nav button: thematic box fill + cream (`#eae6dd`) glyph; inactive buttons unchanged.
- Active-button hover brightens the fill by +10 HSV Brightness (reuse `ShiftBrightness`).
- Settings gear reflects the standalone settings window's open state, live.

**Non-Goals:**
- No change to inactive-button styling or to any non-nav button (the per-row delete/pin buttons keep
  their `SurfaceHigh` behavior).
- No new persisted setting; the colors are code constants (not user-tunable this pass).
- No change to view-switching logic, persistence, or Core.

## Decisions

### Thread an optional "active color" into `ScribeRowButton`
Add an optional `Vector4? activeColor` (default null) to `ScribeRowButton`/`TitleButton`. When null,
the widget behaves exactly as today (neutral `SurfaceHigh` fill, `IconColor` glyph). When set, the
resting fill is `activeColor`, the glyph is forced to cream `#eae6dd`, and the hover fill is
`ScribeRowConstants.ShiftBrightness(activeColor, +10f)`. This keeps all other `ScribeRowButton`
callers untouched and puts the branch in one place (`ScribeRowButtonState.Build`).
*Alternative considered:* a whole separate nav-button widget — rejected as duplicative; the existing
button already has the box + hover machinery.

### `BuildRightColNav` computes each button's active color inline
Add four thematic color constants + the cream glyph constant (natural home: `ScribeRowConstants`,
alongside the existing tints). In `BuildRightColNav`, pass `activeColor: NavRead` when
`viewMode == Read`, else null — and likewise for Edit/Pinned. For Settings, pass `activeColor:
NavSettings` when the settings window is open, else null.

### Settings-open visibility notification
`ScribeSettingsDialog` already fires nothing on open/close. Add a lightweight client-side event on
`ScribeModSystem` (e.g. `event Action? SettingsVisibilityChanged`) invoked from `OpenSettings` after
toggling, and also on the dialog's own close path (`OnGuiClosed`/`TryClose`) so an X-button or Escape
close notifies too. The lectern subscribes on open and calls `ForceRebuild` (mirroring how it already
rebuilds on `MyPinsChanged`), and unsubscribes on close/dispose. The lectern reads
`modSystem.IsSettingsOpen` (a small bool passthrough over `settingsDialog?.IsOpened()`) at build time.
*Alternative considered:* poll `IsOpened()` each frame in the lectern's render loop — rejected;
event-driven `ForceRebuild` matches the existing `MyPinsChanged` pattern and avoids per-frame work.

### Reuse `ShiftBrightness` for hover
The +10 HSV brighten is exactly `ScribeRowConstants.ShiftBrightness(activeColor, +10f)` (the helper
added for drag highlights). No new color math; saturation is left unscaled (full thematic chroma).

## Risks / Trade-offs

- **Settings close paths are multiple** (gear toggle, X button, Escape, lectern close) → if any path
  doesn't fire `SettingsVisibilityChanged`, the gear could show stale. Mitigation: fire from the
  dialog's single `OnGuiClosed` (all closes funnel through it) plus `OpenSettings`, and verify each
  close route in-game.
- **Lectern rebuild churn** → `ForceRebuild` on every settings open/close is cheap (one rebuild per
  toggle, not per frame) and matches existing `MyPinsChanged` usage — acceptable.
- **Cream glyph on light themes** → `#eae6dd` on a mid-tone thematic fill has adequate contrast for
  all four colors (all are mid/dark); verify in-game against both pixel-art and dark global themes.

## Migration Plan

Additive, visual-only. No data migration. Rollback = revert the widget/nav/modsystem edits; buttons
return to the uniform neutral style.

## Open Questions

- None blocking. If cream-on-thematic reads poorly for the lightest color (Settings `#746f66`), the
  glyph tone can be tuned live; it is a single constant.
