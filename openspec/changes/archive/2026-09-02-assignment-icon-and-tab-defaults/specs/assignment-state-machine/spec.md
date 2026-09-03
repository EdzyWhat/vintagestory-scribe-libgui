## ADDED Requirements

### Requirement: The accepted-assignment marker renders after the row checkbox and discloses its provenance on hover
On every surface that renders an accepted assignment's task row with the assignment marker icon
(Read view, Editor view, Pin Tab), the marker SHALL render immediately after the row's completion
checkbox (i.e. to the checkbox's right, not before it), and hovering it SHALL show a two-line
tooltip: the assigner's display name and the date the assignment was sent, and the date it was
accepted.

#### Scenario: Marker position
- **WHEN** a task row is an accepted assignment on the Read view, Editor view, or Pin Tab
- **THEN** the marker icon renders after the row's checkbox in reading order, not before it

#### Scenario: Marker tooltip content
- **WHEN** a player hovers the marker icon on an accepted assignment's row
- **THEN** a tooltip appears showing the assigner's display name and the assignment's sent date on
  its first line, and the date it was accepted on its second line

#### Scenario: Tooltip matches ambient illumination
- **WHEN** the marker tooltip is shown under a low-light ambient shade
- **THEN** the tooltip's bubble and text are shaded the same way every other row/nav tooltip in the
  dialog already is, rather than rendering full-bright
