# scribe-dialog-base Specification

## Purpose
TBD - created by archiving change modularize-dialog-layer. Update Purpose after archive.
## Requirements
### Requirement: ScribeDialogBase contains all shared dialog logic and operates against IScribeDocumentHost
The mod SHALL define a `ScribeDialogBase` abstract class in `src/Mod/ScribeDialogBase.cs` that
inherits `GuiDialogBlockEntityBase` and contains all view state fields, build methods, view-switch
methods, network send helpers, row-event callbacks, lock orchestration, autosave, title editing,
and scroll-management logic currently in `GuiDialogScribeLecternLibGui`. It SHALL hold an
`IScribeDocumentHost host` field and SHALL NOT reference `BlockEntityScribeLectern` anywhere.
All former `lectern.*` accesses SHALL be replaced by `host.*` calls through the interface.

#### Scenario: ScribeDialogBase compiles with no reference to BlockEntityScribeLectern
- **WHEN** `ScribeDialogBase.cs` is compiled
- **THEN** the file contains no `using` import or type reference for `BlockEntityScribeLectern`

#### Scenario: All three views function on the Lectern after the refactor
- **WHEN** a player opens a Lectern after the refactor
- **THEN** the Read, Editor, and Pinned views all operate identically to before: tasks are listed,
  editing acquires the lock and autosaves, the Pin Tab shows the player's pins, view switches
  preserve scroll position

### Requirement: ScribeDialogBase exposes one virtual extension point for extra nav buttons
The base class SHALL declare one `protected virtual Widget[] GetExtraNavButtons()` method that
returns an empty array by default. `BuildRightColNav` SHALL call this method and append the returned
buttons after the four baseline nav buttons (Read, Edit, Pinned, Settings). A subclass that returns
one or more extra widgets from this method SHALL have those buttons appear in the nav column
below the baseline four.

#### Scenario: Lectern shows exactly four nav buttons
- **WHEN** the Lectern's subclass does not override `GetExtraNavButtons`
- **THEN** the right nav column contains exactly the Read, Edit, Pinned, and Settings buttons —
  no more, no fewer

#### Scenario: A subclass can add extra nav buttons
- **WHEN** a hypothetical Notebook subclass overrides `GetExtraNavButtons` to return a History button
- **THEN** the right nav column shows Read, Edit, Pinned, Settings, and then the History button

### Requirement: ScribeDialogBase uses host.DefaultDocumentTitle as the empty-title fallback
When the player commits the title field with empty or whitespace-only text, the base class SHALL
normalize the title to `host.DefaultDocumentTitle` (e.g. `"Lectern"`) rather than to
`ScribeDocument.DefaultTitle`.

#### Scenario: Blanking the title on the Lectern resets it to "Lectern"
- **WHEN** a player clears the Lectern's title field and commits (Enter/blur/Done)
- **THEN** the title is saved as `"Lectern"` — the Lectern host's DefaultDocumentTitle

### Requirement: Row content widget classes are named item-neutrally and live in separate files
The six widget classes currently appended to `GuiDialogScribeLecternLibGui.cs` SHALL be moved to
their own source files with item-neutral names:
- `ScribeLecternReadContent` + `ScribeLecternReadContentState` → `ScribeReadContent` in `ScribeReadContent.cs`
- `ScribeLecternEditorContent` + `ScribeLecternEditorContentState` → `ScribeEditorContent` in `ScribeEditorContent.cs`
- `ScribeLecternPinnedContent` + `ScribeLecternPinnedContentState` → `ScribePinnedContent` in `ScribePinnedContent.cs`
Their data records (`ScribeReadRowData`, `ScribeEditRowData`, `ScribePinRowData`,
`ScribeDepartingEditorRow`) SHALL move with their content class file.
`ScribeReadRow`, `ScribeEditRow`, `ScribePinRow`, `ScribeFrozenEditorRow`, `ScribeRowButton`,
`ScribeRowButtonText`, `ScribeVsIconGlyph`, `ScribeRowControlNudge` SHALL also move out of the
monolith into appropriate separate files (or into the content class file they are most tightly
coupled with).

#### Scenario: Renaming is a pure move — no behavior change
- **WHEN** the mod is built and a Lectern is opened after the rename
- **THEN** every widget renders and behaves identically to before; the rename is invisible to the player

