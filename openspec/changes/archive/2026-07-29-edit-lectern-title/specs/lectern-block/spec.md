## ADDED Requirements

### Requirement: All Lectern views render a document header above the central region
Every Lectern view (read, edit, pin) SHALL render a `BuildDocumentHeader(editable: bool)`
widget above the central region. The header is editable only in edit view (`editable: true`);
in all other views it is display-only (`editable: false`). The header widget is composed
from the title text (and, when editable, the pencil icon or inline input).

#### Scenario: Header rendered in all views
- **WHEN** a player navigates between the read, edit, and pin tabs
- **THEN** the title header remains visible above the central region in each view

#### Scenario: Editable flag controls pencil presence
- **WHEN** `BuildDocumentHeader(editable: false)` is composed
- **THEN** no pencil icon is included in the header layout
