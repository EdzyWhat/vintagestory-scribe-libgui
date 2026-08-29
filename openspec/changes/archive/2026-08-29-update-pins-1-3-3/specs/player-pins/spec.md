## MODIFIED Requirements

### Requirement: Pinning a subtask inserts it under its pinned parent
When a player pins a depth-1 row whose parent (the depth-0 row that owns its contiguous run in the
source document) is already in that player's pin list, the new pin SHALL be inserted immediately
after that parent's HUD cluster: the parent pin, then any already-contiguous pins whose tasks are in
that owned run. The child SHALL NOT be appended at the end of the list. Parent identity SHALL come
from the source document, never from “any depth-0 pin from the same notebook.” If the parent is not
pinned, or the source document cannot be resolved, the pin SHALL insert per the player's **Pin
Insert** setting (Top or Bottom), the same as an unrelated depth-0 pin. Pinning a child SHALL NOT
auto-pin the parent.

#### Scenario: Pinning a child under a pinned parent
- **WHEN** the player has pinned a Craft parent and then pins one of its ingredient children
- **THEN** the child pin sits directly under that parent in the pin list, not at the end

#### Scenario: Parent not pinned appends
- **WHEN** the player pins an ingredient child whose Craft parent is not pinned
- **THEN** the child is inserted at the Top or Bottom of the pin list per the player's Pin Insert
  setting (Bottom by default, matching the historical always-append behavior); the parent is not
  pinned automatically

### Requirement: Pinning a parent gathers its already-pinned children
When a player pins a depth-0 row, the pin SHALL be inserted per the player's **Pin Insert** setting
(Top or Bottom), then any of that player's existing pins whose `TaskId` is in that parent's current
document owned run SHALL be moved to sit immediately after it, preserving those children's relative
order. This clustering of already-pinned children happens regardless of which edge the parent pin
itself was inserted at.

#### Scenario: Pinning the parent later clusters children
- **WHEN** two ingredient children are already pinned and the player then pins their parent
- **THEN** the parent appears in the pin list with those two children directly under it in their
  prior relative order, at whichever edge (Top or Bottom) the player's Pin Insert setting places the
  parent
