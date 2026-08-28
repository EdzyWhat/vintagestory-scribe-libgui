## ADDED Requirements

### Requirement: A pinned Craft parent shows have/need on the HUD
A pinned `Craft` row SHALL show the same live have/need counter as a pinned Tracker (carried count
versus target), in addition to its Craft label. A pinned Link SHALL NOT show a counter.

#### Scenario: Craft parent counter on the HUD
- **WHEN** the player pins a Crafting Task whose output target is 16 and they carry 4 of that output
- **THEN** the HUD row shows a have/need readout of 4/16

### Requirement: The HUD title is Scribe Pins and matches row type size
The HUD header title SHALL be the localized string **Scribe Pins** (storm title unchanged). The
header title font size SHALL equal the HUD row font size (base HUD font × the player's HUD font
scale), not a smaller unscaled size.

#### Scenario: Title reads Scribe Pins
- **WHEN** the HUD is expanded and no temporal-storm title swap is active
- **THEN** the header text is "Scribe Pins"

#### Scenario: Header scales with HUD font
- **WHEN** the player increases HUD font scale
- **THEN** the header title grows by the same factor as the pin row text

### Requirement: The HUD settings gear can be hidden
The player SHALL have a client-local boolean (default on) that shows or hides the HUD header's
settings gear. When hidden, the Lectern/Notebook/Scriptorium Settings surface SHALL remain available.
A pinned note on the HUD SHALL render as text only: no checkbox and no unpin control.

#### Scenario: Gear hidden
- **WHEN** the HUD gear visibility setting is off
- **THEN** the HUD header has no settings gear

#### Scenario: Pinned note has no HUD checkbox
- **WHEN** the player has pinned a Text note
- **THEN** the HUD shows the note text without a completion checkbox or unpin affordance

## MODIFIED Requirements

### Requirement: The HUD is bounded by a configurable maximum row count
The system SHALL display at most the player's configured maximum number of HUD rows (defaulting to 3,
clamped to at most **30**). When the player has more pins than the maximum, the system SHALL show the
first maximum-count pins and indicate that additional pins exist rather than growing without bound.

#### Scenario: Pins beyond the maximum are summarized
- **WHEN** the player has more pinned tasks than their configured maximum HUD rows
- **THEN** the HUD shows exactly the maximum number of rows and indicates that further pins exist

#### Scenario: Changing the maximum is honored
- **WHEN** the player's maximum-HUD-rows setting changes and is synced
- **THEN** the HUD shows up to the new maximum on its next refresh

#### Scenario: Config can stay at 30
- **WHEN** the player sets maximum HUD rows to 30 (in Settings or client config) and reloads
- **THEN** the value remains 30 and is not clamped back to 10
