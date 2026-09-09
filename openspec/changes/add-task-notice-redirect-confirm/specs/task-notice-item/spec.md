## MODIFIED Requirements

### Requirement: Accept converts a Task Notice into a normal tracked assignment
When a player holding a sealed Task Notice accepts it, the system SHALL create a
`ScribeAssignmentStore` record for it beginning in the Accepted state, placing the resulting task
via the existing `AcceptedIntoLabel` bind-to-first-legal-item mechanism used by in-range
assignments. From that point forward the assignment SHALL behave identically to an in-range
assignment: Complete and Discard sync through the existing mechanism regardless of either party's
distance or online status, and no further physical item is required. If the accepting player is
not the notice's recorded recipient, this Accept SHALL only proceed after the confirmation
required by the redirect requirement below; the resulting record's target SHALL be the accepting
player, not the original recipient.

#### Scenario: Accepting a notice creates a normal Accepted record
- **WHEN** the recorded recipient accepts a Task Notice
- **THEN** a `ScribeAssignmentStore` record is created for it in the Accepted state, placed via the
  same mechanism used for in-range assignments

#### Scenario: Post-accept behavior matches an in-range assignment
- **WHEN** an assignment accepted from a Task Notice is later completed or discarded
- **THEN** that outcome syncs to the Assigner through the same existing mechanism used for
  in-range assignments, with no additional physical item involved

## ADDED Requirements

### Requirement: Accepting a Task Notice not addressed to the holder requires explicit confirmation
When a player holding a sealed Task Notice taps Accept and they are not the notice's recorded
recipient, the system SHALL present a confirmation dialog naming the recorded recipient before
proceeding, rather than silently rejecting the action or silently redirecting it. The
confirmation SHALL be a standalone popup layered over the open Task Notice dialog, distinct from
that dialog's own controls. Declining the confirmation SHALL leave the notice and its assignment
record untouched. Confirming SHALL redirect the assignment to the confirming player and proceed
with the normal Accept transition.

#### Scenario: A non-recipient is warned before accepting
- **WHEN** a player who is not a Task Notice's recorded recipient taps Accept
- **THEN** a confirmation popup appears naming the recorded recipient and asking the player to
  confirm before the notice is tracked and consumed

#### Scenario: Declining the confirmation changes nothing
- **WHEN** a non-recipient is shown the confirmation popup and does not confirm
- **THEN** the Task Notice remains sealed and unconsumed, and no assignment record is changed

#### Scenario: Confirming proceeds with Accept
- **WHEN** a non-recipient confirms the popup
- **THEN** the Accept transition proceeds as described in the Accept requirement above, with the
  assignment redirected to the confirming player

### Requirement: A redirected acceptance leaves a trace in the Assigner's Sent Assignment History
When a Task Notice is accepted by a player who was not its recorded recipient, the assignment
record SHALL retain who it was originally addressed to and when the redirect occurred. The
Assigner's Sent Assignment History SHALL continue to show the record's state as Accepted, and its
expanded detail SHALL include an additional line naming the original recipient and the player who
actually accepted it instead. No other notification (toast, highlight, or similar) SHALL be sent
to the Assigner beyond this passive history detail.

#### Scenario: Sent Assignment History shows the redirect
- **WHEN** the Assigner expands a Sent Assignment History row for a notice that was redirected
- **THEN** the row's state still reads Accepted, and the expanded detail includes a line naming
  both the original recipient and the player who accepted it

#### Scenario: No active notification is sent for a redirect
- **WHEN** a Task Notice is redirected and accepted
- **THEN** the Assigner receives no toast or highlight about it — the Sent Assignment History
  detail is the only trace

### Requirement: The original recipient's Inbox silently loses a redirected notice
Once a Task Notice's assignment has been redirected to a different player, the original
recipient's Inbox SHALL no longer show any record for it. No ghost entry, read-only trace, or
notification SHALL be shown to the original recipient.

#### Scenario: The original recipient sees nothing after a redirect
- **WHEN** a Task Notice originally addressed to a player is accepted by someone else instead
- **THEN** the original recipient's Inbox shows no record of it, with no indication it was ever
  redirected away from them
