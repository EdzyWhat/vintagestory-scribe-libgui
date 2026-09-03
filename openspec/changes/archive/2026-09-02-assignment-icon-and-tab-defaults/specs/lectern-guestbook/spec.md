## ADDED Requirements

### Requirement: Guest Book is the Lectern's first tab and its plain-right-click default
The Lectern's Guest Book tab SHALL be the first nav button in its sidebar, ahead of Read, Edit,
Pinned, and (when shown) Inbox and Settings. A plain right-click on a placed Lectern SHALL open
the dialog on the Guest Book tab. Crouch (shift) + right-click SHALL continue to perform the
quick-add-a-task gesture unchanged. The block's right-click interaction-help text SHALL read the
Guest Book tab's own title ("Guest Book") instead of "Read".

#### Scenario: Right-click opens Guest Book
- **WHEN** a player plain-right-clicks a placed Lectern they have read access to
- **THEN** the dialog opens on the Guest Book tab

#### Scenario: Crouch+right-click still quick-adds
- **WHEN** a player crouches and right-clicks a placed Lectern
- **THEN** the editor opens with a fresh empty task inserted and focused, exactly as before this
  change

#### Scenario: Nav order
- **WHEN** the Lectern dialog is open
- **THEN** its sidebar nav buttons read, in order: Guest Book, Read, Edit, Pinned, (Inbox, if
  shown), Settings

#### Scenario: Interaction help text
- **WHEN** a player looks at a placed Lectern
- **THEN** its right-click interaction hint reads "Guest Book"
