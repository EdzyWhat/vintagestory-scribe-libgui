## ADDED Requirements

### Requirement: Transcribe is the Scriptorium's first tab and its plain-right-click default
The Scriptorium's Transcribe tab SHALL be the first nav button in its sidebar, ahead of Read,
Edit, Pinned, Guest Book, and (when shown) Inbox and Settings. A plain right-click on a placed
Scriptorium SHALL open the dialog on the Transcribe tab. Crouch (shift) + right-click SHALL
continue to perform the quick-add-a-task gesture unchanged. The block's right-click
interaction-help text SHALL read the Transcribe tab's own title ("Transcribe") instead of "Read".

#### Scenario: Right-click opens Transcribe
- **WHEN** a player plain-right-clicks a placed Scriptorium they have read access to
- **THEN** the dialog opens on the Transcribe tab

#### Scenario: Crouch+right-click still quick-adds
- **WHEN** a player crouches and right-clicks a placed Scriptorium
- **THEN** the editor opens with a fresh empty task inserted and focused, exactly as before this
  change

#### Scenario: Nav order
- **WHEN** the Scriptorium dialog is open
- **THEN** its sidebar nav buttons read, in order: Transcribe, Read, Edit, Pinned, Guest Book,
  (Inbox, if shown), Settings

#### Scenario: Interaction help text
- **WHEN** a player looks at a placed Scriptorium
- **THEN** its right-click interaction hint reads "Transcribe"
