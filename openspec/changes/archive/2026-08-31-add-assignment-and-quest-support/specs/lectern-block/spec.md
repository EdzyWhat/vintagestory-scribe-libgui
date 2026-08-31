## ADDED Requirements

### Requirement: The Lectern exposes an Inbox nav button
The Lectern's dialog SHALL show a nav button (alongside its existing Guestbook/History/etc. nav
buttons) that switches its view to the shared Inbox tab (`inbox-tab` capability), so a player can
view and act on their assignments without leaving the Lectern. The Lectern SHALL NOT gain a
create-and-send affordance — only the Assignment Desk can create assignments.

#### Scenario: Opening the Inbox from a Lectern
- **WHEN** a player at a Lectern clicks its Inbox nav button
- **THEN** the Lectern's dialog switches to the same Inbox tab shown by the Assignment Desk and
  the standalone Inbox block

### Requirement: The Lectern shows the ambient unseen-assignment particle
A placed Lectern SHALL emit the ambient particle effect defined by the `inbox-tab` capability
when the viewing player has a New (unseen) assignment and is within range.

#### Scenario: A Lectern particles for a player with an unseen assignment
- **WHEN** a player with an unseen assignment is within range of a placed Lectern
- **THEN** that Lectern emits the ambient particle effect for that player's client
