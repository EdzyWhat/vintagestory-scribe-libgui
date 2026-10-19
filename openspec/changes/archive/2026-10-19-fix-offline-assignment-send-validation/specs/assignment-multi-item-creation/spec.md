## MODIFIED Requirements

### Requirement: Sending a batch creates one independent assignment per selected row
Sending the batch SHALL create one independent assignment record per selected row, all addressed
to the single recipient chosen via the existing target-player picker. Each created assignment
SHALL carry that row's full shape (kind, text, target item/quantity, link fields, depth) and SHALL
behave exactly like any other assignment from that point on — its own Accept/Decline/Cancel/
Discard lifecycle, its own row in the recipient's Inbox. No bundling identifier or "batch" concept
SHALL be introduced; declining or completing one row's assignment SHALL have no effect on any other
row sent in the same batch.

The recipient MAY be any player the target-player picker validly presents, including a currently
offline player who has previously connected to the world. The server SHALL accept such a recipient
and create the assignment(s) exactly as it would for a currently-online recipient — the recipient's
current online/offline state SHALL NOT by itself cause the batch to be rejected. A batch SHALL only
be rejected for a recipient the server cannot recognize as a real player at all (an unknown UID
that is neither online nor previously known to the world).

#### Scenario: A mixed-kind batch arrives as independent assignments
- **WHEN** the player selects a Task row, a Tracker row, and a Link row from the staged document
  and sends them to one recipient
- **THEN** the recipient's Inbox shows three separate assignments, each with its own state and its
  own Accept/Decline/Cancel/Discard controls

#### Scenario: One row's outcome does not affect its batch-mates
- **WHEN** a recipient declines one assignment that was sent as part of a multi-row batch
- **THEN** the other assignments sent in that same batch are unaffected and keep their own
  independent state

#### Scenario: Sending to a currently-offline, previously-known recipient succeeds
- **WHEN** the Assigner selects a target-player who has connected to the world before but is not
  currently online, and sends a batch of selected rows to them
- **THEN** the assignment(s) are created exactly as they would be for an online recipient — the
  recipient sees them in their Inbox (Local Inboxes mode) or receives a Sealed Task Notice (Send a
  Notice mode) once delivered, rather than the send silently doing nothing

#### Scenario: Sending to an unrecognized target is still rejected
- **WHEN** a send request names a target UID that is neither currently online nor previously known
  to the world
- **THEN** the server rejects the batch and creates no assignment records, exactly as before this
  change
