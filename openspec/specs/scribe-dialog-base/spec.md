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

### Requirement: ScribeDialogBase exposes virtual extension points for subclass layout
The base class SHALL declare `protected virtual` extension points that let a subclass vary its layout
without altering the shared three-column layout skeleton (`[ SideColW spacer | TasksColW center |
SideColW right ]`) or the behavior of any dialog that does not override them:

- `GetExtraNavButtons()` returns an empty array by default. `BuildRightColNav` SHALL call it and
  append the returned buttons after the four baseline nav buttons (Read, Edit, Pinned, Settings).
- `BuildRightColNav()` SHALL be `protected virtual` so a subclass may replace the entire right column
  (for example, an empty, nav-less column whose `SideColW` width still preserves the side margin).
- `BuildCentralRegion()` SHALL be `protected virtual`, and `BuildEditorContent()` SHALL be
  `protected` (not `private`), so a subclass may supply its own center content while reusing the
  inherited editable task list rather than forking it.

The default bodies of these methods SHALL be the existing implementations, so a subclass that
overrides nothing behaves exactly as before.

#### Scenario: Lectern shows exactly four nav buttons
- **WHEN** the Lectern's subclass overrides none of these extension points
- **THEN** the right nav column contains exactly the Read, Edit, Pinned, and Settings buttons —
  no more, no fewer

#### Scenario: A subclass can add extra nav buttons
- **WHEN** a hypothetical Notebook subclass overrides `GetExtraNavButtons` to return a History button
- **THEN** the right nav column shows Read, Edit, Pinned, Settings, and then the History button

#### Scenario: A subclass can replace the right column and central content
- **WHEN** a subclass overrides `BuildRightColNav` to return an empty column and `BuildCentralRegion`
  to return its own single-view content (reusing the inherited `BuildEditorContent`)
- **THEN** the dialog renders no nav buttons and shows the subclass's own center layout, while the
  three-column skeleton and side margins are preserved

#### Scenario: Incumbent dialogs are unchanged
- **WHEN** the Lectern and both Notebook dialogs (which override none of these points) are built and
  opened after the extension points are added
- **THEN** their right column, central region, and all views (Read, Edit, Pinned, Settings, and
  History where present) build and behave exactly as before

