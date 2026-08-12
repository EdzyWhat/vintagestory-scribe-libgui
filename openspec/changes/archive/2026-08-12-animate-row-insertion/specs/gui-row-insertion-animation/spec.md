## ADDED Requirements

### Requirement: New rows animate into an animating list

When a row is added to a surface backed by the shared row-animation container
(`ScribeAnimatedList`), the row SHALL enter with motion over a short duration rather than
appearing at full size in a single frame. The container SHALL detect a genuinely new row from the
list diff's appeared set (a live id present neither as a live row nor an animating ghost on the
previous frame), and the sibling rows SHALL reflow to accommodate the entering row's growing
footprint.

#### Scenario: A quick-added row grows into place
- **WHEN** a new task or note row is added to a surface that is not editing that row (e.g. a
  quick-add, or a row appearing on the Pin Tab or Read view)
- **THEN** the row enters with a height-grow slide (its rendered height animates from zero to full)
- **AND** the rows below it settle downward to make room over the same interval, rather than
  jumping to their final positions in one frame

#### Scenario: An existing row is unaffected by another row's entry
- **WHEN** a new row animates in
- **THEN** rows that were already present keep their content, focus, caret, scroll position, and
  hover state undisturbed for the duration of the entry animation

### Requirement: A freshly-created auto-focused editor row enters focus-safely

A row that is created already focused for immediate text entry (the new editor row a user types
into on add) SHALL NOT use a height-grow entry. It SHALL instead enter at full height with an
opacity fade from transparent to opaque, so the text caret is visible and correctly positioned and
pointer clicks land on the true row bounds from the first frame of the animation.

#### Scenario: Adding a task keeps the caret usable during entry
- **WHEN** the user adds a task and the new row is auto-focused for typing
- **THEN** the row is rendered at its full height for the entire entry animation
- **AND** the entry animation is an opacity fade, not a height grow
- **AND** typing and caret position work correctly from the first frame, and a click within the
  row lands on the intended target throughout the animation

#### Scenario: Entry mode is selected by focus, not by surface
- **WHEN** the container materializes an appeared row
- **THEN** it applies the opacity-fade entry if and only if that row is the auto-focused
  newly-created row, and applies the height-grow entry otherwise, regardless of which surface hosts
  the list

### Requirement: Entry animation is rebuild- and reconcile-stable

The entry animation SHALL play to completion exactly once per row insertion and SHALL NOT restart,
snap, or double-play when the host tree is rebuilt (`ForceRebuild`) or reconciled (`SetState`)
mid-animation. The animation SHALL be driven by a host-owned controller keyed by the row's stable
id (the same discipline the removal-collapse animation uses), so a remount mid-entry resumes the
in-flight animation rather than reseeding it.

#### Scenario: A rebuild during entry does not restart the animation
- **WHEN** a row is partway through its entry animation and the host performs a `ForceRebuild` or a
  reconciling `SetState`
- **THEN** the entry animation continues from its current progress to completion without snapping
  to full size or restarting from zero

#### Scenario: A completed entry leaves no residual animation state
- **WHEN** a row's entry animation finishes
- **THEN** the row renders at full size as an ordinary live row
- **AND** the entry controller for that id is released, so a later removal (or a future re-insertion
  of the same id) starts from a clean state
