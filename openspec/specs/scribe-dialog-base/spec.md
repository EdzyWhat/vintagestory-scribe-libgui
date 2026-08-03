# scribe-dialog-base Specification

## Purpose
TBD - created by archiving change modularize-dialog-layer. Update Purpose after archive.
## Requirements
### Requirement: ScribeDialogBase contains all shared dialog logic and operates against IScribeDocumentHost
The mod SHALL define a `ScribeDialogBase` abstract class that inherits `GuiDialogBlockEntityBase`
and contains all view state fields, build methods, view-switch methods, network send helpers,
row-event callbacks, lock orchestration, autosave, title editing, and scroll-management logic
currently in `GuiDialogScribeLecternLibGui`. The class MAY be organized across multiple
`partial class ScribeDialogBase` source files by concern (e.g.
`ScribeDialogBase.<Concern>.cs`) rather than a single monolithic file, provided every part
declares the same `partial class ScribeDialogBase` in the same namespace and assembly and the
split is a pure relocation of members (no rename, no visibility change, no signature change, no
logic change). It SHALL hold an `IScribeDocumentHost host` field and SHALL NOT reference
`BlockEntityScribeLectern` anywhere. All former `lectern.*` accesses SHALL be replaced by
`host.*` calls through the interface.

#### Scenario: ScribeDialogBase compiles with no reference to BlockEntityScribeLectern
- **WHEN** the `ScribeDialogBase` partial-class files are compiled
- **THEN** no part contains a `using` import or type reference for `BlockEntityScribeLectern`

#### Scenario: All three views function on the Lectern after the refactor
- **WHEN** a player opens a Lectern after the refactor
- **THEN** the Read, Editor, and Pinned views all operate identically to before: tasks are listed,
  editing acquires the lock and autosaves, the Pin Tab shows the player's pins, view switches
  preserve scroll position

#### Scenario: Splitting the class across partial files is invisible to the player
- **WHEN** `ScribeDialogBase` is divided into multiple `partial class` files by concern and the mod
  is rebuilt
- **THEN** every dialog renders and behaves identically to before; the split changes only file
  organization, not the type's public surface or runtime behavior

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

### Requirement: The editor footer Information button toggles the Scribe Editor Features handbook page

The editor footer's Information (ⓘ) button SHALL toggle the "Scribe Editor Features" handbook page
rather than only opening it. When the survival handbook dialog is NOT open, clicking the button SHALL
open the handbook to the Scribe Editor Features page (`handbook://craftinginfo-scribe-editor-reference`)
via the game's registered `"handbook"` link protocol, exactly as before. When the survival handbook
dialog IS open, clicking the button SHALL close it. This behavior applies uniformly to every dialog
whose editor footer is built by `ScribeEditorContent` — the Lectern, the plain Notebook, the
Clockmaker's Notebook, and the always-edit tablet — because they share the one footer button.

The button SHALL detect and close the handbook using only public Vintage Story API, WITHOUT taking a
type reference to, or reflecting into, the survival mod's private handbook dialog or mod system: the
open handbook is discovered by scanning `capi.Gui.OpenedGuis` for the `GuiDialog` whose public
`ToggleKeyCombinationCode` equals `"handbook"`, and it is closed by calling that dialog's public
`TryClose()`. This preserves the deliberate decoupling of the current implementation.

When the handbook is open on a DIFFERENT page (not the Scribe Editor Features page), the button SHALL
navigate the handbook to the Scribe Editor Features page rather than closing the handbook — a
"focus, don't hide" rule — so a player who opened the handbook to another entry is taken to the
reference instead of losing the handbook entirely; a subsequent click, with our page then showing,
closes it.

When the survival mod (and thus the `"handbook"` link protocol and dialog) is not loaded, the button
SHALL be a graceful no-op on both the open and the close paths — no crash and no exception — matching
today's fail-safe behavior.

The button's hover tooltip (lang key `scribe:scribe-gui-editor-reference-tooltip`) SHALL convey the
toggle (open/close) affordance rather than an open-only label.

#### Scenario: Clicking the Information button opens the reference when the handbook is closed

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  not currently open
- **THEN** the survival handbook opens to the Scribe Editor Features page
  (`handbook://craftinginfo-scribe-editor-reference`)

#### Scenario: Clicking the Information button closes the handbook when it is already showing the reference

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  open on the Scribe Editor Features page
- **THEN** the survival handbook dialog closes

#### Scenario: Clicking the Information button navigates to the reference when the handbook is open elsewhere

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  open on a different page than the Scribe Editor Features page
- **THEN** the handbook navigates to the Scribe Editor Features page (it is not closed), and a
  subsequent click of the button closes the handbook

#### Scenario: The handbook is detected and closed without coupling to the survival mod's private dialog

- **WHEN** the button determines whether the handbook is open and needs closing
- **THEN** it does so by scanning `capi.Gui.OpenedGuis` for the `GuiDialog` whose
  `ToggleKeyCombinationCode` is `"handbook"` and calling its public `TryClose()`, taking no type
  reference to `GuiDialogHandbook`/`ModSystemSurvivalHandbook` and using no reflection

#### Scenario: Graceful no-op when the survival handbook is not loaded

- **WHEN** a player clicks the Information (ⓘ) button in a game where the survival mod (and its
  `"handbook"` link protocol) is not loaded
- **THEN** nothing happens — no handbook opens, no dialog closes, and no crash or exception occurs

#### Scenario: The toggle behavior is shared across every editor footer

- **WHEN** the Information (ⓘ) button is present on the Lectern, plain Notebook, Clockmaker's Notebook,
  or tablet editor footer
- **THEN** it exhibits the same open/close toggle behavior in each, because all four share the
  `ScribeEditorContent` footer button

