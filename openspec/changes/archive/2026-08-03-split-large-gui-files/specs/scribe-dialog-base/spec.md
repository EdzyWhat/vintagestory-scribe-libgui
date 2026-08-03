## MODIFIED Requirements

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
