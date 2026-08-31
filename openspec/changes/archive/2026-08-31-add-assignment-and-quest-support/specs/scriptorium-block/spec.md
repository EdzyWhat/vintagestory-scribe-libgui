## ADDED Requirements

### Requirement: The Scriptorium exposes an Inbox nav button
The Scriptorium's dialog SHALL show a nav button that switches its view to the shared Inbox tab
(`inbox-tab` capability). This supersedes the Scriptorium's earlier reserved plan to host its own
Scriptorium-only Assign & History surface — that create-and-send role now belongs exclusively to
the Assignment Desk (`assignment-desk-block`); the Scriptorium only ever gains a viewing nav
button, never a create affordance.

#### Scenario: Opening the Inbox from a Scriptorium
- **WHEN** a player at a Scriptorium clicks its Inbox nav button
- **THEN** the Scriptorium's dialog switches to the same Inbox tab shown by the Assignment Desk
  and the standalone Inbox block

#### Scenario: The Scriptorium never gains a create-and-send affordance
- **WHEN** a player opens a Scriptorium's dialog
- **THEN** no control for creating and sending a new assignment is present

### Requirement: The Scriptorium shows the ambient unseen-assignment particle
A placed Scriptorium SHALL emit the ambient particle effect defined by the `inbox-tab` capability
when the viewing player has a New (unseen) assignment and is within range.

#### Scenario: A Scriptorium particles for a player with an unseen assignment
- **WHEN** a player with an unseen assignment is within range of a placed Scriptorium
- **THEN** that Scriptorium emits the ambient particle effect for that player's client
