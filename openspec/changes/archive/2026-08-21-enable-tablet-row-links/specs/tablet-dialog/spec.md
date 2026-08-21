## ADDED Requirements

### Requirement: The tablet's always-edit view activates item-row links

Because the tablet dialog has no separate read view — a wet (editable) tablet renders the editor
row path directly — the click-to-open-Handbook affordance that the Lectern/Notebook read view
provides SHALL be surfaced on the tablet's always-edit central region for item-kind rows (Link,
Tracker, and Craft). Activation SHALL be scoped to the tablet: the shared editor row path SHALL
gain the affordance only when the dialog opts in (a `ScribeDialogBase` seam the tablet turns on),
so the Lectern and Notebook editor views remain non-clickable and continue to rely on their own
read view for link activation.

The affordance SHALL be a distinct hit region from the row's editing controls, per kind:

- A **Link** row SHALL open its referenced Handbook page when its name label is clicked. A Link
  row has no editable inline field, so the whole name label is the activation region.
- A **Tracker** or **Craft** row SHALL open its target item's Handbook page when its **name
  label** is clicked, while the row's existing inline numeric target-quantity field (the `+/-`
  stepper) SHALL continue to receive clicks on the **number** and edit the target quantity. The
  name label and the numeric field SHALL be independent hit regions on the same row.

Activation SHALL be distinct from the row's completion control: opening a Handbook page SHALL NOT
complete, delete, or reorder the task. This behavior applies only to a wet (editable) tablet;
a hardened/fired tablet already renders through the read view, which provides link activation
unchanged.

#### Scenario: Clicking a Link task on a wet tablet opens its page

- **WHEN** a player clicks the name of a Link task on a wet (editable) tablet
- **THEN** the game opens that Link's referenced Handbook page, and the Link's completion state is
  unchanged

#### Scenario: Clicking a Tracker/Craft name on a wet tablet opens the item page

- **WHEN** a player clicks the item **name** of a Tracker or Craft task on a wet tablet
- **THEN** the game opens that item's Handbook page (the Tracker's `TargetItemCode` or the Craft
  parent's output item), and the task's completion state is unchanged

#### Scenario: Clicking the number still edits the target on a wet tablet

- **WHEN** a player clicks the numeric target-quantity control (or its `+/-` steppers) on a
  Tracker or Craft row on a wet tablet
- **THEN** the numeric field edits the target quantity as before, and no Handbook page is opened

#### Scenario: Lectern and Notebook editors remain non-clickable

- **WHEN** a player views an item-kind row in the Lectern or Notebook **editor** view
- **THEN** the row's name is not a Handbook link (link activation remains available only through
  their read view), unchanged from before this change

### Requirement: Craft rows resolve their Handbook page on activation

The link-activation dispatch that opens a Handbook page from an item-row name SHALL resolve a
**Craft** row to its output item's Handbook page (via the Craft parent's `TargetItemCode`), in
addition to the existing Link (`LinkTarget`) and Tracker (`TargetItemCode`) resolution. This SHALL
hold wherever the dispatch is used — the tablet's always-edit view and the Lectern/Notebook read
view — so that a Craft parent name, which already renders as a clickable link, actually opens its
page instead of silently doing nothing.

#### Scenario: Clicking a Craft name opens the output item's page in the read view

- **WHEN** a player clicks a Craft parent's name in the Lectern or Notebook read view
- **THEN** the game opens the Handbook page for the Craft's output item, rather than doing nothing
