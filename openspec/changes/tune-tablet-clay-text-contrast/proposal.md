## Why

An in-game screenshot of a fired **red** clay tablet shows the central **placeholder** text ("empty
task list" hint) is barely legible against the mid-tone clay backdrop. The three clay-type palettes
(`TabletFire` / `TabletRed` / `TabletBlue`, added by `add-tablet-clay-type-themes`) each **hand-author**
their muted-text role (`OnSurfaceVariant`) as an independent `Vector4`, and the multiline field draws
the placeholder from that role at a further **0.55 alpha** (`ScribeMultilineField.cs`). So the muted
text is both a per-hue guess and blended ~45% into the backdrop — the two things that make a "consistent
amount of darkening across three themes" impossible to achieve by nudging RGB triples by eye.

We want the muted/placeholder text darkened by a **consistent perceptual amount across all three clay
palettes**, in a way that stays maintainable (one knob, not three) and keeps each clay's hue identity.

## What Changes

- **Derive the muted-text role (`OnSurfaceVariant`) from each palette's own `ink`** via a single shared
  HSV *Value* lift, instead of authoring three independent `Vector4`s. Reuse the existing
  `ScribeRowConstants.ShiftBrightness(color, deltaValue, saturationScale)` helper (HSV Value shift on
  Skia's 0–100 scale, hue + chroma + alpha preserved) so one constant governs the contrast step for
  fire, red, and blue at once. Darkening "a bit" = lowering that one constant; all three move in lockstep.
- **~~Raise the placeholder alpha floor~~ — dropped during implementation.** The plan was to raise
  `ScribeMultilineField`'s `0.55` placeholder alpha toward ~`0.70`, but the scope check found the tablet
  never uses that seam: it renders through the cuneiform path (no placeholder), and the faint screenshot
  hint is the empty-task-list hint drawn at FULL alpha from `OnSurfaceVariant`. The `0.55` seam serves
  only the readable Lectern/Notebook path this change leaves untouched, so raising it is forbidden here.
  D1's darker derived muted role is the whole fix. (See design D2.)
- **Scope is muted/hint/placeholder text only.** The body/title **`ink`** (`OnSurface`/`OnBackground`)
  is left unchanged — it is already near-black (0.12–0.24 per channel) and darkening it risks crushing
  it into the mid-tone clay. This is the explicitly chosen scope (option: *muted role + placeholder alpha*).
- **Export refresh:** re-export the three clay palettes to the sibling `libGUI-Theme-Library/` gallery
  `themes/*.json` (as `add-tablet-clay-type-themes` did) so the browsable palettes reflect the new muted
  role, then rebuild with `node build.mjs`.
- **Unchanged:** Pixel-Art-off still follows the global theme; backdrop art, cuneiform glow, accent/
  secondary/surfaces/borders, and the Lectern/Notebook parchment theme are all untouched. Core takes no
  color decisions (stays VS-API-free).

## Capabilities

### New Capabilities
<!-- None: this refines an existing requirement's color derivation, adds no new capability. -->

### Modified Capabilities
- `tablet-dialog`: Two refinements. (1) The "Tablet dialog uses its own theme and material-keyed
  backdrops" requirement is refined so the per-clay **muted-text role (`OnSurfaceVariant`)** is derived
  from that palette's `ink` by a single shared HSV lift (consistent contrast across all three clay types)
  rather than authored independently per palette. (2) A new requirement specifies that the tablet's
  muted/hint/**placeholder** text renders legibly on the clay backdrops (placeholder alpha floor raised).
  *(`tablet-dialog` is the correct home — `bundled-font-rendering` covers only font-face registration,
  not placeholder color/alpha.)*

## Impact

- **Code (Mod layer only):**
  - `src/Mod/ScribeTheme.cs` — `ClayPalette` derives `onSurfaceVariant` from `ink` via `ShiftBrightness`
    with one shared lift constant; the three `Tablet*` palettes drop their hand-authored
    `onSurfaceVariant` argument (or the parameter is removed from `ClayPalette` entirely).
  - `src/Mod/ScribeMultilineField.cs` — raise the placeholder alpha floor (`OnSurfaceVariant with { W = … }`).
  - Possibly `src/Mod/ScribeRowConstants.cs` — if the shared lift constant lives alongside `ShiftBrightness`.
- **Cross-project export:** `libGUI-Theme-Library/themes/*.json` regenerated for the three clay palettes.
- **No new dependencies, no asset changes, no Core changes, no persistence/settings changes.**
- **Regression surface:** only the muted/placeholder text color+alpha on the three clay tablet palettes.
  Body/title ink, accent-driven chrome, surfaces, borders, backdrops, cuneiform glow, and the entire
  readable (Pixel-Art-off) / Lectern / Notebook path are unchanged. The parchment `Light` theme's
  `OnSurfaceVariant` is NOT touched by this change (it is authored separately and is not a clay palette).
