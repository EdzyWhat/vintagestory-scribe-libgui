## Why

The `add-chalkboard-block` work added an adaptive horizontal-placement rule to the shared
right nav column (`BuildRightColNav`): center the nav-button stack when the `SideColW` column
has slack, and right-align + spill-left when the column is narrower than a button. That fixed
the chalkboard, whose hard-bordered slate art wants the buttons centered and never clipped at
the frame edge. But it was applied **mod-wide**, and the original left-align (`Start`) layout
was in fact *deliberately* tuned for the paper-margin surfaces (Lectern, Notebook, Scriptorium,
Clockmaker's Notebook): their page art has a soft left gutter the buttons sit against. Centering
them there is a regression. Nav-button placement needs to differ by surface family, not be one
global rule.

## What Changes

- Introduce two surface families for nav-button horizontal placement, each with its own rule:
  - **Pages group** — the paper-margin surfaces (Lectern, Notebook, Scriptorium, Clockmaker's
    Notebook): keep the ORIGINAL behavior, `CrossAxisAlignment.Start` (buttons hug the left of
    their `SideColW` column). This restores the pre-chalkboard layout that was tuned for their
    art.
  - **Hard Border group** — the framed/bordered surfaces (Chalkboard and the Tablet): the
    adaptive rule — CENTER the stack when the column is at least as wide as a button, and align
    to the END (right edge pinned to the column's outer edge, overflow spilling LEFT) when the
    column is narrower than a button, so the buttons never clip off the window's right edge at
    small `PixelArtSize`. Note the Tablet renders NO nav column (`BuildRightColNav` returns an
    empty box), so its Hard Border membership is a classification of intent only — the placement
    seam never fires for it and it needs no code override.
- Add a placement seam on `ScribeDialogBase` (a `private protected virtual` method that maps the
  already-computed `SideColW` and nav-button box width to a `CrossAxisAlignment`), defaulting to
  the Pages behavior. Every Pages-group dialog inherits the default; the Chalkboard dialog
  overrides it to the adaptive Hard Border rule. This reverts the currently mod-wide centering
  to be group-scoped without changing the button geometry, count, order, or theming.
- No behavior change for the Pages group beyond restoring `Start`; no change to `SideColW`, the
  three-column skeleton, button sizes, shadows, tooltips, or active-state coloring.

## Capabilities

### New Capabilities
<!-- None: this refines an existing seam's behavior rather than introducing a new capability. -->

### Modified Capabilities
- `scribe-dialog-base`: The right nav column's horizontal button placement becomes a
  group-scoped seam — a Pages-group default (left-align/`Start`) that a Hard Border-group
  subclass (the Chalkboard) overrides with an adaptive center/end rule keyed on column width vs.
  button width. The Tablet is classified Hard Border for intent but renders no nav column, so
  the seam does not affect it.

## Impact

- `src/Mod/ScribeDialogBase.Layout.cs` — `BuildRightColNav`: replace the inline mod-wide
  `navAlign` computation with a call to the new placement seam (default returns `Start`).
- `src/Mod/GuiDialogScribeChalkboard.cs` — override the placement seam to return the adaptive
  center/end rule (the exact logic currently living inline in the base).
- `src/Mod/GuiDialogScribeTablet.cs` — no code change; classified Hard Border for intent but its
  `BuildRightColNav` already returns an empty column, so the seam never runs.
- No Core changes, no new dependencies, no persistence/asset changes. Purely a client-side
  layout seam refactor.
