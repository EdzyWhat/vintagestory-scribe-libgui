## ADDED Requirements

### Requirement: The pinned HUD removes rows through the shared container's delayed policy
The pinned-task HUD SHALL render its rows through the shared `ScribeAnimatedList` container using
the delayed removal policy, rather than a HUD-private departing-row mechanism. When a pin is removed
destructively (completed under Unpin/Delete, or the unpin half of UnpinSink), the HUD SHALL cause
that row's identity to leave the container's item set, and the container SHALL animate the removal
via the delayed policy. The HUD SHALL NOT own a departing-row map, a snapshot/ghost widget, a
per-collapse cleanup callback, or a private collapse controller for departures.

#### Scenario: A destructively-completed HUD row departs through the container
- **WHEN** a player completes a pinned task under a destructive completion policy on the HUD
- **THEN** the pin's identity leaves the container's item set and the container animates its removal,
  with no HUD-side departing bookkeeping

#### Scenario: One animation path across all surfaces
- **WHEN** the editor, Read view, Pin Tab, and HUD all render their rows
- **THEN** all four route through the same `ScribeAnimatedList` container, and no surface retains a
  hand-wired copy of the collapse choreography

### Requirement: The HUD's undo window is preserved as delayed-removal timing
The delayed removal SHALL preserve the HUD's misclick-rescue undo window: a destructively-removed
row SHALL be held at FULL height for the undo window (during which its content MAY fade as a
countdown preview), and only after the window SHALL its height collapse. Holding at full height
during the window is load-bearing — it is what allows a misclick rescue, because the HUD hides the
Completion Policy and a completion may otherwise be a silent no-undo removal. The undo-window
duration and fade feel SHALL match the pre-migration behavior.

#### Scenario: A removed HUD row holds before collapsing
- **WHEN** a HUD pin is destructively completed
- **THEN** its row stays at full height for the undo window (optionally fading its content), and only
  after the window does its height collapse and the rows below slide up

#### Scenario: Undo within the window revives the row
- **WHEN** a HUD pin is destructively completed and the player re-pins/re-adds it before the undo
  window elapses
- **THEN** the departure is cancelled, the row is restored as a single live row at full opacity, and
  no ghost lingers

### Requirement: Non-removing HUD state changes are not delayed departures
A HUD pin whose identity remains in the item set SHALL NOT be treated as a delayed departure. In
particular, a Sink / UnpinSink completion that moves a still-present pin to the bottom SHALL be an
in-set reorder, not a container departure, and its existing sink countdown/overlay SHALL continue to
render correctly even while a different row is in a delayed departure.

#### Scenario: Sink moves a row without departing it
- **WHEN** a HUD pin is completed under the Sink policy
- **THEN** its identity stays in the item set and it moves to the bottom as a reorder, with no
  collapse-and-remove animation

#### Scenario: Sink overlay coexists with a concurrent departure
- **WHEN** one HUD row is sinking while another is in its delayed-removal undo window
- **THEN** both render correctly — the sinking row shows its countdown/overlay and the departing row
  holds-then-collapses — without interfering with each other
