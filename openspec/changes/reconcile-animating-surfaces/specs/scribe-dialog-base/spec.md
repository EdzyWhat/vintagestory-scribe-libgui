## ADDED Requirements

### Requirement: The editor updates structural mutations by reconcile with stable identity
The editor surface SHALL apply structural mutations — inserting, deleting, and reordering task rows —
by reconciliation (`SetState` on persistent editor content) with rows keyed by stable TaskId, rather
than by `GuiBase.ForceRebuild()`. Across such a mutation the editor SHALL preserve the actively-edited
row's caret position and in-progress unsaved text, SHALL preserve cross-row focus (no focus leak or
loss), and SHALL preserve the scroll offset without relying on the capture-and-restore machinery that
`ForceRebuild` required. View switches (read ⇄ editor ⇄ settings), fresh editor seed, and lost-lock
recovery SHALL continue to use `ForceRebuild`, as those are genuinely-new trees.

#### Scenario: Deleting a row preserves the caret in another edited row
- **WHEN** the player is editing one task row (caret placed mid-text, unsaved changes) and deletes a
  different row
- **THEN** the edited row keeps its caret position and in-progress text, and focus is not lost or
  leaked to another row

#### Scenario: Reorder and insert preserve focus and scroll without capture-restore
- **WHEN** the player inserts or reorders rows in the editor
- **THEN** focus and scroll offset are preserved by reconciliation directly, without a
  capture-and-restore pass, and no row loses its `State`

#### Scenario: A view switch still uses a full rebuild
- **WHEN** the player switches between the read, editor, and settings views
- **THEN** the surface still rebuilds fully via `ForceRebuild`, because the target is a genuinely
  different tree with no identity to preserve

### Requirement: An external resync landing mid-edit does not drop a legitimately-local in-flight row
When an authoritative server resync arrives while the editor is reconciling and the player has a
freshly-created, not-yet-persisted row in progress, the editor SHALL NOT prune that local in-flight
row against the server snapshot. This preserves the existing guard (never drop the focused row; never
drop an empty task, which is never persisted by design) under the reconciling update path.

#### Scenario: A just-created local row survives an async server resync
- **WHEN** the player creates a new task row and, before it is persisted, an authoritative server
  resync arrives that does not contain that row
- **THEN** the local in-flight row is retained (not pruned), and its focus and caret are undisturbed
