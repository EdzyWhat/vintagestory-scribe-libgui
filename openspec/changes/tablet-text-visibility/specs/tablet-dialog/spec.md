## ADDED Requirements

### Requirement: Tablet Link/Tracker/Craft rows use a distinct per-material link ink

On the Pixel-Art path, the tablet dialog SHALL supply a dedicated per-material **link ink** for the
tappable content of a Link/Tracker/Craft row (the item-name hyperlink, the guide-page book glyph, and
the Tracker have/need count) via `ScribeRowStyle.LinkColor`, rather than letting the row fall through to
the theme accent (`colors.Primary`). Each material's link ink SHALL be a deeper, more-saturated tone
than that palette's accent, chosen to clear WCAG AA (≥ 4.5 : 1) against that material's clay/wax face
while remaining chromatically distinct from the near-black body ink, so a link reads as a legible,
tappable colored link and not as body text.

The link ink SHALL be keyed off the same `material` variable the theme and backdrop use (one
parameterized dialog, not a subclass per material). With Pixel-Art Display OFF, the tablet follows the
global theme over a flat panel and MAY use the theme accent for links unchanged.

#### Scenario: A tablet link reads as a distinct legible link

- **WHEN** a Link/Tracker/Craft row is shown on a tablet with Pixel-Art Display ON
- **THEN** the item name renders in the material's dedicated link ink, clearly distinct from both the
  clay backdrop and the near-black body ink

#### Scenario: Link ink is material-keyed

- **WHEN** the same row is shown on a fire vs. red vs. blue vs. wax tablet
- **THEN** each uses the link ink authored for that material (a deep rust / wine / steel-blue /
  amber-bronze respectively), all clearing AA on their own backdrop

#### Scenario: Flat-panel fallback is unchanged

- **WHEN** the tablet is shown with Pixel-Art Display OFF
- **THEN** the row link color follows the global theme accent exactly as before (no material link ink is
  applied)
