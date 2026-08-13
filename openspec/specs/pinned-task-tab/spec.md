# pinned-task-tab Specification

## Purpose
TBD - created by archiving change scribe-pin-editor. Update Purpose after archive.
## Requirements
### Requirement: A Pin Tab nav view lists all of the player's pins

The system SHALL provide a **Pin Tab** — a selectable view in the Lectern dialog's central region,
reached from the right-column `scribepin` navigation button, that lists **all** of the opening player's
pins across every document, regardless of which document or block each pin references. The Pin Tab SHALL
be a peer view alongside the read and editor views (a distinct central-region state selected by the nav,
not an overlay), so switching to it swaps the central content the way switching to the read or editor
view does. It SHALL read the same authoritative per-player pin set the corner HUD reads (the
server-synced `MyPins`), SHALL NOT derive pins locally, and SHALL present them in the player's current
pin-list order. It SHALL show every pin the player holds with no maximum-row cap (unlike the HUD's
bounded, "+N more"-summarized view). When the player's pin set changes (a pin added, removed, edited,
reordered, orphaned, or its snapshot refreshed), the Pin Tab SHALL reflect the updated set.

#### Scenario: The Pin Tab lists pins from every document
- **WHEN** a player who has pinned tasks in several different documents switches to the Pin Tab
- **THEN** the Pin Tab lists all of that player's pins together in their current pin-list order,
  regardless of which document each references, with no row-count cap

#### Scenario: Selecting the Pin Tab swaps the central region
- **WHEN** a player activates the `scribepin` navigation button
- **THEN** the Lectern's central region switches to the Pin Tab view (as read/editor switching does),
  and the same navigation switches back to read or editor

#### Scenario: The Pin Tab updates when the pin set changes
- **WHEN** the player's pin set changes while the Pin Tab is open (for example a pin is completed,
  edited, reordered, or its snapshot refreshes)
- **THEN** the Pin Tab re-renders to reflect the updated pin set

### Requirement: Pin Tab rows are editable by default with complete, edit-text, delete, unpin, and reorder

Each row in the Pin Tab SHALL be editable by default (not behind a separate "edit" mode), reusing the
editor view's row rendering but sourced from the player's pin set rather than the current document. Each
row SHALL provide: a control to complete the task, a directly-editable text field for the task's text, a
control to delete the underlying task, a control to unpin (remove the pin without deleting the task), and
an affordance to reorder the pin within the player's list. Each control SHALL act on the pin by its stable
identity `(DocId, TaskId)` and SHALL drive the server-authoritative pin operations lock-free
(complete / edit-text / delete / unpin / reorder), never through the document's edit lock and never by
mutating pins locally without the server round-trip. Tab / Shift+Tab traversal within the Pin Tab SHALL
visit only the rows' editable text fields, in row order; it SHALL NOT stop focus on a row's completion
checkbox (the checkbox remains operable by mouse click). Because a Pin Tab row reuses the editor's
multi-line text field, its caret navigation SHALL match the editor's: the Up / Down arrows SHALL move the
caret between the row's visual lines (to the text start / end when already on the first / last line),
within the row, without moving focus to another row or committing the edit.

#### Scenario: A row exposes every edit action
- **WHEN** a player views any pin row in the Pin Tab
- **THEN** the row offers complete, a directly-editable text field, delete, unpin, and reorder affordances
  for that pin

#### Scenario: Editing a row's text drives the identity-addressed edit
- **WHEN** a player edits a row's text and commits it
- **THEN** the Pin Tab sends the edit addressed by that pin's `(DocId, TaskId)` and the row reflects the
  server-synced result

#### Scenario: Tab traversal skips the row checkbox
- **WHEN** the player presses Tab or Shift+Tab to move between rows in the Pin Tab
- **THEN** focus moves directly from one row's editable text field to an adjacent row's editable text
  field, never landing on a row's completion checkbox

#### Scenario: Up/Down navigate lines within a pin row
- **WHEN** the player presses Up or Down while editing a multi-line pin row
- **THEN** the caret moves to the adjacent visual line within that same row (or to the text start/end
  at the first/last line), without moving focus to another pin row or committing the edit

#### Scenario: Unpin removes only the pin
- **WHEN** a player uses a row's unpin control
- **THEN** that player's pin is removed without deleting the underlying task, distinct from the delete
  control which removes the task itself

### Requirement: The Pin Tab fulfills the HUD's deferred manual reorder and manual unpin

The Pin Tab SHALL be the surface that provides the manual pin reordering and manual pin removal that the
corner HUD deliberately omits. The HUD orders pins automatically and offers no manual reorder or manual
unpin control; the Pin Tab SHALL provide both, permuting the per-player pin list order (not any document's
block order) and removing pins by explicit control rather than only as a completion-policy side effect.

