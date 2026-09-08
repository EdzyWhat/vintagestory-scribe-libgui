## ADDED Requirements

### Requirement: A Quest Link row renders a distinct marker icon, no completion checkbox, and a distinct color
On every surface that renders a Link row (Read view, Editor view, Pinned view, HUD pins, and the
Assignment-stage row), a Link block whose `LinkTarget` identifies a quest SHALL render differently
from a plain (non-quest) Link row in three ways:
- It SHALL show a dedicated quest-marker icon (an exclamation mark inside a circle) in place of
  the generic itemless glyph a guide-page Link uses, and SHALL NOT additionally render that icon a
  second time inline alongside the item name.
- On a surface whose Link row shows a completion checkbox (Read view, Editor view, Pinned view, HUD
  pins), it SHALL NOT render that checkbox; the quest-marker icon SHALL occupy the row's leading
  slot the checkbox would otherwise occupy instead. On the Assignment-stage row, whose leading-slot
  checkbox is a row-**selection** control (not a completion toggle — it determines which rows are
  included in the assignment being created), that selection checkbox SHALL be unaffected; the
  quest-marker icon there SHALL replace only the Link's inline icon, not the selection checkbox.
- It SHALL render in a color distinct from a plain Link's color on that surface.

Suppressing a completion checkbox is a presentation-only change: the underlying block SHALL remain
completable exactly as before (it keeps contributing to a document's task totals and keeps a
`Done` value), so no other behavior that depends on completability changes.

#### Scenario: A Quest Link row shows the quest-marker icon, not the book glyph
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on any surface
- **THEN** the row shows the quest-marker (exclamation-in-a-circle) icon exactly once, not the
  `scribebook` glyph a guide-page Link shows

#### Scenario: A Quest Link row shows no completion checkbox
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on the Read view, Editor
  view, Pinned view, or HUD pins
- **THEN** no completion checkbox is shown for that row, and the quest-marker icon appears in the
  row's leading slot instead

#### Scenario: A Quest Link row on the Assignment-stage row keeps its selection checkbox
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on the Assignment-stage row
- **THEN** its row-selection checkbox is still shown and still selects/deselects the row for the
  assignment being created, and the quest-marker icon replaces only its inline Link icon

#### Scenario: A plain Link row is unaffected
- **WHEN** a Link row whose `LinkTarget` does NOT identify a quest (a guide-page or item Link) is
  rendered on any surface
- **THEN** it still shows its completion (or, on the Assignment-stage row, selection) checkbox and
  its icon in its current inline position, unchanged

#### Scenario: A Quest Link row renders in a distinct color from a plain Link
- **WHEN** a Quest Link row and a plain Link row are both visible on the same surface/theme
- **THEN** the Quest Link row's icon and name render in a color distinct from the plain Link row's
  color

#### Scenario: Suppressing the checkbox does not change completion semantics
- **WHEN** a Quest Link block's document computes its task-completion count or exports via the TSV
  codec
- **THEN** the Quest Link block is still counted as completable and its `Done` value is still
  reported, exactly as it was before the checkbox was suppressed from rendering
