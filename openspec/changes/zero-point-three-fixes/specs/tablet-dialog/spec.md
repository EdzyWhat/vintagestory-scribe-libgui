## MODIFIED Requirements

### Requirement: Central region keeps the editable task list

The tablet dialog's central region SHALL retain the editable task list inherited from
`ScribeDialogBase` (the same editor Proposal B exposed through the interim dialog), presented without
tab navigation. Adding, editing, checking off, and pinning tasks SHALL continue to work under the
tablet document policy (10-task / 1-pin caps). This change SHALL NOT remove task-editing capability
that the tablet has today.

When an add is refused because the tablet already holds the maximum number of task blocks (10), the
dialog SHALL surface a standard in-game error through the game's transient-error path rather than
silently doing nothing, so the player learns why no row appeared. The refusal SHALL be reported at
every add gesture that the cap governs (the footer add-task control and the keyboard insert-below
gesture), and the add-task control MAY additionally remain visually disabled at the cap.

#### Scenario: Tasks remain editable on the tablet

- **WHEN** a player opens a tablet and adds, edits, checks, or pins a task
- **THEN** the edit is applied and saved exactly as before, subject to the tablet's 10-task / 1-pin
  policy, with no tab navigation shown

#### Scenario: Adding an 11th task shows an in-game error

- **WHEN** a player attempts to add a task to a wet tablet that already holds 10 task blocks (via the
  add-task control or the keyboard insert gesture)
- **THEN** no task is added and a standard in-game error message tells the player the tablet is full

## ADDED Requirements

### Requirement: A hardened or fired tablet keeps checkboxes and pins live while blocking text edits

A hardened or fired tablet SHALL present its task list read-only with respect to **text** — the player
SHALL NOT be able to edit task text, add rows, delete rows, or reorder rows — while its **completion
checkboxes and pin toggles SHALL remain interactive**. Checking a task complete and pinning or unpinning
a task SHALL work on a hard or fired tablet exactly as on a wet one. This ensures a task pinned to the HUD
before the tablet hardened or was fired can still be unpinned, so firing a tablet never permanently strands
a pin. This behavior is specific to the tablet's read view; the tabbed Lectern/Notebook read view is
unaffected.

#### Scenario: Completing and unpinning work on a fired tablet

- **WHEN** a player opens a fired (or hardened) tablet and taps a task's checkbox or its pin control
- **THEN** the task's completion toggles and its pin toggles respectively, and the change is saved — the
  read-only state does not disable the checkbox or hide the pin control

#### Scenario: Text remains uneditable on a hardened tablet

- **WHEN** a player attempts to edit a task's text, add a row, delete a row, or reorder rows on a hardened
  or fired tablet
- **THEN** no such text edit is possible, and attempting to edit a row's text surfaces a material-specific
  in-game message explaining why (hardened: soften it in water to make changes; fired: it cannot be changed)

### Requirement: Completion policy collapses to unpin-only on a read-only tablet

When a task on a hardened or fired tablet is completed, any completion policy that would MUTATE the locked
document — *delete*, *sink*, or *unpin-and-sink* — SHALL resolve to *unpin* only, and *keep* SHALL remain
*keep*. The task's completion state and its pin removal SHALL still apply, but the underlying locked
document SHALL NOT be reordered or have rows deleted. This collapse SHALL be enforced at the
server-authoritative completion path so it holds for completion from the read view and from the HUD alike.
On a wet (editable) tablet the completion policy SHALL behave unchanged.

#### Scenario: Delete policy unpins instead of deleting on a fired tablet

- **WHEN** a player whose completion policy is *delete* completes a pinned task that belongs to a fired
  tablet
- **THEN** the task is marked complete and its pin is removed, but the task is not deleted from the tablet's
  document

#### Scenario: Sink policy unpins instead of reordering on a hardened tablet

- **WHEN** a player whose completion policy is *sink* or *unpin-and-sink* completes a pinned task on a
  hardened tablet
- **THEN** the task is marked complete and its pin is removed, but the tablet's document order is unchanged

#### Scenario: Wet tablet completion is unchanged

- **WHEN** a player completes a task on a wet tablet under any completion policy
- **THEN** the policy applies with its full effect (delete, sink, unpin, unpin-and-sink, or keep) exactly as
  before
