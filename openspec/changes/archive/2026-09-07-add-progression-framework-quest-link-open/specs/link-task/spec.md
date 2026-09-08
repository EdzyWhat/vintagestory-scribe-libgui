## MODIFIED Requirements

### Requirement: A Link task behaves as a hyperlink from every surface
Clicking a Link task's label SHALL open its referenced destination, whether the click occurs in a
Scribe UI (Scriptorium, Lectern, Notebook, Tablet) or on the pinned-task HUD. For a plain item, guide
page, or VS Quest Link, that destination is the referenced Handbook page. For a **Progression
Framework** Quest Link, the destination is instead that backend's own Quest Log dialog — a quest has
no Handbook page, so it is never routed through the Handbook-open path. This activation is distinct
from the row's completion control: opening the destination SHALL NOT complete or delete the Link;
completion remains a separate, explicit player action.

#### Scenario: Clicking a link in the Scribe UI opens the handbook page
- **WHEN** the player clicks a Link task's label in any Scribe UI
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Clicking a link on the HUD opens the handbook page
- **WHEN** the player clicks a pinned Link task on the HUD
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Clicking a Progression Framework Quest Link opens the Quest Log, not a Handbook page
- **WHEN** the player clicks a Quest Link whose backend is Progression Framework, from any surface
- **THEN** the game opens Progression Framework's own Quest Log dialog, landed on its Quest Log tab,
  rather than attempting to open a Handbook page

#### Scenario: Activating a link leaves its completion state unchanged
- **WHEN** the player activates (opens) a Link task that is not completed
- **THEN** after the page or dialog opens, the Link is still not completed
