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

### Requirement: Read-view completion applies the completion policy locally and immediately

Completing a task from the read view SHALL apply the player's completion policy to the read view's own
document view and refresh immediately — the same optimistic-then-confirm model the editor uses — rather
than sending the completion to the server and waiting for a resync to make the result visible. The
visible result SHALL NOT depend on whether the completed task is pinned: a completion under a
document-mutating policy (`Delete`, `Sink`, `UnpinSink`) SHALL be reflected in the read view for an
unpinned task exactly as for a pinned one. The completion policy's document semantics SHALL be defined
by a single shared Core function used by both the server and every client view, so no surface derives
its own policy behavior. The authoritative server resync SHALL still arrive and supersede the optimistic
result.

#### Scenario: Completing an unpinned task under Delete removes its row immediately

- **WHEN** the player completes an unpinned document task from the read view while their completion
  policy is `Delete`
- **THEN** the task's row is removed from the read view immediately (not only after a later, unrelated
  refresh), the scroll offset holds, and the authoritative resync later confirms the same result

#### Scenario: Pinned and unpinned completions behave identically in the read view

- **WHEN** the player completes a task from the read view under a document-mutating policy
- **THEN** the read view reflects the policy's effect regardless of whether that task was pinned — the
  pinned case does not rely on the pin push while the unpinned case is left stale

#### Scenario: A read-only source does not optimistically predict a refused mutation

- **WHEN** the player completes a task on a permanently read-only source (a hard/fired tablet), where
  the server collapses every document-mutating policy to a plain unpin
- **THEN** the read view does not optimistically remove or reorder the task (which the server would
  refuse); the visible change is driven by the authoritative resync instead

### Requirement: The read view animates row departures through the shared collapse container

The read view SHALL render its rows through the shared animated-list container (`ScribeAnimatedList`),
so a row removed by a completion policy (or an external resync) collapses out with the same motion the
editor and pinned surfaces use, rather than disappearing in a single frame. The read view SHALL supply
its own static ghost snapshot for the collapsing row, consistent with the container's contract that a
live interactive row is never frozen in place.

#### Scenario: A policy-deleted read row collapses out instead of vanishing

- **WHEN** a read-view task is removed by the `Delete` completion policy (or an external resync removes
  a row)
- **THEN** the departing row collapses its height to zero with the shared animation and the rows below
  slide up smoothly, rather than the row disappearing instantly
