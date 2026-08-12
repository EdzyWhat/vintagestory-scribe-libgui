## ADDED Requirements

### Requirement: The editor add control is a kind picker

The editor footer's add control SHALL let the player choose which kind of block to create,
rather than always creating a checkbox task. The control SHALL present the available kinds
(this release: **Standard Task** and **Note**) and, on selecting a kind, SHALL create a new
block of that kind, append it to the editor's row list, and focus it — the same
append-and-focus behavior the "Add task" control has today. The control SHALL be shared by
every editor surface built from `ScribeEditorContent` (Lectern, plain Notebook, Clockmaker's
Notebook, and the always-edit tablet).

The control SHALL keep a fast path for the common case: activating the add control without
explicitly opening the kind menu SHALL create a Standard Task, so a single click still adds a
task exactly as before.

#### Scenario: Choosing Task creates a checkbox task

- **WHEN** the player uses the add control and selects the Standard Task kind
- **THEN** a new task block (with a checkbox) is appended to the editor and focused, empty and
  ready to type into, identical to the pre-change "Add task" behavior

#### Scenario: Choosing Note creates a freeform note

- **WHEN** the player uses the add control and selects the Note kind
- **THEN** a new text-section block (no checkbox, no completion state) is appended to the
  editor and focused, empty and ready to type into

#### Scenario: The default add is a task

- **WHEN** the player activates the add control without explicitly picking a kind (a plain
  click on the primary affordance)
- **THEN** a Standard Task is created, so the one-click add-a-task workflow is unchanged

### Requirement: The kind picker is backed by an extensible kind registry

The set of kinds the picker offers SHALL be defined by a registry (kind identifier, display
label, and the add behavior that creates a block of that kind), so that introducing a future
kind (e.g. a Tracked or Linked task) is a matter of registering it rather than restructuring
the footer control. Kinds that are not part of this release SHALL be absent from the live
menu — not shown as disabled or placeholder entries. The registry SHALL define exactly two
live kinds in this release: Standard Task and Note.

#### Scenario: This release offers exactly Task and Note

- **WHEN** the player opens the add control's kind menu
- **THEN** exactly two kinds are offered — Standard Task and Note — with no placeholder or
  disabled future-kind entries

#### Scenario: A future kind is added without changing the footer contract

- **WHEN** a new kind is registered in the kind registry
- **THEN** it appears in the picker's menu and creates its block kind, and no change to the
  footer layout, the add-control widget, or the other kinds' behavior is required

### Requirement: Note adds honor the same policy boundary as task adds

Creating a block through the kind picker SHALL pass through the same document-policy boundary
that governs task adds today. The tablet's task-count cap SHALL continue to gate Standard Task
adds; adding a Note SHALL NOT be blocked by the task-count cap (the cap is task-scoped). A
refused add SHALL be surfaced to the player, not silently swallowed, exactly as a refused task
add is today.

#### Scenario: Task cap still blocks a task add on a full tablet

- **WHEN** a tablet is at its task-count cap and the player selects the Standard Task kind
- **THEN** the add is refused and the refusal is surfaced to the player (unchanged from the
  pre-change behavior)

#### Scenario: A Note can be added on a tablet at its task cap

- **WHEN** a tablet is at its task-count cap and the player selects the Note kind
- **THEN** a note is added, because the task-count cap does not apply to notes
