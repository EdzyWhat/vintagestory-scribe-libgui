## REMOVED Requirements

### Requirement: Import/Export section is present but unwired
**Reason**: The Import/Export section is now wired. Its live behavior — clipboard-based JSON/TSV export and
import with best-effort reconstruction, injection safety, Overwrite/Append semantics, fresh TaskIds, and the
IMPORTED/EXPORTED stamp — is specified by the new `scriptorium-import-export` capability. The placeholder
slot becomes the real source/target slot; no inert controls remain.

The Transcribe view SHALL include a distinct Import/Export section, below the copy pair, with a
**placeholder slot** ("note to export from / import into") and controls for Export JSON, Export CSV,
and Import. The section SHALL be visibly present but inert ("coming in a later update"): the controls
perform no action when clicked and the placeholder slot does not yet accept or store an item. This
reserves the final layout without wiring the import/export logic and without adding a persisted
block-entity slot (the placeholder becomes a real slot when import/export is wired).

#### Scenario: Placeholder controls are shown and disabled
- **WHEN** the player opens a Scriptorium and selects the Transcribe tab
- **THEN** an Import/Export section is visible with a placeholder slot and Export JSON, Export CSV, and Import controls
- **AND** the controls are visibly disabled
- **AND** clicking a disabled control performs no import or export

#### Scenario: The placeholder section does not affect the copy slots
- **WHEN** the player interacts with the Import/Export section
- **THEN** the copy slots and copy button are unaffected
