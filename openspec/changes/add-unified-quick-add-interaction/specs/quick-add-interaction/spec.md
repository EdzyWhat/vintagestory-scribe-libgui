## ADDED Requirements

### Requirement: Shift+Right-Click performs quick-add on every Scribe writing surface

The system SHALL provide a single quick-add gesture — **Shift + Right-Click** — that behaves
identically across all three Scribe writing surfaces (the Lectern block, the Notebook item, and the
Tablet item). Performing quick-add SHALL, in one action: open that surface's editor, insert a new
empty task block at the **top** of the surface's document, and place the text-input caret focus on
that new task so the player can type immediately. The inserted task SHALL be an ordinary task block
subject to the surface's existing document policy (caps, persistence, sync) and SHALL be created
through the same document add-task operation the editor already uses (no new Core document
capability). If the surface's document policy is at its task cap, quick-add SHALL surface the same
"document full" feedback the editor's own add control surfaces, and SHALL NOT insert a task.

#### Scenario: Quick-add on a Lectern

- **WHEN** a player Shift+Right-Clicks a placed lectern
- **THEN** the lectern's editor opens with a new empty task at the top of the document and the caret
  focused on it, ready for typing

#### Scenario: Quick-add on a held Notebook

- **WHEN** a player Shift+Right-Clicks while holding a Notebook (not aimed such that ground placement
  applies)
- **THEN** the Notebook's editor opens with a new empty task at the top and the caret focused on it

#### Scenario: Quick-add on a held Tablet not aimed at water

- **WHEN** a player Shift+Right-Clicks while holding a Tablet and is NOT aiming at a water block
- **THEN** the Tablet's always-edit dialog opens with a new empty task at the top and the caret
  focused on it

#### Scenario: Quick-add respects the document task cap

- **WHEN** a player performs quick-add on a surface whose document is already at its task cap
- **THEN** no task is inserted and the surface surfaces the same "document full" in-game feedback its
  editor add control uses

### Requirement: The quick-add gesture is consistent and documented across surfaces

The quick-add trigger SHALL be the same modifier+button (**Shift + Right-Click**) on all three
surfaces, so a player learns one rule. Each surface's interaction help / tooltip surface SHALL
advertise the quick-add gesture where the platform provides an interaction-help affordance (held
items via `GetHeldInteractionHelp`, the block via its interaction hints). The gesture's effect
(open editor + new top task + caret focus) SHALL be the same on every surface even though the
underlying dialog differs (tabbed Lectern/Notebook editor vs. the always-edit Tablet dialog).

#### Scenario: One rule across surfaces

- **WHEN** a player who has learned quick-add on one surface uses Shift+Right-Click on another Scribe
  surface
- **THEN** the same quick-add effect occurs (editor open, new top task, caret focused)

#### Scenario: Held items advertise quick-add in interaction help

- **WHEN** a player views the held-interaction help for a Notebook or Tablet
- **THEN** the help lists the Shift+Right-Click quick-add action alongside the Ctrl+Shift+Right-Click
  ground-placement action
