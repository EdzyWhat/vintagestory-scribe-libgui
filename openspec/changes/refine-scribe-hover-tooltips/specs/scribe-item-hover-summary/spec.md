## ADDED Requirements

### Requirement: Item-slot hover shows a compact Scribe document summary

Hovering an item stack in a Scriptorium inventory slot SHALL show a compact Scribe-specific summary
card instead of the stock full-size item tooltip. The card SHALL present:
- the item/block display name,
- the document **Title** the stack carries (or an untitled placeholder when the document has no
  custom title),
- a per-type count of the document's contents grouped by kind (tasks, notes/text sections, trackers,
  links), omitting or zeroing types that are absent.

The card SHALL be materially smaller than the stock LibGUI item tooltip (which is fixed at 350px wide
with full name + description + durability + quantity). The card is presentation only and SHALL NOT
alter the stack, the document, or any inventory state.

#### Scenario: Hover an opened document item

- **WHEN** the player hovers a Scribe item stack that carries a document with, say, a title and a
  mix of tasks and trackers
- **THEN** a compact card shows the item name, the document title, and the per-type counts (e.g.
  tasks and trackers), sized notably smaller than the stock item tooltip

#### Scenario: Counts reflect the document's block kinds

- **WHEN** the hovered document contains blocks of more than one kind
- **THEN** the card reports a separate count per kind present, derived from the document's blocks —
  not a single undifferentiated total

### Requirement: Item-slot hover distinguishes a crafted-but-never-opened item

When a hovered Scribe item stack carries **no** document (a freshly crafted item that has never been
opened/initialized), the card SHALL clearly indicate that empty/never-opened state rather than
showing a title placeholder and all-zero counts as if it were an opened-but-empty document.

#### Scenario: Hover a fresh crafted item

- **WHEN** the player hovers a Scribe item that was crafted but never opened (its stack carries no
  stored document)
- **THEN** the card shows the item name and an explicit "never opened" indication, distinct from the
  presentation of an opened document

### Requirement: Custom slot preserves standard item interaction

Replacing the stock slot widget with the custom summary-card slot SHALL NOT regress item interaction:
the standard click-to-grab / click-to-place model (left-click picks up or places against the
mouse-held stack, right-click places-one / splits, wheel transfers) SHALL continue to work through the
existing server-authoritative slot path, and the Scribe-items-only accept filter SHALL still be
enforced. (Click-hold-drag distribution is not an inventory mechanic and is out of scope.) Only the
hover presentation changes.

#### Scenario: Moves still work through the custom slot

- **WHEN** the player left-clicks, right-clicks, or wheel-scrolls the Scriptorium inventory slots
- **THEN** the item is grabbed/placed/split exactly as before (server-authoritative), with the
  Scribe-only accept rule still rejecting non-Scribe items — the custom slot only changes the hover

### Requirement: Item-hover card is shaded like other Scribe hovers

The item-hover summary card SHALL be shaded by the illumination pass at the same reduced hover
strength as the other Scribe-owned hover surfaces, so it does not render at full brightness above a
dimmed inventory tab.

#### Scenario: Item card dims with the inventory tab

- **WHEN** the player hovers an item slot while the Scriptorium dialog body is shaded by low light
- **THEN** the summary card is shaded to match (at reduced hover strength), consistent with the
  nav-button and other Scribe tooltips
