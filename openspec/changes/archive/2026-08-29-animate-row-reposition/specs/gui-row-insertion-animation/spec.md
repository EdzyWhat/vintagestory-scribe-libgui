## MODIFIED Requirements

### Requirement: New rows animate into an animating list

When a row is added to a surface backed by the shared row-animation container
(`ScribeAnimatedList`), the row SHALL enter with motion over a short duration rather than
appearing at full size in a single frame. The container SHALL detect a genuinely new row from the
list diff's appeared set (a live id present neither as a live row nor an animating ghost on the
previous frame), and the sibling rows SHALL reflow to accommodate the entering row via the shared
reposition animation (`animated-task-list`), rather than jumping to their final positions in one
frame. This holds regardless of which edge the row is inserted at — a row inserted at the Top of the
list per the player's New Task Insert / Pin Insert setting reflows the rows below it exactly like a
row inserted at the Bottom.

#### Scenario: A quick-added row grows into place
- **WHEN** a new task or note row is added to a surface that is not editing that row (e.g. a
  quick-add, or a row appearing on the Pin Tab or Read view)
- **THEN** the row enters with a height-grow slide (its rendered height animates from zero to full)
- **AND** the rows below it settle downward to make room over the same interval, rather than
  jumping to their final positions in one frame

#### Scenario: A row inserted at the Top reflows existing rows the same as a Bottom insert
- **WHEN** a new row is inserted at the Top of the list (New Task Insert or Pin Insert set to Top)
- **THEN** every existing row below shifts downward via the shared reposition animation instead of
  jumping instantly, exactly as it would for a Bottom insert

#### Scenario: An existing row is unaffected by another row's entry
- **WHEN** a new row animates in
- **THEN** rows that were already present keep their content, focus, caret, scroll position, and
  hover state undisturbed for the duration of the entry animation
