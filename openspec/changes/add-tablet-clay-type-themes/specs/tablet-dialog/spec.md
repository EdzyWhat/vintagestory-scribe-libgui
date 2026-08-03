## MODIFIED Requirements

### Requirement: Tablet dialog uses its own theme and material-keyed backdrops

The tablet dialog SHALL select a clay-type-specific `ScribeTheme` palette in its `Build()` theme
wrapper, keyed to the tablet item's `material` variant, when Pixel-Art Display is ON. There SHALL be
three authored per-clay-type palettes (red, blue, fire) whose colors harmonize with each type's
backdrop art; `wax` and any unrecognized material SHALL resolve to the fire palette (its interim
backdrop twin), so the resolved theme and the resolved backdrop always agree. Each per-material palette
SHALL vary the roles that carry material identity — the ink (`OnSurface`/`OnBackground`), the accent
(`Primary`, which programmatically drives button fill, button text, hover, press, caret, focused-input
border, and text selection), the secondary tone (`Secondary`, which drives the pinned-row tint), the
input field background (`SurfaceHigh`), the input/divider border (`Border`), and the panel
`Background`. Within each palette, `Secondary` SHALL read clearly distinct from `Primary` so a focused
input inside a pinned row shows a legible focus border against the pinned wash.

When Pixel-Art Display is OFF, the tablet dialog SHALL follow the player's global theme
(`ThemeData.Default`), unchanged — per-clay theming and backdrop art both apply only when Pixel-Art is
ON. The backdrop SHALL continue to be applied through the existing `WrapBackdrop` / `BuildOuterArtBox`
mechanism, and backdrop selection SHALL remain keyed to the `material` variant as before.

#### Scenario: Tablet opens with its clay-type theme

- **WHEN** a player opens a red, blue, or fire clay tablet with Pixel-Art Display ON
- **THEN** the dialog is drawn with that clay type's palette (its own ink, accent, input
  background/border, and panel background) and the backdrop slot for that material

#### Scenario: Wax and unknown materials fall back to the fire palette

- **WHEN** the tablet dialog resolves the theme for a `wax` tablet or an unrecognized material with
  Pixel-Art Display ON
- **THEN** it resolves to the fire clay palette, matching the fire interim backdrop that material uses

#### Scenario: Pixel-Art off follows the global theme

- **WHEN** a tablet of any material is opened with Pixel-Art Display OFF
- **THEN** the dialog follows the player's global theme (`ThemeData.Default`) with no per-clay coloring
  and no backdrop art, exactly as before this change

#### Scenario: Focus cue stays distinct on a pinned row

- **WHEN** a player focuses an input field inside a pinned task row on a tablet
- **THEN** the focused-input border (from `Primary`) reads clearly against the pinned-row wash (from
  `Secondary`), so the focus is unambiguous

#### Scenario: Non-tablet dialogs and readable path are unaffected except the pinned tint

- **WHEN** the Lectern or Notebook dialog is opened, or any dialog is rendered on the non-cuneiform
  readable path
- **THEN** its theme is unchanged from before this change (the parchment `Light`/global theme), EXCEPT
  the pinned-row tint, which is now derived from `Secondary` instead of `Primary` (the same global
  remap applied for focus clarity)
