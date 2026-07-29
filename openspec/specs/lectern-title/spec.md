# lectern-title Specification

## Purpose
TBD - created by archiving change edit-lectern-title. Update Purpose after archive.
## Requirements
### Requirement: Title is displayed in all Lectern views
The Lectern GUI SHALL display the document's title as a text element above the central region
in every view (read, edit, and pin tab). The title SHALL reflect the current document title
at all times. If no title has been set, the default `"Lectern"` SHALL be shown.

#### Scenario: Title visible in read view
- **WHEN** a player opens the Lectern in read view
- **THEN** the document title is shown above the task/note list

#### Scenario: Title visible in edit view
- **WHEN** a player opens the Lectern in edit view
- **THEN** the document title is shown above the row editor

#### Scenario: Title visible in pin view
- **WHEN** a player navigates to the Pins tab
- **THEN** the document title is shown above the pinned task list

#### Scenario: Default title shown for unset lecterns
- **WHEN** a player opens a Lectern that has never had a title set
- **THEN** the title displayed is `"Lectern"`

### Requirement: Pencil affordance appears in edit view only
In edit view, a pencil icon (using the `"scribeedit"` SVG) SHALL appear to the right of the
title text. The pencil icon SHALL NOT appear in read view or pin view.

#### Scenario: Pencil present in edit view
- **WHEN** a player is in edit view
- **THEN** the pencil icon is visible to the right of the title

#### Scenario: Pencil absent in read and pin views
- **WHEN** a player is in read view or pin view
- **THEN** no pencil icon is shown next to the title

### Requirement: Clicking the pencil activates inline title editing
When the player clicks the pencil icon, the title text display SHALL be replaced with a
single-line text input pre-populated with the current title. The input SHALL accept at most
80 characters. Focus SHALL be placed in the input immediately.

#### Scenario: Pencil click activates input
- **WHEN** a player clicks the pencil icon in edit view
- **THEN** the title text is replaced by a focused single-line text input containing the current title

#### Scenario: Input enforces 80-char maximum
- **WHEN** a player types more than 80 characters into the title input
- **THEN** the input does not accept characters beyond the 80th

### Requirement: On blur the title is saved and the row reverts to text display
When the title input loses focus (the player clicks away, presses Tab, or switches view),
the input SHALL be committed: leading/trailing whitespace is trimmed, an empty result is
replaced with `"Lectern"`, the result is clamped to 80 chars, written to the document, and
the row reverts to static text display. The document SHALL be flushed to the server via the
standard `FlushIfDirty()` path.

#### Scenario: Non-empty title saves and reverts on blur
- **WHEN** the player types "Stone Age Notes" in the title input and then clicks away
- **THEN** the title row shows "Stone Age Notes" as static text and the document is saved

#### Scenario: Empty title resets to default on blur
- **WHEN** the player clears the title input and clicks away
- **THEN** the title row shows `"Lectern"` and the document is saved with that default

#### Scenario: Whitespace-only title resets to default on blur
- **WHEN** the player enters only spaces in the title input and clicks away
- **THEN** the title row shows `"Lectern"`

### Requirement: Title persists across world reload
A Lectern title set by a player SHALL survive world save/reload and chunk unload/reload,
because it is serialized as part of the document bytes.

#### Scenario: Title survives server restart
- **WHEN** a player sets a Lectern title and the world is saved and reloaded
- **THEN** reopening the Lectern shows the same title

