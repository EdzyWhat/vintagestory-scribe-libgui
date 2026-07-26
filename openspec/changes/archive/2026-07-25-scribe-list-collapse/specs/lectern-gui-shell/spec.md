## MODIFIED Requirements

### Requirement: Editor rows expose a working delete control
Each editor-view row SHALL provide a delete control that removes that block from the document
through the server-authoritative edit path. The control SHALL be a real action (not a reserved
column or a logging stub). Deleting the row the player is currently editing SHALL commit or
discard that row's in-progress edit safely (no crash, no orphaned focus on a removed row). When a
row is deleted, its height SHALL collapse smoothly to zero in place — so the rows below move up to
fill the space — and the row SHALL be removed from the list only after that collapse completes.
While it collapses, the departing row SHALL be shown as a non-interactive snapshot (it holds no
edit focus). Any re-clamp of the scroll position to the shrunken list SHALL be deferred until the
collapse completes, so it does not fight the collapsing row's changing height.

#### Scenario: Delete control removes the row
- **WHEN** the player activates a row's delete control
- **THEN** that block is removed from the document and the row disappears from the list

#### Scenario: Deleting the focused row does not break focus
- **WHEN** the player deletes the row that currently holds edit focus
- **THEN** the editor does not crash and focus is not left pointing at the removed row

#### Scenario: A deleted row collapses before it leaves
- **WHEN** the player activates a row's delete control
- **THEN** the row's height collapses smoothly to zero in place and the rows below move up to meet
  it, and the row is removed from the list only after that collapse finishes

#### Scenario: Deleting the bottom row does not leave dead scroll space
- **WHEN** the list is scrolled to the bottom and the player deletes the last row
- **THEN** the row collapses and the viewport settles onto the shortened list without a dead-space
  flash, because the scroll re-clamp waits until the collapse completes
