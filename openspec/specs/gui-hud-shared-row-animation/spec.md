# gui-hud-shared-row-animation Specification

## Purpose
Defines how the pinned-task HUD participates in the shared row-animation system: it renders its rows
through the same `ScribeAnimatedList` container the editor, Read view, and Pin Tab use, so there is one
collapse/entry animation path across all four surfaces. The HUD's misclick-rescue undo window is
preserved as a HUD-owned deferred-send phase on a live row (not a container "delayed removal" ghost),
and the HUD's row order agrees with the Pin Tab.

## Requirements
### Requirement: The pinned HUD removes rows through the shared container's Immediate policy
The pinned-task HUD SHALL render its rows through the shared `ScribeAnimatedList` container using the
`Immediate` removal policy — the same policy the editor, Read view, and Pin Tab already use — rather
than a HUD-private departing-row mechanism. When a pin's identity leaves the container's item set (the
destructive completion has been SENT and the pin is now awaiting the server's removal push), the
container SHALL animate the collapse. The HUD SHALL NOT own a departing-row map, a snapshot/ghost
widget, a per-collapse cleanup callback, or a private collapse controller for departures.

#### Scenario: A destructively-completed HUD row collapses through the container
- **WHEN** a HUD pin's destructive completion is sent (its undo window has elapsed) and its identity
  leaves the item set handed to the container
- **THEN** the container animates its collapse — rows below slide up — with no HUD-side departing
  bookkeeping (`departing` / `BeginDeparting` / `ReconcileDeparting` / `CancelDeparting` /
  `OnDepartingCollapsed`)

#### Scenario: One animation path across all surfaces
- **WHEN** the editor, Read view, Pin Tab, and HUD all render their rows
- **THEN** all four route through the same `ScribeAnimatedList` container under the `Immediate` policy,
  and no surface retains a hand-wired copy of the collapse choreography

### Requirement: The HUD's undo window is a live-row deferred-send phase, not an animation hold
The HUD's misclick-rescue undo window SHALL remain a HUD-owned deferred-send phase: when a pin is
completed under a destructive policy, the pin's identity STAYS in the item set for the undo window,
its row stays LIVE (its checkbox clickable) at full height, and its text MAY fade as a countdown
preview. Undo SHALL be unchecking that live row, which cancels the pending send so nothing reaches the
server. Only after the window elapses does the completion send, the pin leave the item set, and the
container's `Immediate` collapse begin. The undo-window duration and fade feel SHALL match the
pre-migration behavior.

The undo window is therefore NOT expressed as a container "delayed removal" (a held, faded ghost): a
ghost cannot host the live checkbox the undo depends on. Holding the LIVE row at full height during the
window is load-bearing — it is what allows the misclick rescue, because the HUD hides the Completion
Policy and a completion may otherwise be a silent no-undo removal.

#### Scenario: A destructively-completed HUD row holds live before it departs
- **WHEN** a HUD pin is destructively completed
- **THEN** its row stays LIVE at full height for the undo window (its checkbox clickable, its text
  optionally fading), and only after the window does the send fire, the pin leave the item set, and the
  container collapse the row

#### Scenario: Undo within the window cancels the send
- **WHEN** a HUD pin is destructively completed and the player unchecks it before the undo window
  elapses
- **THEN** the pending send is cancelled (nothing reached the server), the row is restored as a single
  live row at full opacity, and no collapse or ghost occurs — the pin never left the item set

### Requirement: Non-removing HUD state changes are not container departures
A HUD pin whose identity remains in the item set SHALL NOT be treated as a container departure. In
particular, a Sink / UnpinSink completion that moves a still-present pin to the bottom SHALL be an
in-set reorder, not a container departure, and its existing sink countdown/overlay SHALL continue to
render correctly even while a different row is collapsing out.

#### Scenario: Sink moves a row without departing it
- **WHEN** a HUD pin is completed under the Sink policy
- **THEN** its identity stays in the item set and it moves to the bottom as a reorder, with no
  collapse-and-remove animation

#### Scenario: Sink overlay coexists with a concurrent collapse
- **WHEN** one HUD row is sinking while another has departed the item set and is collapsing
- **THEN** both render correctly — the sinking row shows its countdown/overlay and the departed row
  collapses — without interfering with each other

### Requirement: HUD rows enter and order like the other surfaces
A newly-appearing HUD row (a freshly-pinned task, or one crossing into the capped window because
another collapsed out) SHALL slide in via the container's entry animation, matching the editor / Read
view / Pin Tab, rather than snapping. No entry animation SHALL fire on the HUD's first open or a
ForceRebuild remount. The HUD's row ORDER SHALL follow the Pin Tab's display order
(`ScribePinOrdering.ForDisplay` under the sinking policies, raw pin order otherwise), so the two
surfaces agree, retaining only the HUD-specific overlays that must survive (the durable session-sink
bottom-hold and the in-undo-window in-place hold).

#### Scenario: A newly-pinned HUD row slides in
- **WHEN** a player pins a task while the HUD is open
- **THEN** its HUD row slides in with the same entry animation as the editor / Read / Pin Tab, and no
  entry animation fires on first open or a ForceRebuild

#### Scenario: The HUD and Pin Tab agree on order
- **WHEN** the same pin set is shown on the HUD and the Pin Tab under any completion policy
- **THEN** both render the pins in the same order, and the cross-surface Sink agreement is preserved
