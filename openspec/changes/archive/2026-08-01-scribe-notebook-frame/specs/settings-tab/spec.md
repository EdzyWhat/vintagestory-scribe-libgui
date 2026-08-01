## ADDED Requirements

### Requirement: A Pixel Art Size preference drives the lectern layout
The Appearance section SHALL expose a permanent "Pixel Art Size" numeric preference — the driving width `W`
of the lectern's proportional layout. It SHALL be a numeric-entry control (not a slider) that increments by
10 and is clamped to the range 300..1000 on entry and on load. Changing it SHALL rescale the open lectern
live, following the same write-through-with-live-preview behavior as the other appearance preferences.

#### Scenario: Pixel Art Size appears under Appearance
- **WHEN** the player opens the settings surface
- **THEN** the Appearance section shows a "Pixel Art Size" numeric-entry control stepping by 10, bounded to
  300..1000

#### Scenario: Changing Pixel Art Size rescales the open lectern live
- **WHEN** the player changes Pixel Art Size while a lectern is open
- **THEN** the open lectern's layout rescales to the new width immediately, with no separate apply step

#### Scenario: Pixel Art Size is clamped and persisted
- **WHEN** a value outside 300..1000 is entered, or a hand-edited config holds an out-of-range value, and
  it is loaded
- **THEN** the value is clamped to the range (and snapped to the 10-step grid), and in-range values persist
  across sessions like the other client-local preferences
