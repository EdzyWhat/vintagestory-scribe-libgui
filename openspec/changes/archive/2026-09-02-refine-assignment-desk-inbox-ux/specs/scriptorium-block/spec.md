## MODIFIED Requirements

### Requirement: The Scriptorium exposes an Inbox nav button

The Scriptorium's dialog SHALL show a nav button that switches its view to the shared Inbox tab
(`inbox-tab` capability). This supersedes the Scriptorium's earlier reserved plan to host its own
Scriptorium-only Assign & History surface — that create-and-send role now belongs exclusively to
the Assignment Desk (`assignment-desk-block`); the Scriptorium only ever gains a viewing nav
button, never a create affordance. Per `inbox-tab`'s assignment-history gating requirement, this
nav button SHALL be shown only once the viewing player has received at least one assignment, ever;
before that, it SHALL NOT appear.

#### Scenario: Opening the Inbox from a Scriptorium
- **WHEN** a player at a Scriptorium clicks its Inbox nav button
- **THEN** the Scriptorium's dialog switches to the same Inbox tab shown by the Assignment Desk
  and the standalone Inbox block

#### Scenario: The Scriptorium never gains a create-and-send affordance
- **WHEN** a player opens a Scriptorium's dialog
- **THEN** no control for creating and sending a new assignment is present

#### Scenario: No Inbox button before any assignment history
- **WHEN** a player who has never received an assignment opens a Scriptorium's dialog
- **THEN** no Inbox nav button is present
