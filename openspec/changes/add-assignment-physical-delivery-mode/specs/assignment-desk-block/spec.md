## ADDED Requirements

### Requirement: The Create Assignments tab shows a delivery-mode toggle in Hybrid mode
When the server's `DeliveryMode` (per the `assignment-delivery-mode` capability) is `Hybrid`, the
Create Assignments tab SHALL show a two-position toggle labeled "Local Inboxes" and "Send a
Notice," pre-selected according to the range check for the currently-selected target, but always
freely switchable by the player to either position with no blocked or grayed-out state and no
confirmation step. In `AlwaysInstant` mode the toggle SHALL NOT appear at all (behavior is
unconditionally "Local Inboxes"); in `AlwaysPhysical` mode the toggle SHALL NOT appear either
(behavior is unconditionally "Send a Notice").

#### Scenario: The toggle appears in Hybrid mode
- **WHEN** the server's `DeliveryMode` is `Hybrid` and the Assigner has selected a target
- **THEN** the Create Assignments tab shows the "Local Inboxes" / "Send a Notice" toggle,
  pre-selected per the range check

#### Scenario: The player can override the computed default in either direction
- **WHEN** the toggle's computed default is "Local Inboxes" or "Send a Notice"
- **THEN** the player may select the other position instead, with no blocking, graying, or
  confirmation step

#### Scenario: The toggle is absent outside Hybrid mode
- **WHEN** the server's `DeliveryMode` is `AlwaysInstant` or `AlwaysPhysical`
- **THEN** the Create Assignments tab shows no delivery-mode toggle

### Requirement: An info button explains the delivery-mode mechanic
The Create Assignments tab SHALL show an info (ⓘ) button beside the delivery-mode toggle, matching
the existing info-button convention on the Edit page, which opens a longer-form explanation of the
range check and the two delivery paths. No other in-line warning or hint text SHALL be shown when
the player picks a toggle position against its computed default.

#### Scenario: The info button opens an explanation
- **WHEN** the player clicks the info (ⓘ) button beside the delivery-mode toggle
- **THEN** a longer-form explanation of the delivery-mode mechanic is shown

#### Scenario: No inline warning on an overridden toggle
- **WHEN** the player selects the toggle position opposite its computed default
- **THEN** no inline warning or note appears on the tab itself

### Requirement: "Send a Notice" mode reveals the Task Notice supply and output slots
When the delivery-mode toggle is set to "Send a Notice," the Create Assignments tab SHALL show two
additional slots alongside the existing staging slot: a stacking supply slot accepting blank Task
Notices, and a non-stacking output slot. When the toggle is set to "Local Inboxes," neither slot
SHALL be shown, and the tab SHALL be otherwise identical to its appearance before this change.

#### Scenario: Send a Notice reveals both slots
- **WHEN** the delivery-mode toggle is set to "Send a Notice"
- **THEN** the tab shows a blank-notice supply slot and an output slot in addition to the existing
  staging slot

#### Scenario: Local Inboxes shows neither slot
- **WHEN** the delivery-mode toggle is set to "Local Inboxes"
- **THEN** the tab shows neither the supply slot nor the output slot, matching its pre-existing
  appearance

### Requirement: Sending in "Send a Notice" mode requires a blank Task Notice
When the delivery-mode toggle is "Send a Notice," sending a batch SHALL consume one blank Task
Notice from the supply slot and place the sealed result in the output slot. If the supply slot is
empty, the Send control SHALL be blocked with a clear message rather than allowing a click that
fails.

#### Scenario: An empty supply slot blocks sending
- **WHEN** the delivery-mode toggle is "Send a Notice" and the supply slot is empty
- **THEN** the Send control is blocked with a message explaining that a blank Task Notice is
  required

#### Scenario: Sending consumes one blank notice
- **WHEN** the delivery-mode toggle is "Send a Notice," the supply slot holds at least one blank
  notice, and the player sends a batch
- **THEN** one blank Task Notice is consumed from the supply slot and the sealed result appears in
  the output slot
