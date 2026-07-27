## MODIFIED Requirements

### Requirement: Read view renders the document as a scrollable widget tree
The read view SHALL render the document as a LibGUI widget tree — a window frame containing a free-text
section and a scrollable list of task/note rows — laid out declaratively (flex/`Column`/`Row` and the
Scribe-owned scrolling list container) rather than by absolute-bounds composition. A document with more
rows than fit the visible height SHALL remain fully reachable by scrolling the list, with no row rendered
permanently off-screen and no row content painting outside the scroll viewport. The list SHALL scroll
continuously (no page-turn navigation). An external change to the authoritative document (another viewer
edits it, or an autosave lands) SHALL be reflected by reconciling the list's rows from the new data,
NOT by unmounting and recreating the whole dialog tree.

#### Scenario: A long document remains fully reachable
- **WHEN** a lectern's document has more tasks and/or note sections than fit the visible content area
- **THEN** the row list scrolls, and every row remains reachable by scrolling — no row is rendered
  permanently off-screen, and no row paints outside the scroll viewport

#### Scenario: No page-turn controls are present
- **WHEN** the read view is rendered
- **THEN** the row list is a single continuously scrollable list with no "Prev"/"Next" page-turn
  controls or page-count indicator

#### Scenario: An external document change reconciles the list
- **WHEN** the authoritative document changes from outside the local read view (another viewer toggles
  a task, or an autosave lands) while the read view is open
- **THEN** the affected rows repaint from the new data by reconciling the list, without a full-tree
  rebuild of the dialog

### Requirement: Editor rows expose a working delete control
Each editor-view row SHALL provide a delete control that removes that block from the document
through the server-authoritative edit path. The control SHALL be a real action (not a reserved
column or a logging stub). Deleting the row the player is currently editing SHALL commit or
discard that row's in-progress edit safely (no crash, no orphaned focus on a removed row). A
structural change to the editor row set (delete, add, or reorder) SHALL update the list by
reconciling its rows in place — preserving surviving rows' focus/caret and animation state — rather
than by unmounting and recreating the whole dialog tree; the centralized cross-row focus
coordination SHALL be preserved across that reconcile.

#### Scenario: Delete control removes the row
- **WHEN** the player activates a row's delete control
- **THEN** that block is removed from the document and the row disappears from the list

#### Scenario: Deleting the focused row does not break focus
- **WHEN** the player deletes the row that currently holds edit focus
- **THEN** the editor does not crash and focus is not left pointing at the removed row

#### Scenario: A structural change preserves surviving rows' edit state
- **WHEN** a row is added, deleted, or reordered while another row holds an in-progress edit
- **THEN** the surviving edited row keeps its focus, caret position, and unsaved text, because the
  list reconciles by row identity rather than rebuilding the whole tree
