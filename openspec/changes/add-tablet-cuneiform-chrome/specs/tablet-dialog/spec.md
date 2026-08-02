## REMOVED Requirements

### Requirement: A display-only cuneiform title banner renders above the task list

**Reason**: Superseded by cuneiform title-bar rendering. The 2026-08-02 playtest rejected the
display-only banner as a redundant duplicate of the title already shown in the title bar; this change
removes the banner and instead renders the existing (editable) title bar in cuneiform. See the
`tablet-cuneiform-chrome` capability.

**Migration**: No data migration — the banner was display-only chrome, not persisted state. The
document title is unchanged; it now renders in cuneiform through the title bar rather than a separate
banner. `BuildTitleBanner()` and the banner stack in `GuiDialogScribeTablet.BuildCentralRegion()` are
removed.

## MODIFIED Requirements

### Requirement: A single branch honors the disable-cuneiform setting

The tablet dialog SHALL compute a single `UseCuneiform` value from the player's `DisableCuneiformFont`
setting (via `ScribeTaskFont.UseCuneiform`). When cuneiform is enabled, this single branch SHALL route
the tablet's cuneiform surfaces — the editable title bar text, the editable task-row text (both at rest
and while being typed), and the tablet's button labels — to the cuneiform render path; when disabled,
the same surfaces SHALL render through the normal text path using `ScribeTaskFont.Resolve` in the
player's resolved task font (and rows/title use the normal editable field). The branch SHALL be
evaluated in one place and threaded to every surface, not scattered per widget.

#### Scenario: Fallback to normal font when cuneiform is disabled

- **WHEN** a player has `DisableCuneiformFont` enabled and opens a tablet
- **THEN** the title bar, task rows, and button labels all render and edit in the normal resolved task
  font instead of cuneiform strokes

#### Scenario: Cuneiform renders when the setting is off

- **WHEN** a player has `DisableCuneiformFont` disabled (the default) and opens a tablet
- **THEN** the title bar, task rows, and button labels all render as cuneiform strokes, and typing in
  the title or a row produces cuneiform
