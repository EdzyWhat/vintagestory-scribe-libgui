## MODIFIED Requirements

### Requirement: The inventory is surfaced as its own Scriptorium dialog tab
The Scriptorium dialog SHALL present the inventory as a distinct nav-rail tab labeled **"Transcribe"**,
selectable alongside the existing Read / Task Editor / Pinned / Guest Book / Settings tabs. Selecting the
tab SHALL show the two slots and allow moving Scribe items between the player and the slots. Switching
away from and back to the tab SHALL show the current stored contents. This tab SHALL appear only on the
Scriptorium dialog, not on any other Scribe surface. The "Transcribe" name reflects that the view exists
for copying documents (and, later, import/export), not general storage.

#### Scenario: The Transcribe tab is reachable
- **WHEN** the player opens a Scriptorium and selects the Transcribe tab from the nav rail
- **THEN** the two slots are shown with their current contents
- **AND** the nav-button tooltip and the view heading both read "Transcribe"

#### Scenario: The tab is Scriptorium-only
- **WHEN** the player opens a Lectern, Notebook, or Tablet dialog
- **THEN** no Transcribe tab is present
