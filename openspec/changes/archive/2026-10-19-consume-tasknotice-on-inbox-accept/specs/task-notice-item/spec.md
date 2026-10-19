## ADDED Requirements

### Requirement: A Task Notice is proactively consumed when its assignment resolves through another path
When an assignment's Accept, Decline, Cancel, or Discard transition succeeds through a path other
than that specific Task Notice's own Accept/Decline dialog (for example, the Inbox tab's action
buttons on an Assignment Desk, Lectern, or Inbox block), the system SHALL search for and consume
any sealed Task Notice still carrying that assignment, so it stops rendering and behaving as a
live, actionable item. The search SHALL cover the same scope already used by the Task Notice
proximity-discovery signal: the affected player's own carried inventory, plus dropped items and
block-entity containers within that signal's existing scan radius of the affected player's current
position. If no matching notice is found within that scope, the notice is left unchanged and
continues to self-consume the next time a player interacts with it, exactly as before this
requirement existed.

#### Scenario: Accepting via the Inbox tab consumes a carried notice
- **WHEN** a player is carrying the sealed Task Notice for an assignment and accepts that
  assignment via the Inbox tab instead of opening the notice itself
- **THEN** the carried notice is consumed as part of that Accept, and no longer appears in their
  inventory

#### Scenario: Declining via the Inbox tab consumes a nearby notice
- **WHEN** a player declines an assignment via the Inbox tab while its sealed Task Notice sits in a
  container within the proximity-discovery signal's scan radius of them
- **THEN** that notice is consumed as part of the Decline

#### Scenario: A notice out of scan range is left for lazy self-consumption
- **WHEN** an assignment resolves via the Inbox tab while its sealed Task Notice is outside the
  proximity-discovery signal's scan scope (for example, held by a different, distant player)
- **THEN** the notice is left untouched by this requirement, and continues to consume itself the
  next time a player opens it and taps Accept or Decline, unchanged from prior behavior

#### Scenario: No matching notice exists
- **WHEN** an assignment resolves via the Inbox tab and no sealed Task Notice for it exists anywhere
  in scope (it was already consumed earlier)
- **THEN** nothing happens beyond the normal state transition — this requirement is a no-op