#### Scenario: Manual reorder that the HUD does not offer
- **WHEN** a player reorders their pins in the Pin Tab
- **THEN** their per-player pin list is permuted into the chosen order (persisted and re-synced), while no
  document's block order changes — providing the manual ordering the HUD defers to this surface

#### Scenario: Manual unpin that the HUD does not offer
- **WHEN** a player unpins a task from the Pin Tab without completing it
- **THEN** the pin is removed by that explicit action, independent of any completion policy

### Requirement: The Pin Tab exposes the completion-policy control

The Pin Tab SHALL surface the "on completing a task" completion-policy control (the same
`ScribeCompletionPolicy` choice — keep-and-sink, keep-in-place, unpin, or delete — offered by the Scribe
Settings window), because the Pin Tab is where that policy's effect on checking a task off is most
directly observed. The control on the Pin Tab and the control in the Settings window SHALL edit the one
shared per-player completion-policy preference; changing it in either place SHALL update the same stored
value and take effect for both.

#### Scenario: Changing the policy from the Pin Tab
- **WHEN** a player changes the completion policy using the control on the Pin Tab
- **THEN** the shared per-player completion-policy preference is updated (and persisted client-local),
  and the Settings window reflects the same value

#### Scenario: The policy governs Pin Tab completions
- **WHEN** a player completes a task from the Pin Tab under a given completion policy
- **THEN** the completion is applied under that policy (kept-and-sunk, kept-in-place, unpinned, or the
  task deleted) exactly as the same policy governs completions elsewhere

### Requirement: The Pin Tab completes with no undo delay

Unlike the corner HUD, which applies a client-side undo window before a completion settles, the Pin Tab
SHALL apply completion (and every other row action) with no undo delay: the action SHALL take effect
immediately with no client-side sink timer. The HUD's undo-window behavior SHALL remain unchanged; the
no-undo behavior is specific to the Pin Tab.

#### Scenario: Completing in the Pin Tab is immediate
- **WHEN** a player completes a task from a pin row in the Pin Tab
- **THEN** the completion is applied immediately with no undo delay or sink-timer window

#### Scenario: The HUD keeps its undo window
- **WHEN** a player completes a task from the corner HUD (not the Pin Tab)
- **THEN** the HUD's existing client-side undo window still applies, unchanged by this capability

### Requirement: The Pin Tab complements the corner HUD

The Pin Tab SHALL complement, not replace, the always-on corner HUD. The HUD SHALL keep its existing
behavior as the tiny glanceable view (including its undo window and automatic ordering), and both surfaces
SHALL read the same authoritative per-player pin set so an action in one is reflected in the other.
Opening or using the Pin Tab SHALL NOT alter, hide, or reconfigure the HUD.

#### Scenario: HUD and Pin Tab stay in lockstep on the same pin set
- **WHEN** a player completes, edits, deletes, unpins, or reorders a pin in the Pin Tab
- **THEN** the corner HUD reflects the same change (because both read the same server-synced pin set),
  and the HUD's own behavior is otherwise unchanged

#### Scenario: The HUD remains the glanceable view
- **WHEN** the Pin Tab is opened
- **THEN** the corner HUD continues to display as before, unmodified — the Pin Tab adds a full editable
  surface without replacing the HUD

### Requirement: Pin Tab row removal animates immediately with no undo window
When a pin is removed from the Pin Tab — by completing it (under a completion policy that removes
it), unpinning it, or deleting the underlying task — the departing row SHALL animate out (its height
collapsing so the rows below move up smoothly to fill the space) rather than vanishing in a single
frame. The Pin Tab SHALL take **immediate** removal action with no undo/grace window: the animation
begins as soon as the removal is initiated, and the action is not held for a revert period. This
differs deliberately from the pinned-task HUD, which delays completion behind an undo window; the
Pin Tab shows and lets the player change the Completion Policy and provides discrete unpin and delete
controls, so its choices are affirmative and need no misclick grace.

#### Scenario: Removing a Pin Tab row collapses instead of snapping
- **WHEN** the player completes (with a removing policy), unpins, or deletes a task from the Pin Tab
- **THEN** that row's height collapses to zero and the rows below move up to fill the space, rather
  than the row disappearing and the list snapping in a single frame

#### Scenario: Pin Tab removal is immediate, not delayed
- **WHEN** the player removes a Pin Tab row
- **THEN** the collapse begins immediately with no undo window held before it, unlike the HUD's
  delayed completion

#### Scenario: The underlying completion/unpin/delete semantics are unchanged
- **WHEN** the player removes a Pin Tab row
- **THEN** the same completion, unpin, or delete action is performed as before (same authoritative
  effect on the pin set), with only the visual removal now animated

