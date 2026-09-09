## MODIFIED Requirements

### Requirement: Non-tablet dialog tabs share a three-row header above one durable divider

Every dialog view/tab built on `ScribeDialogBase` (excluding the Tablet host, which renders no
nav column) SHALL render its header as up to three stacked rows above exactly one durable
divider that separates the header from the tab's scrollable/general content:

- **Row 1** — the existing title bar: a leading drag-handle grip, then the title text, then the
  edit/close actions. Its padding SHALL be tightened relative to its pre-unification size, to
  help offset the height added by Row 2.
- **Row 2** — a persistent subtitle line naming the active tab: a small-caps label followed by an
  italic descriptor (e.g. "GUEST BOOK: who has visited"). Every tab covered by this requirement
  SHALL render this row UNLESS the client-local "show subtitle row" setting is off, in which case
  no tab covered by this requirement renders it.
- **Row 3** — the tab's own existing controls, if it has any (filter pills, the completion-policy
  picker, column-header labels, or drafting/form controls). A tab with no such controls has no
  Row 3, and the durable divider follows directly after Row 2 (or, when Row 2 is hidden, directly
  after Row 1).

The durable divider SHALL sit 8 layout units below the end of the header block (Row 3, Row 2, or
Row 1 when both Row 2 and Row 3 are absent), and SHALL sit flush against the scrollable/general
content's viewport below it, with no additional gap outside that viewport. The tab's own
scrollable content SHALL instead carry 4 layout units of top padding INSIDE its scroll region (so
that padding scrolls away with the content rather than remaining as fixed chrome between the
divider and the viewport). No tab covered by this requirement SHALL use a uniform equal-spacing
gap (e.g. applying the same spacing value between every header element and the divider) in place
of this asymmetric 8-above-divider/4-inside-scroll placement.

Whether Row 2 renders is controlled by a single client-local, boolean "show subtitle row" setting,
defaulting to on, read once when the client starts (see `visual-tuning-config`). Turning it off
hides Row 2 on every tab covered by this requirement uniformly; a tab cannot independently show or
hide its own Row 2.

#### Scenario: A tab with row-3 content shows its subtitle above its controls

- **WHEN** a player opens the Read view (or any tab with filter pills, a policy picker, or
  column-header labels) with the show-subtitle-row setting on (its default)
- **THEN** the subtitle row appears directly below the title bar, the tab's existing controls
  appear below the subtitle, and exactly one divider separates that block from the scrollable
  content — with no extra divider anywhere else in the header

#### Scenario: A tab with no row-3 content still gets a subtitle

- **WHEN** a player opens the Editor view or the Notebook History tab (tabs with no header
  controls of their own) with the show-subtitle-row setting on
- **THEN** the subtitle row still appears below the title bar, and the durable divider follows
  directly after it

#### Scenario: Two visually similar tabs are distinguishable by subtitle

- **WHEN** a player switches between the Assignment Inbox tab and the Sent Assignment History tab
  with the show-subtitle-row setting on
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

#### Scenario: Turning the setting off hides the subtitle on every tab

- **WHEN** the show-subtitle-row setting is off and a player opens any tab covered by this
  requirement
- **THEN** Row 2 does not render on that tab; Row 3 (if the tab has one) and the trailing durable
  divider still render, now sitting directly under Row 1 instead of under Row 2, at the same
  8-layout-unit offset from the end of whatever header content remains

#### Scenario: Turning the setting off does not remove Row 3 or the divider

- **WHEN** the show-subtitle-row setting is off and a player opens the Read view (a tab with Row 3
  content)
- **THEN** the filter-pill row still renders directly below the title bar, and the durable divider
  still separates it from the scrollable content
