# handbook-scribe-entry Specification

## Purpose
TBD - created by archiving change add-tracker-link-tasks. Update Purpose after archive.
## Requirements
### Requirement: Handbook item pages offer an "Add to Scribe" action
Every collectible's Handbook page SHALL present an "Add to Scribe" action for that item. The action
SHALL be added by extending the vanilla handbook-info composition (a Harmony postfix on
`CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo`, appending a clickable link
component). Harmony ships with the base game (`Lib/0Harmony.dll`) and is not a new mod dependency.
The action SHALL be present on both item and block Handbook pages that use the standard handbook
behavior.

#### Scenario: The action appears on an item's handbook page
- **WHEN** the player opens the Handbook page for an item that uses the standard handbook behavior
- **THEN** an "Add to Scribe" action for that item is shown on the page

#### Scenario: The action carries the page's item identity
- **WHEN** the player triggers "Add to Scribe" from an item's Handbook page
- **THEN** the created task references that specific item (its asset code), not a generic placeholder

### Requirement: Add to Scribe resolves a target surface in three tiers
Triggering "Add to Scribe" SHALL create a new task for that item, resolving which Scribe surface
receives it in three ordered tiers:
1. If a Scribe surface is **currently open** — a block (Scriptorium, Lectern) or an item (Notebook,
   Tablet) — the task SHALL be added to that open surface.
2. Otherwise, if the player **carries a Scribe item**, the game SHALL open the UI for the
   last-opened Scribe item the player still carries (or, if there is no last-opened record, a Scribe
   item they carry) and add the task to it.
3. Otherwise, the action SHALL show a Vintage Story error to the effect of "You need a Scribe item
   to do that." and SHALL create no task.
In tiers 1 and 2 the task SHALL be added through the normal server-authoritative edit path (not
written client-side only).

#### Scenario: Task lands in the already-open surface
- **WHEN** the player has any Scribe surface open (block or item) and triggers "Add to Scribe"
- **THEN** a new task for that item is appended to that open surface's document and is visible there

#### Scenario: A carried Scribe item is opened and receives the task
- **WHEN** no Scribe surface is open but the player carries a Scribe item, and they trigger "Add to
  Scribe"
- **THEN** the last-opened carried Scribe item's UI opens and the new task is added to it

#### Scenario: No Scribe item reports guidance
- **WHEN** the player triggers "Add to Scribe" with no Scribe surface open and no Scribe item carried
- **THEN** the player is shown a "You need a Scribe item to do that." error and no task is created

### Requirement: Footer Tracker/Link entries guide the player to the Handbook
The editor footer's add-picker SHALL offer Tracker and Link entries. Because these kinds require an
item identity the footer cannot supply, activating either entry SHALL NOT create a block directly;
instead it SHALL route the player to the Handbook: if the Handbook is **not open**, the game SHALL
open it to the Tracker/Link explainer entry; if the Handbook is **already open**, the game SHALL
fire a Vintage Story error instructing the player to scroll to the bottom of the current entry and
click the "Add to Scribe" link for the task type they want.

#### Scenario: Footer entry opens the explainer when the Handbook is closed
- **WHEN** the player clicks the footer Tracker (or Link) entry with the Handbook closed
- **THEN** the Handbook opens to the Tracker/Link explainer entry and no block is created

#### Scenario: Footer entry instructs when the Handbook is already open
- **WHEN** the player clicks the footer Tracker (or Link) entry while the Handbook is open
- **THEN** a Vintage Story error tells them to scroll to the current entry's bottom and click the
  "Add to Scribe" link for that task type, and no block is created

### Requirement: A Handbook explainer entry describes the Tracker and Link task types
The mod SHALL register a Handbook entry that explains what Tracker and Link tasks are and how to
create them (via the per-item "Add to Scribe" link). This entry SHALL be the destination the footer
guide opens.

#### Scenario: The explainer entry exists and is reachable
- **WHEN** the player opens the Handbook to the Tracker/Link explainer entry (e.g. via the footer
  guide)
- **THEN** the entry describes both task types and directs the player to an item page's "Add to
  Scribe" link

### Requirement: Add to Scribe seeds a Tracker or a Link
The task created by "Add to Scribe" SHALL be creatable as either a **Tracker** (with an inline
quantity entry so the player sets the target N on the row using the existing arrow-affordance
numeric stepper) or a **Link** (a reference to that item's Handbook page, no quantity). A Tracker
created this way SHALL start at `CurrentQuantity` 0 and a valid `TargetQuantity` (at least 1); a
Link created this way SHALL carry the item's Handbook reference and no quantity.

#### Scenario: Creating a tracker sets the target inline
- **WHEN** the player adds an item as a Tracker and enters a target quantity on the row's numeric
  stepper
- **THEN** the row is a Tracker with that `TargetQuantity` and `CurrentQuantity` 0

#### Scenario: Creating a link needs no quantity
- **WHEN** the player adds an item as a Link
- **THEN** the row is a Link referencing that item's Handbook page, with no quantity entry shown

