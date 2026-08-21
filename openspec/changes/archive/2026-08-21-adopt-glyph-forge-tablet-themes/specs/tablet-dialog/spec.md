## MODIFIED Requirements

### Requirement: Tablet dialog uses its own theme and material-keyed backdrops

The tablet dialog SHALL select a clay-type-specific `ScribeTheme` palette in its `Build()` theme
wrapper, keyed to the tablet item's `material` variant, when Pixel-Art Display is ON. There SHALL be
three authored per-clay-type palettes (red, blue, fire) whose colors harmonize with each type's
backdrop art; `wax` and any unrecognized material SHALL resolve to the fire palette (its interim
backdrop twin), so the resolved theme and the resolved backdrop always agree.

The **body ink** (`OnSurface`/`OnBackground`) and the per-material **link ink** (the row style's
`LinkColor`) SHALL be resolved with the tablet's **drying state** as an additional input, sourced from
the readability bundle for the current `(material, state)` view, so ink can differ across wet, hard, and
fired (fired ink is darker). The remaining material-identity roles — the accent (`Primary`, which
programmatically drives button fill, button text, hover, press, caret, focused-input border, and text
selection), the secondary tone (`Secondary`, which drives the pinned-row tint), the input field
background (`SurfaceHigh`), the input/divider border (`Border`), and the panel `Background` — SHALL
remain per-material (state-independent). Within each palette, `Secondary` SHALL read clearly distinct
from `Primary` so a focused input inside a pinned row shows a legible focus border against the pinned
wash.

The per-material **muted-text role (`OnSurfaceVariant`)** — used for hint/placeholder and secondary
text — SHALL be **derived from that view's own ink** by a single shared HSV **Value** lift (via
`ScribeRowConstants.ShiftBrightness`, which preserves hue and chroma), governed by one shared constant
across all clay palettes, rather than authored as an independent per-palette color. This makes the
muted-vs-ink contrast a consistent perceptual step across fire, red, and blue, and makes "darken the
muted text" a single-constant adjustment. The derived muted tone SHALL remain clearly lighter/weaker
than the body `ink` so it still reads as secondary text, not body text.

When Pixel-Art Display is OFF, the tablet dialog SHALL follow the player's global theme
(`ThemeData.Default`), unchanged — per-clay theming and backdrop art both apply only when Pixel-Art is
ON. The backdrop SHALL continue to be applied through the existing `WrapBackdrop` / `BuildOuterArtBox`
mechanism, and backdrop selection SHALL remain keyed to the `material` variant as before (see "The
tablet dialog backdrop is chosen by clay type and state").

#### Scenario: Tablet opens with its own theme and backdrop

- **WHEN** a player opens a red, blue, or fire clay tablet with Pixel-Art Display ON
- **THEN** the dialog is drawn with that clay type's palette (its own ink, accent, input
  background/border, and panel background) and the backdrop slot for that material

#### Scenario: Ink and link ink vary by drying state

- **WHEN** a wet, a hardened, and a fired tablet of the same clay type are each opened with Pixel-Art
  Display ON
- **THEN** the body ink (`OnSurface`) and the link ink resolve to that clay's authored values for each
  state (the fired view's ink is darker than the wet view's), while the accent (`Primary`), secondary,
  surfaces, border, and background stay the same across the three states

#### Scenario: Muted text contrast is consistent across clay types

- **WHEN** a red, blue, and fire tablet are each opened with Pixel-Art Display ON
- **THEN** each palette's `OnSurfaceVariant` is the palette's own `ink` lifted by the same shared HSV
  Value amount, so the muted-vs-ink contrast step is perceptually equal across all three clay types
- **AND** each derived muted tone stays recognizably that clay's hue and reads as secondary (weaker
  than the body ink), not as body text

#### Scenario: Muted text darkens via a single constant

- **WHEN** the shared muted-text lift constant is lowered
- **THEN** the muted/placeholder text on all three clay palettes darkens by the same perceptual amount,
  with no per-palette color edits required

#### Scenario: Wax and unknown materials fall back to the fire palette

- **WHEN** the tablet dialog resolves the theme for a `wax` tablet or an unrecognized material with
  Pixel-Art Display ON
- **THEN** it resolves to the fire clay palette for the matching state, matching the fire interim
  backdrop that material uses

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
