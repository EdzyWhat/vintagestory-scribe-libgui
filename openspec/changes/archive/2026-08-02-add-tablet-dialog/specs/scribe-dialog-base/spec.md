## RENAMED Requirements

- FROM: `### Requirement: ScribeDialogBase exposes one virtual extension point for extra nav buttons`
- TO: `### Requirement: ScribeDialogBase exposes virtual extension points for subclass layout`

## MODIFIED Requirements

### Requirement: ScribeDialogBase exposes virtual extension points for subclass layout
The base class SHALL declare `protected virtual` extension points that let a subclass vary its layout
without altering the shared three-column layout skeleton (`[ SideColW spacer | TasksColW center |
SideColW right ]`) or the behavior of any dialog that does not override them:

- `GetExtraNavButtons()` returns an empty array by default. `BuildRightColNav` SHALL call it and
  append the returned buttons after the four baseline nav buttons (Read, Edit, Pinned, Settings).
- `BuildRightColNav()` SHALL be `protected virtual` so a subclass may replace the entire right column
  (for example, an empty, nav-less column whose `SideColW` width still preserves the side margin).
- `BuildCentralRegion()` SHALL be `protected virtual`, and `BuildEditorContent()` SHALL be
  `protected` (not `private`), so a subclass may supply its own center content while reusing the
  inherited editable task list rather than forking it.

The default bodies of these methods SHALL be the existing implementations, so a subclass that
overrides nothing behaves exactly as before.

#### Scenario: Lectern shows exactly four nav buttons
- **WHEN** the Lectern's subclass overrides none of these extension points
- **THEN** the right nav column contains exactly the Read, Edit, Pinned, and Settings buttons —
  no more, no fewer

#### Scenario: A subclass can add extra nav buttons
- **WHEN** a hypothetical Notebook subclass overrides `GetExtraNavButtons` to return a History button
- **THEN** the right nav column shows Read, Edit, Pinned, Settings, and then the History button

#### Scenario: A subclass can replace the right column and central content
- **WHEN** a subclass overrides `BuildRightColNav` to return an empty column and `BuildCentralRegion`
  to return its own single-view content (reusing the inherited `BuildEditorContent`)
- **THEN** the dialog renders no nav buttons and shows the subclass's own center layout, while the
  three-column skeleton and side margins are preserved

#### Scenario: Incumbent dialogs are unchanged
- **WHEN** the Lectern and both Notebook dialogs (which override none of these points) are built and
  opened after the extension points are added
- **THEN** their right column, central region, and all views (Read, Edit, Pinned, Settings, and
  History where present) build and behave exactly as before
