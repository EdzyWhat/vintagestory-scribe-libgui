## ADDED Requirements

### Requirement: Right nav-column button placement is a group-scoped seam

The horizontal placement of the nav-button stack within its `SideColW` column SHALL be
resolved through a placement seam on `ScribeDialogBase` that maps the (already-computed) column
width and single nav-button box width to a cross-axis alignment, so a subclass can choose its
family's placement rule without forking `BuildRightColNav`. The seam SHALL default to the
**Pages group** behavior — left-aligned (`CrossAxisAlignment.Start`), the buttons hugging the
left edge of their column — which every dialog that does not override the seam SHALL use. The
button geometry, count, order, sizes, shadows, tooltips, and active-state coloring SHALL NOT be
affected by this seam.

#### Scenario: Pages-group dialogs left-align their nav buttons

- **WHEN** a Pages-group dialog (Lectern, Notebook, Scriptorium, or Clockmaker's Notebook) that
  does not override the placement seam renders its right nav column
- **THEN** the nav buttons are left-aligned (`Start`) against the inner edge of the `SideColW`
  column at every window size, restoring the pre-chalkboard layout tuned for their paper-margin
  art

#### Scenario: The seam does not alter button geometry

- **WHEN** the placement seam changes the chosen alignment
- **THEN** only the horizontal position of the stack within its column changes; the button box
  size, glyph size, spacing, shadows, count, and order are unchanged

### Requirement: Hard Border-group dialogs place nav buttons adaptively

A **Hard Border group** dialog that renders a nav column (the Chalkboard) SHALL override the
placement seam to an adaptive rule keyed on column width versus button width: when the
`SideColW` column is at least as wide as a nav button, the stack SHALL be centered
(`CrossAxisAlignment.Center`); when the column is narrower than a nav button, the stack SHALL be
aligned to the end (`CrossAxisAlignment.End`) so the button's right edge pins to the column's
outer edge and the overflow spills inward (LEFT) rather than off the window's right edge where
it would be clipped. The Tablet is also a Hard Border-group surface, but it renders no nav
column (its `BuildRightColNav` returns an empty box), so the placement seam does not apply to it
and it requires no override.

#### Scenario: Roomy column centers the stack

- **WHEN** a Chalkboard dialog renders at a `PixelArtSize` where the `SideColW` column is at
  least as wide as a nav button
- **THEN** the nav-button stack is horizontally centered within its column

#### Scenario: Narrow column pins to the outer edge and spills left

- **WHEN** a Chalkboard dialog renders at a small `PixelArtSize` where the `SideColW` column is
  narrower than a nav button (e.g. the button box exceeds the column width)
- **THEN** the stack aligns to the end so its right edge sits at the column's outer edge and the
  overflow spills inward toward the tasks column, and no button is clipped off the window's
  right edge

#### Scenario: The Tablet renders no nav column

- **WHEN** the Tablet dialog is built
- **THEN** its `BuildRightColNav` returns an empty column so no nav buttons render, and the
  placement seam has no effect on it despite its Hard Border-group classification
