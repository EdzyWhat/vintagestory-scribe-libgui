## MODIFIED Requirements

### Requirement: Item-slot hover shows a compact Scribe document summary

Hovering an item stack in a Scriptorium, Assignment Desk, or Inbox restricted inventory slot
SHALL show a compact Scribe-specific summary card instead of the stock full-size item tooltip.
For every Scribe item EXCEPT a sealed (assignment-carrying) Task Notice, the card SHALL present:
- the item/block display name,
- the document **Title** the stack carries (or an untitled placeholder when the document has no
  custom title),
- a per-type count of the document's contents grouped by kind (tasks, notes/text sections, trackers,
  links), omitting or zeroing types that are absent.

For a sealed Task Notice specifically (one carrying an assignment), the card SHALL instead show
the item/block display name followed by two lines: who assigned it and who it is addressed to —
reusing the same assigner/date and addressee formatting already used elsewhere for a Task Notice
(the held-item tooltip's "Addressed to" line and the Accept dialog's "Assigned by" line). A
Task Notice never carries a custom title (its document is blocks-only), so the generic Title
line and per-kind counts would be meaningless for it and SHALL NOT be shown. A **blank** Task
Notice (no assignment) is unaffected by this exception and follows the "never opened" path
below like any other Scribe item with no document.

The card SHALL be materially smaller than the stock LibGUI item tooltip (which is fixed at 350px
wide with full name + description + durability + quantity). The card is presentation only and
SHALL NOT alter the stack, the document, or any inventory state.

#### Scenario: Hover an opened document item

- **WHEN** the player hovers a Scribe item stack that carries a document with, say, a title and a
  mix of tasks and trackers
- **THEN** a compact card shows the item name, the document title, and the per-type counts (e.g.
  tasks and trackers), sized notably smaller than the stock item tooltip

#### Scenario: Counts reflect the document's block kinds

- **WHEN** the hovered document contains blocks of more than one kind
- **THEN** the card reports a separate count per kind present, derived from the document's blocks —
  not a single undifferentiated total

#### Scenario: Hovering a sealed Task Notice shows who assigned it and who it's addressed to

- **WHEN** the player hovers a sealed (assignment-carrying) Task Notice sitting in a Scriptorium,
  Assignment Desk, or Inbox restricted slot
- **THEN** the card shows the item name, a line naming who assigned it (with the assigned date),
  and a line naming who it is addressed to — with no Title or per-kind-count lines

#### Scenario: Hovering a blank Task Notice is unaffected

- **WHEN** the player hovers a blank (no assignment) Task Notice sitting in one of those
  restricted slots
- **THEN** the card shows the item name and the existing "never opened" indication, exactly as
  before this change

### Requirement: Item-slot hover distinguishes a crafted-but-never-opened item

When a hovered Scribe item stack carries **no** document (a freshly crafted item that has never
been opened/initialized, including a blank Task Notice), the card SHALL clearly indicate that
empty/never-opened state rather than showing a title placeholder and all-zero counts as if it
were an opened-but-empty document.

#### Scenario: Hover a fresh crafted item

- **WHEN** the player hovers a Scribe item that was crafted but never opened (its stack carries no
  stored document)
- **THEN** the card shows the item name and an explicit "never opened" indication, distinct from the
  presentation of an opened document
