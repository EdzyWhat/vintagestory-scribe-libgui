## Why

Tablet styling is chosen by four parallel switches on the raw `material` string: `TabletReadability.For`
(ink, glow), `ScribeTheme.ClayColorsFor` (palette), `ScribeBackdrops.ForTablet` (backdrop) and
`ScribeTheme.ForTabletQuestLink`, plus a `material == "wax"` branch in `TabletHost.GetLayout`. They disagree on the
fallback for an unrecognized material (ink, glow and palette fall back to fire; the backdrop falls back to red), so
adding a material means touching five places and remembering all of them.

## What Changes

- Add `TabletStyle.For(material, state)`, one facade that resolves a material string once and returns the
  readability bundle, clay palette, backdrop spec, quest-link accent and layout proportions for it.
- Route `GuiDialogScribeTablet`, `ItemScribeTablet`, `CuneiformGlowTable` and `TabletHost` through the facade;
  the existing per-concern tables stay as the data, the switches on the string go away.
- Unknown materials fall back to red everywhere (previously fire for ink, glow and palette). Unknown materials
  cannot be produced by the shipped item variants, so no player-visible change is expected.
- Add a matrix test first that pins every shipped `(material, state)` pair, run before and after the routing
  change, plus the unknown-material fallback.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. No player-visible behavior changes for shipped materials (`skip_specs: true`).

## Impact

`src/Mod/TabletReadability.cs`, `ScribeTheme.cs`, `ScribeBackdrop.cs`, `CuneiformGlow.cs`, `TabletHost.cs`,
`GuiDialogScribeTablet.cs`, `ItemScribeTablet.cs`, a new `TabletStyle.cs`, and a new Atlas matrix scenario.
Needs a playtest of the 10 tablet variants (3 clays x wet/hard/fired, plus wax).
