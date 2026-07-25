## ADDED Requirements

### Requirement: A slide-out pin-editor pagelet lists all of the player's pins

The system SHALL provide a slide-out pagelet on the Lectern dialog that lists **all** of the opening
player's pins across every document, regardless of which document or block each pin references. The
pagelet SHALL read the same authoritative per-player pin set that the corner HUD reads (the
server-synced `MyPins`), SHALL NOT derive pins locally, and SHALL present them in the player's current
pin-list order. When the player's pin set changes (a pin added, removed, edited, reordered, orphaned,
or its snapshot refreshed), the pagelet SHALL reflect the updated set.

#### Scenario: The pagelet lists pins from every document
- **WHEN** a player who has pinned tasks in several different documents opens the pin-editor pagelet
- **THEN** the pagelet lists all of that player's pins together in their current pin-list order,
  regardless of which document each references

#### Scenario: The pagelet updates when the pin set changes
- **WHEN** the player's pin set changes while the pagelet is open (for example a pin is completed,
  edited, reordered, or its snapshot refreshes)
- **THEN** the pagelet re-renders to reflect the updated pin set

### Requirement: Each pin row supports complete, edit-text, delete, unpin, and reorder

Each row in the pin-editor pagelet SHALL provide the full edit treatment for that pin: a control to
complete the task, an inline editable text field for the task's text, a control to delete the task, a
control to unpin (remove the pin without deleting the task), and an affordance to reorder the pin
within the player's list. Each control SHALL act on the pin by its stable identity `(DocId, TaskId)`
and SHALL drive the server-authoritative pin operations (complete / edit-text / delete / unpin /
reorder); the pagelet SHALL NOT mutate pins locally without the server round-trip.

#### Scenario: A row exposes every edit action
- **WHEN** a player views any pin row in the pagelet
- **THEN** the row offers complete, inline text edit, delete, unpin, and reorder affordances for that
  pin

#### Scenario: Editing a row's text drives the identity-addressed edit
- **WHEN** a player edits a row's inline text and commits it
- **THEN** the pagelet sends the edit addressed by that pin's `(DocId, TaskId)` and the row reflects the
  server-synced result

#### Scenario: Unpin removes only the pin
- **WHEN** a player uses a row's unpin control
- **THEN** that player's pin is removed without deleting the underlying task, distinct from the delete
  control which removes the task itself

### Requirement: The pagelet completes with no undo delay

Unlike the corner HUD, which applies a client-side undo window before a completion settles, the
pin-editor pagelet SHALL apply completion (and every other row action) with no undo delay: the action
SHALL take effect immediately with no client-side sink timer. The HUD's undo-window behavior SHALL
remain unchanged; the no-undo behavior is specific to the pagelet.

#### Scenario: Completing in the pagelet is immediate
- **WHEN** a player completes a task from a pin row in the pagelet
- **THEN** the completion is applied immediately with no undo delay or sink-timer window

#### Scenario: The HUD keeps its undo window
- **WHEN** a player completes a task from the corner HUD (not the pagelet)
- **THEN** the HUD's existing client-side undo window still applies, unchanged by this capability

### Requirement: The pagelet complements the corner HUD

The pin-editor pagelet SHALL complement, not replace, the always-on corner HUD. The HUD SHALL keep its
existing behavior as the tiny glanceable view (including its undo window), and both surfaces SHALL read
the same authoritative per-player pin set so an action in one is reflected in the other. Opening or
using the pagelet SHALL NOT alter, hide, or reconfigure the HUD.

#### Scenario: HUD and pagelet stay in lockstep on the same pin set
- **WHEN** a player completes, edits, deletes, unpins, or reorders a pin in the pagelet
- **THEN** the corner HUD reflects the same change (because both read the same server-synced pin set),
  and the HUD's own behavior is otherwise unchanged

#### Scenario: The HUD remains the glanceable view
- **WHEN** the pagelet is opened
- **THEN** the corner HUD continues to display as before, unmodified — the pagelet adds a full editor
  without replacing the HUD

### Requirement: The pagelet slides in and out via a handle and stays interactive while sliding

The pagelet SHALL slide in and out from the Lectern window edge, toggled by a handle affordance. While
sliding, the pagelet SHALL remain interactive and correctly hit-testable — its controls SHALL respond
to input at their rendered on-screen position throughout the slide, not only when fully open. The
pagelet SHALL be clipped to the window edge so it reads as sliding out of the window rather than
floating free.

#### Scenario: The handle toggles the slide
- **WHEN** a player activates the pagelet handle
- **THEN** the pagelet slides in (or out) from the window edge to its open (or closed) position

#### Scenario: Controls are hit-testable mid-slide
- **WHEN** the pagelet is partway through sliding open or closed
- **THEN** its rows and controls respond to input at their current rendered position, so interaction is
  correct throughout the animation rather than only at the endpoints
