# tablet-dialog Specification

## Purpose
TBD - created by archiving change add-tablet-dialog. Update Purpose after archive.
## Requirements
### Requirement: GuiDialogScribeTablet is a ScribeDialogBase subclass

The system SHALL provide a `GuiDialogScribeTablet` class in `src/Mod/GuiDialogScribeTablet.cs` that
subclasses `ScribeDialogBase` and reuses the inherited title editing, drag-grip, close, autosave,
and network-send chrome rather than reimplementing it. It SHALL be constructed with a `TabletHost`
so it operates against the tablet's `ItemStack`-backed document through the existing
`IScribeDocumentHost` contract.

#### Scenario: Tablet dialog reuses inherited chrome

- **WHEN** a player opens a tablet and edits its title, drags it by the grip, and closes it
- **THEN** title editing (pencil → edit, unfocus/Done → save), grip dragging, and close all behave
  identically to the Notebook dialog, because they are inherited from `ScribeDialogBase`

### Requirement: Tablet dialog is always-edit with no tab navigation

The tablet dialog SHALL present a single always-edit central region with NO tab navigation column.
It SHALL NOT show the Read / Edit / Pinned / Settings baseline nav buttons, and SHALL NOT show a
History button. `GetExtraNavButtons()` SHALL remain empty for the tablet, and the tablet layout
path SHALL render no nav column.

#### Scenario: No nav column on the tablet

- **WHEN** a player opens a tablet
- **THEN** the dialog shows the editable document directly with no Read/Edit/Pinned/Settings/History
  tab buttons

### Requirement: Central region keeps the editable task list

The tablet dialog's central region SHALL retain the editable task list inherited from
`ScribeDialogBase` (the same editor Proposal B exposed through the interim dialog), presented without
tab navigation. Adding, editing, checking off, and pinning tasks SHALL continue to work under the
tablet document policy (10-task / 1-pin caps). This change SHALL NOT remove task-editing capability
that the tablet has today.

#### Scenario: Tasks remain editable on the tablet

- **WHEN** a player opens a tablet and adds, edits, checks, or pins a task
- **THEN** the edit is applied and saved exactly as before, subject to the tablet's 10-task / 1-pin
  policy, with no tab navigation shown

### Requirement: A display-only cuneiform title banner renders above the task list

The tablet dialog SHALL render a display-only cuneiform banner above the editable task list, showing
the document's **title** via the `CuneiformText` widget from the cuneiform-font capability. The
banner SHALL be given an explicit pixel height derived from `fontSizeEm` rather than relying on an
`Expanded` (which is inert inside a scroll view). The banner SHALL be display-only this round — the
title is still edited through the inherited title field, not by interacting with the banner, and the
task rows themselves are not yet rendered in cuneiform (that is a later proposal).

#### Scenario: Title appears in cuneiform above the tasks

- **WHEN** a player opens a tablet with cuneiform enabled
- **THEN** the document's title is shown as cuneiform strokes at a fixed, legible height above the
  editable task list

#### Scenario: Banner is display-only this round

- **WHEN** a player interacts with the cuneiform title banner
- **THEN** the banner does not become an editable field (the title is edited via the inherited title
  control, and cuneiform-editable task rows are a later proposal)

### Requirement: A single branch honors the disable-cuneiform setting

The tablet dialog SHALL compute a single `UseCuneiform` value from the player's `DisableCuneiformFont`
setting. When cuneiform is enabled the title banner SHALL render via `CuneiformText`; when disabled
the same title text SHALL render through the normal text path using `ScribeTaskFont.Resolve`. The
branch SHALL be evaluated in one place, not scattered per widget.

#### Scenario: Fallback to normal font when cuneiform is disabled

- **WHEN** a player has `DisableCuneiformFont` enabled and opens a tablet
- **THEN** the title banner renders with the normal resolved task font instead of cuneiform strokes

#### Scenario: Cuneiform renders when the setting is off

- **WHEN** a player has `DisableCuneiformFont` disabled (the default) and opens a tablet
- **THEN** the title banner renders as cuneiform strokes

### Requirement: Tablet dialog uses its own theme and material-keyed backdrops

The tablet dialog SHALL select an earthen/clay `ScribeTheme.Tablet` palette in its `Build()` theme
wrapper, and SHALL declare two named backdrop slots in `ScribeBackdrops` keyed to the tablet item's
`material` variant axis: `clay` and `wax`. In this change both slots SHALL point at the existing
`textures/gui/scribe-lectern.png` placeholder (whose 1024×1160 ratio matches the tablet layout
aspect). The backdrop SHALL be applied through the existing `WrapBackdrop` / `BuildOuterArtBox`
mechanism unchanged.

#### Scenario: Tablet opens with its own theme and backdrop

- **WHEN** a player opens a tablet
- **THEN** the dialog is drawn with the earthen `Tablet` theme and the backdrop slot for that
  tablet's material (the shared placeholder art this round)

#### Scenario: Backdrop slots reuse the placeholder art

- **WHEN** the tablet dialog resolves the `clay` or `wax` backdrop slot
- **THEN** each resolves to `textures/gui/scribe-lectern.png` for now, with the ratio matching the
  tablet layout

