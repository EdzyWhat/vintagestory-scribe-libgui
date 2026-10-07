## ADDED Requirements

### Requirement: Carried-item dialogs are not world-anchored in Immersive Mouse Mode
The Notebook, Clockmaker Notebook, and Clay Tablet dialogs SHALL keep a fixed screen position when Immersive Mouse Mode is enabled, and SHALL NOT be repositioned from the camera orientation or player position. Block-bound dialogs (Lectern, Chalkboard, Scriptorium, Assignment Desk, Inbox) SHALL continue to float above their block in Immersive Mouse Mode. Behavior with Immersive Mouse Mode disabled SHALL be unchanged for all dialogs.

#### Scenario: Notebook stays put while looking around
- **WHEN** Immersive Mouse Mode is enabled and the player opens a Notebook, then turns the camera in any direction
- **THEN** the Notebook window remains at the same screen position

#### Scenario: Carried-item dialog can be dragged in Immersive Mouse Mode
- **WHEN** Immersive Mouse Mode is enabled and the player drags an open Notebook or Tablet by its title bar
- **THEN** the window follows the drag and stays where it is released

#### Scenario: Block dialog still floats above its block
- **WHEN** Immersive Mouse Mode is enabled and the player opens a Lectern dialog, then turns the camera
- **THEN** the window continues to track the projected position of the Lectern

#### Scenario: Immersive Mouse Mode disabled
- **WHEN** Immersive Mouse Mode is disabled
- **THEN** every Scribe dialog opens and behaves exactly as before this change
