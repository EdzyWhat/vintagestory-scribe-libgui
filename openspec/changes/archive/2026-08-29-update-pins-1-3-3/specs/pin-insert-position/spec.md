## ADDED Requirements

### Requirement: A Pin Insert setting governs where an unrelated new pin lands
The system SHALL let the player choose a **Pin Insert** preference — `Top` or `Bottom` — distinct
from the **New Task Insert** setting. When a player pins a task that has no pinned-parent
relationship in the pin list (it is a depth-0 row, or a depth-1 row whose parent is not currently
pinned), the new pin SHALL be inserted at index 0 (`Top`) or appended (`Bottom`) per this setting.
This setting SHALL NOT affect where a subtask attaches under its pinned parent, nor the clustering of
already-pinned children when their parent is pinned — both remain governed entirely by the pin's
document relationships, never by this setting.

#### Scenario: Top places an unrelated pin at the head of the list
- **WHEN** Pin Insert is `Top` and the player pins a task with no pinned parent
- **THEN** the new pin appears at index 0 of the pin list, ahead of previously pinned tasks

#### Scenario: Bottom places an unrelated pin at the end of the list
- **WHEN** Pin Insert is `Bottom` and the player pins a task with no pinned parent
- **THEN** the new pin is appended after every existing pin

#### Scenario: A pinned subtask ignores Pin Insert when its parent is pinned
- **WHEN** Pin Insert is `Top` and the player pins a subtask whose parent is already pinned
- **THEN** the subtask is inserted directly after its parent's pinned cluster, not at index 0

### Requirement: Missing Pin Insert setting defaults to Bottom
When the client settings JSON has no Pin Insert value, or the stored value is unknown, the system
SHALL treat the setting as `Bottom` — matching the pin-placement behavior that existed before this
setting was introduced, so existing players see no change in pin order until they explicitly opt in.

#### Scenario: Fresh install / pre-existing save defaults to Bottom
- **WHEN** a player has never saved a Pin Insert value
- **THEN** newly pinned unrelated tasks continue to append at the end of the pin list

#### Scenario: Unknown stored value falls back to Bottom
- **WHEN** the stored Pin Insert value is not a recognized `Top`/`Bottom` value
- **THEN** the system treats it as `Bottom`
