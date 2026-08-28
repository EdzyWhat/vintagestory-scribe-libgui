# task-font-metrics

## Purpose
Task-text typefaces share Caudex's Skia line-box so switching families does not change single-line
row height. Optical scale and a per-family vertical offset sit on top of that match. Metrics apply
through one chokepoint on document surfaces; cuneiform, HUD, Settings chrome, and Caudex titles/buttons
are excluded.

## Requirements
### Requirement: Task fonts share Caudex's Skia line-box
At a given nominal font size (the window-scaled base size), every selectable task-text family SHALL
occupy the same layout line-box height as Caudex. That height SHALL be the value LibGUI reports for
Caudex via `TextLayoutHelper.MeasureText` on the probe `"Ag"` (Skia
`Descent − Ascent + Leading`). The empty-string default family (`sans-serif`) SHALL be included.
Caudex as a task-font choice SHALL be a no-op (scale 1).

This requirement applies to task-text TTF rendering on Read and Edit (and other task-font document
surfaces). Tablet cuneiform (`CuneiformText`, `ScribeCuneiformField`, cuneiform titles) SHALL remain
on `CuneiformMetrics` and SHALL NOT be scaled or offset by this table. Dialog titles and in-dialog
text buttons that use the fixed Caudex chrome face SHALL NOT use the task-font size scale. The
pinned-task HUD SHALL keep its own face and SHALL NOT be pegged to Caudex. The settings form SHALL
NOT inherit this table (see `settings-tab`).

#### Scenario: Switching from Caudex to Scapholene keeps a single-line row the same height
- **WHEN** a Lectern shows a single-line task in Caudex at the current window font scale
- **AND** the player selects Scapholene in Settings
- **THEN** that task's Read-view row height and Edit-view field height each remain the same as under
  Caudex (within 1 px)

#### Scenario: Switching to La Belle Aurore keeps a single-line row the same height
- **WHEN** a Lectern shows a single-line task in Caudex at the current window font scale
- **AND** the player selects La Belle Aurore in Settings
- **THEN** that task's Read-view row height and Edit-view field height each remain the same as under
  Caudex (within 1 px)

#### Scenario: Default sans-serif is pegged too
- **WHEN** the player uses the Default task-font choice
- **THEN** a single-line task row uses Caudex's line-box height at that nominal size, not the system
  sans-serif's native line-box

#### Scenario: Cuneiform tablet text is not pegged
- **WHEN** a tablet is rendering in cuneiform
- **THEN** its row/title/label sizes still follow `CuneiformMetrics.LineHeightRatio` and are unchanged
  by the task-font metrics table

#### Scenario: Caudex chrome is not scaled
- **WHEN** the player selects a non-Caudex task font
- **THEN** the dialog title and in-dialog text buttons still render in unscaled Caudex

#### Scenario: The pinned HUD is not pegged
- **WHEN** the player cycles Task Text Font in Settings
- **THEN** the pinned-task HUD keeps its own face and sizing
- **AND** HUD row height does not follow the Caudex line-box table

### Requirement: Per-family vertical offset is applied at draw time
Each selectable task-font family SHALL have a vertical draw offset, expressed in ems of the nominal
font size, so glyphs that sit high or low in the shared line-box can be shifted to read optically
like Caudex. Caudex's offset SHALL be 0. The offset SHALL be applied to glyph painting (Read `Text`
and Edit field text) and SHALL NOT change the reserved line-box height, caret bar height, or
checkbox/grip layout box.

#### Scenario: A positive offset moves Edit-view glyphs down without growing the field
- **WHEN** a family has a positive `OffsetEm`
- **THEN** that family's Edit-view glyphs paint lower in the field
- **AND** the field's laid-out height, caret bar, and selection highlight stay on the Caudex line-box

#### Scenario: Read-view text uses the same offset
- **WHEN** a family has a non-zero `OffsetEm`
- **THEN** Read-view task text is shifted by the same pixel amount as Edit-view text at that size

### Requirement: Per-family optical scale is applied on top of the line-box match
Each selectable task-font family SHALL have a hand-tuned `OpticalScale` multiplier applied on top of
the auto line-box `SizeScale`, so letters that share Caudex's line-box can still be drawn larger or
smaller until they *read* similarly sized. Caudex's optical scale SHALL be 1. Layout height SHALL
remain Caudex's line-box (`LineHeight`); only draw size changes. Optical scale SHALL NOT be written
into stock `TextStyle.FontSize` (LibGUI would then report a taller layout box). Stock `Text` SHALL
layout at `LayoutSize` and receive optical scale as a paint-only transform. Default (`sans-serif`)
typically needs a scale below 1; La Belle Aurore typically needs a scale above 1. Values are authored
via `tools/task-font-optical-scale/index.html` and confirmed in-game.

#### Scenario: Optical scale changes letter size without jumping the row
- **WHEN** a family has `OpticalScale` other than 1
- **THEN** that family's glyphs draw at `nominal × SizeScale × OpticalScale`
- **AND** a single-line Read/Edit row height stays on Caudex's line-box (within 1 px)

#### Scenario: Default can be optically shrunk
- **WHEN** Default's `OpticalScale` is less than 1
- **THEN** Default task text reads smaller than the post-line-box-match size
- **AND** the reserved row height is unchanged

### Requirement: Metrics apply through one chokepoint on every task-font surface
Size scale, optical scale, and vertical offset SHALL be resolved in one Mod-layer helper adjacent to
`ScribeTaskFont.Resolve`, not copied into individual widgets. Every surface that draws the player's
task TTF — Lectern, Notebook, Clockmaker's Notebook, Chalkboard, Scriptorium, Guestbook body text,
and tablet text when cuneiform is disabled — SHALL go through that helper for both measurement and
drawing. The pinned-task HUD and Settings chrome SHALL NOT.

#### Scenario: Tablet fallback to the task font is pegged
- **WHEN** the player disables cuneiform tablets and a tablet row renders in the selected task font
- **THEN** that row uses the same size scale, optical scale, and offset as a Lectern row in that font

#### Scenario: A missed call site cannot choose a different scale
- **WHEN** a new task-text widget is added and uses `ScribeTextDefaults` / `ScribeTaskFont` as existing
  tabs do
- **THEN** it inherits the pegged effective size without a per-widget metrics argument
