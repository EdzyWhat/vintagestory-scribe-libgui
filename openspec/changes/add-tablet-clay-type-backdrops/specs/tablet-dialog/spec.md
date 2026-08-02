## MODIFIED Requirements

<!-- NOTE: This modifies the requirement introduced by change `add-tablet-dialog` (Proposal C).
     Proposal C is not yet archived, so its ADDED requirement lives under that change's dir, not
     openspec/specs/tablet-dialog/. This delta is authored against C's exact header text and MUST be
     applied only AFTER Proposal C archives (so the target requirement exists in openspec/specs/). -->

### Requirement: Tablet dialog uses its own theme and a placeholder backdrop

The tablet dialog SHALL select an earthen/clay `ScribeTheme.Tablet` palette in its `Build()` theme
wrapper. It SHALL declare **seven** named backdrop slots in `ScribeBackdrops` — six clay backdrops
(the three Vintage Story clay types red, blue, and fire crossed with the two fired-states soft and
fired) and one wax backdrop — and SHALL select exactly one of them from the tablet stack's `material`,
`clayType`, and `fired` state. When `material` is wax the wax backdrop SHALL be selected; otherwise the
backdrop SHALL be chosen from the stack's `clayType` (red / blue / fire) and `fired` (soft / fired)
appearance values. When those attributes are absent the selection SHALL default to red + soft so every
tablet resolves to a valid backdrop. The six clay backdrops SHALL be sourced from verified vanilla
Vintage Story pottery textures (the unfired per-type clay swatches and the fired ceramic swatch), and
the wax backdrop MAY use a placeholder swatch until bespoke wax art exists. The backdrop SHALL be
applied through the `gui-backdrop` mechanism, rendered so a small material swatch appears crisp rather
than a blurrily upscaled full-page stretch.

#### Scenario: A soft clay tablet opens with its clay-type backdrop

- **WHEN** a player opens an unfired clay tablet whose stack records `clayType = blue`
- **THEN** the dialog is drawn with the earthen `Tablet` theme and the blue-soft clay backdrop, sourced
  from the vanilla blue-clay swatch and rendered crisply (not a blurry full-page stretch)

#### Scenario: Each of the three clay types selects a distinct backdrop

- **WHEN** a player opens clay tablets recording `clayType` of red, blue, and fire in turn
- **THEN** each opens with a backdrop distinct to its clay type, so the three clay types are visually
  distinguishable

#### Scenario: Fired and unfired clay select different backdrops

- **WHEN** a player opens a clay tablet with `fired = false` and one with `fired = true` of the same
  clay type
- **THEN** the unfired tablet shows a soft-clay backdrop and the fired tablet shows a fired-ceramic
  backdrop

#### Scenario: A wax tablet opens with the wax backdrop

- **WHEN** a player opens a tablet whose `material` is wax
- **THEN** the dialog shows the single wax backdrop (a placeholder swatch this round), not any clay
  backdrop

#### Scenario: A tablet with no recorded clay-type falls back to a default backdrop

- **WHEN** a player opens a clay tablet whose stack carries no `clayType` or `fired` attribute (e.g. a
  creative-inventory or legacy stack)
- **THEN** the dialog selects the red + soft clay backdrop as the default and does not fail
