## ADDED Requirements

### Requirement: Cuneiform item-kind titles wrap to width

Item-kind titles (Tracker, Link, and Craft rows) rendered on the cuneiform (Tablet) surface SHALL
wrap to the available row width rather than clipping mid-word, matching the wrapping behavior every
other surface (HUD, Lectern, Notebook, Scriptorium) already provides for the same titles. This applies
to both parent rows and their indented subtasks, and to both the read view and the wet-tablet editor
view (where the item name is display-only).

The single-line dialog title band (the title chrome) is out of scope and MUST remain single-line.

#### Scenario: A long Tracker/Link/Craft name on the Tablet wraps

- **WHEN** a Tracker, Link, or Craft row whose referenced item has a name longer than the row width is
  shown on the Tablet (read view or wet editor view)
- **THEN** the cuneiform name wraps onto additional lines within the row's bounds and no glyphs are
  clipped or run past the row edge

#### Scenario: A subtask item name on the Tablet wraps

- **WHEN** an indented (Depth 1) Tracker/Craft ingredient subtask with a long item name is shown on the
  Tablet, which has less horizontal room than a parent row
- **THEN** the cuneiform name wraps within the narrower indented bounds rather than clipping

#### Scenario: The dialog title band stays single-line

- **WHEN** the Tablet dialog renders its title chrome (which uses the same cuneiform renderer with
  single-line mode)
- **THEN** the title remains single-line and unaffected by this change

#### Scenario: Non-cuneiform surfaces are unchanged

- **WHEN** the same Tracker/Link/Craft titles are shown on the HUD, Lectern, Notebook, or Scriptorium
  (which render with a plain wrapping text style, not cuneiform)
- **THEN** their rendering is byte-for-byte unchanged by this change
