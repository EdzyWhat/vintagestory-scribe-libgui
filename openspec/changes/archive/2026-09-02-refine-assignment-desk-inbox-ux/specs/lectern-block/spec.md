## MODIFIED Requirements

### Requirement: The Lectern exposes an Inbox nav button
The Lectern's dialog SHALL show a nav button (alongside its existing Guestbook/History/etc. nav
buttons) that switches its view to the shared Inbox tab (`inbox-tab` capability), so a player can
view and act on their assignments without leaving the Lectern. The Lectern SHALL NOT gain a
create-and-send affordance — only the Assignment Desk can create assignments. Per `inbox-tab`'s
assignment-history gating requirement, this nav button SHALL be shown only once the viewing player
has received at least one assignment, ever; before that, it SHALL NOT appear.

#### Scenario: Opening the Inbox from a Lectern
- **WHEN** a player at a Lectern clicks its Inbox nav button
- **THEN** the Lectern's dialog switches to the same Inbox tab shown by the Assignment Desk and
  the standalone Inbox block

#### Scenario: No Inbox button before any assignment history
- **WHEN** a player who has never received an assignment opens a Lectern's dialog
- **THEN** no Inbox nav button is present
