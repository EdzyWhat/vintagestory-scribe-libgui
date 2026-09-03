## MODIFIED Requirements

### Requirement: Craftable, placeable Assignment Desk block
The system SHALL provide an Assignment Desk block, obtainable via a crafting recipe (and in
creative), that hosts a Scribe document dialog with six tabs, in nav order: Create Assignments
(the default view), Sent Assignment History, Inbox, Read, Editor, and Settings. It SHALL reuse
the existing writing-station block-entity and dialog base classes (server-authoritative lock,
autosave, persistence/sync) rather than introducing a parallel mechanism.

#### Scenario: Placing and opening the Assignment Desk
- **WHEN** a player crafts or spawns an Assignment Desk and right-clicks it
- **THEN** the block registers and renders its own model, and its dialog opens showing the
  Create Assignments tab by default, with Sent Assignment History, Inbox, Read, Editor, and
  Settings all reachable via nav buttons in that order

#### Scenario: Access grant lands on the last-active tab, never a nonexistent view
- **WHEN** a player who already has the Assignment Desk's dialog open on a non-default tab
  (Sent Assignment History, Inbox, Read, or Editor) receives a fresh access grant (e.g. a
  right-click reopen)
- **THEN** the dialog stays on that same tab rather than being forced back to Create Assignments
  or to a Notebook-style default Read landing
