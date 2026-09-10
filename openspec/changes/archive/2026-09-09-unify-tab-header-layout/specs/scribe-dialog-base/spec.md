## ADDED Requirements

### Requirement: Non-tablet dialog tabs share a three-row header above one durable divider

Every dialog view/tab built on `ScribeDialogBase` (excluding the Tablet host, which renders no
nav column) SHALL render its header as up to three stacked rows above exactly one durable
divider that separates the header from the tab's scrollable/general content:

- **Row 1** — the existing title bar: a leading drag-handle grip, then the title text, then the
  edit/close actions. Its padding SHALL be tightened relative to its pre-unification size, to
  help offset the height added by Row 2.
- **Row 2** — a persistent subtitle line naming the active tab: a small-caps label followed by an
  italic descriptor (e.g. "GUEST BOOK: who has visited"). Every tab covered by this requirement
  SHALL render this row; none may omit it.
- **Row 3** — the tab's own existing controls, if it has any (filter pills, the completion-policy
  picker, column-header labels, or drafting/form controls). A tab with no such controls has no
  Row 3, and the durable divider follows directly after Row 2.

The durable divider SHALL sit 8 layout units below the end of the header block (Row 3, or Row 2
when Row 3 is absent), and SHALL sit flush against the scrollable/general content's viewport
below it, with no additional gap outside that viewport. The tab's own scrollable content SHALL
instead carry 4 layout units of top padding INSIDE its scroll region (so that padding scrolls
away with the content rather than remaining as fixed chrome between the divider and the
viewport). No tab covered by this requirement SHALL use a uniform equal-spacing gap (e.g.
applying the same spacing value between every header element and the divider) in place of this
asymmetric 8-above-divider/4-inside-scroll placement.

#### Scenario: A tab with row-3 content shows its subtitle above its controls

- **WHEN** a player opens the Read view (or any tab with filter pills, a policy picker, or
  column-header labels)
- **THEN** the subtitle row appears directly below the title bar, the tab's existing controls
  appear below the subtitle, and exactly one divider separates that block from the scrollable
  content — with no extra divider anywhere else in the header

#### Scenario: A tab with no row-3 content still gets a subtitle

- **WHEN** a player opens the Editor view or the Notebook History tab (tabs with no header
  controls of their own)
- **THEN** the subtitle row still appears below the title bar, and the durable divider follows
  directly after it

#### Scenario: Two visually similar tabs are distinguishable by subtitle

- **WHEN** a player switches between the Assignment Inbox tab and the Sent Assignment History tab
- **THEN** each shows a different subtitle (e.g. "INBOX: assignments sent to you" vs.
  "SENT HISTORY: assignments you've sent") even though their filter-pill row and list layout are
  otherwise identical

#### Scenario: The divider sits flush against the scroll viewport at rest

- **WHEN** a player opens any tab covered by this requirement at its default scroll position
- **THEN** the durable divider's bottom edge sits immediately against the top of the scrollable
  content's viewport, with no fixed gap between them

#### Scenario: The drag-grip sits leading, left of the title text

- **WHEN** a player looks at any dialog's title bar
- **THEN** the drag-grip glyph appears to the left of the title text, not among the trailing
  edit/close actions on the right, and remains draggable and tooltip-discoverable exactly as before

#### Scenario: The top padding scrolls away with the content

- **WHEN** a player scrolls a tab's content down from the top
- **THEN** the 4-unit top padding (read as breathing room above the first row at rest) scrolls out
  of view along with the rest of the content, rather than remaining as permanent chrome under the
  divider

### Requirement: Guest Book keeps an extra divider around its column-header row

The Guest Book tab SHALL be the one exception to the single-divider rule in the previous
requirement: it SHALL render an additional divider directly above its "Visitor / Note"
column-header row (its Row 3), immediately below the subtitle (Row 2), in addition to the
durable divider that still follows the column-header row. No other tab SHALL render this extra
divider.

#### Scenario: Guest Book shows two dividers; no other tab does

- **WHEN** a player opens the Guest Book tab
- **THEN** a divider appears directly above the "Visitor / Note" column headers, and a second,
  durable divider appears below them, before the scrollable list of visitor entries

#### Scenario: Every other tab shows only the one durable divider

- **WHEN** a player opens any tab covered by the previous requirement other than Guest Book
- **THEN** it shows only the one durable divider, with no extra divider around its Row 3 content

### Requirement: Form-shaped tabs treat their entire header content as Row 3

The Create Assignments tab and the Scriptorium's Transcribe tab, whose content is a fixed
drafting form or a set of non-scrollable zones rather than a scrollable list with simple header
controls, SHALL treat that entire form/zone content as Row 3 for the purposes of the
single-divider rule: the durable divider SHALL appear once, after all of that content,
separating it from whatever scrollable/general content follows (the stage tray, on Create
Assignments). An existing divider used to separate sibling zones within that content (for
example, Transcribe's Copy/Seal zone versus its Import/Export zone) MAY remain, but SHALL be
understood as an internal content separator, not the header-closing divider.

#### Scenario: Create Assignments gains its first divider

- **WHEN** a player opens the Create Assignments tab
- **THEN** a subtitle appears below the title bar, the drafting form (heading, staging, delivery,
  and send-to controls) appears below it, and a single divider separates all of that from the
  stage tray below

#### Scenario: Transcribe's zone divider is retained as an internal separator

- **WHEN** a player opens the Scriptorium's Transcribe tab
- **THEN** the durable divider appears once, directly after the subtitle row, and the existing
  divider between the Copy/Seal zone and the Import/Export zone remains, but is not the tab's
  header-closing divider
