## MODIFIED Requirements

### Requirement: A Handbook explainer entry describes the Tracker and Link task types
The mod SHALL register a Handbook entry that explains what Item Tracker, Link, **and Crafting Task** types are and how to create them (via the per-item "Add to Scribe" / "Add Crafting Task" links on an item's Handbook page). This entry SHALL be the destination the footer guide opens. The entry's title SHALL name all three types (not only Trackers and Links). Crafting Tasks SHALL be described as handbook-created, bound to a grid recipe variant, generating ingredient subtasks (including litre trackers for liquid ingredients).

#### Scenario: The explainer entry exists and is reachable
- **WHEN** the player opens the Handbook to the Tracker/Link explainer entry (e.g. via the footer guide)
- **THEN** the entry describes Item Trackers, Links, **and Crafting Tasks** and directs the player to an item page's "Add to Scribe" / "Add Crafting Task" links

#### Scenario: Crafting Tasks are not omitted
- **WHEN** a player reads the task-types explainer
- **THEN** it does not say Scribe has only two item-bound types, and it explains that a Crafting Task builds an ingredient shopping list from a recipe
