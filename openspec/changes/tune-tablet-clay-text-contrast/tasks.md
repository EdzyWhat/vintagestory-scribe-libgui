## 1. Derive the muted-text role from ink (consistency mechanism)

- [x] 1.1 In `src/Mod/ScribeTheme.cs`, add a shared `MutedTextValueLift` constant (HSV Value points on
  Skia's 0–100 scale) governing the muted-vs-ink contrast for all clay palettes. Place it beside the
  clay palettes (or alongside `ShiftBrightness` in `ScribeRowConstants.cs` if that reads cleaner).
  *(Placed in `ScribeTheme.cs` beside the clay palettes, seeded at `14f`; the three previously
  hand-authored `onSurfaceVariant` colors all sat at exactly +20 above their ink, so this seeds darker.)*
- [x] 1.2 In `ClayPalette(...)`, compute `OnSurfaceVariant` as
  `ScribeRowConstants.ShiftBrightness(ink, +MutedTextValueLift)` instead of taking a hand-authored
  `onSurfaceVariant` argument. Remove the `onSurfaceVariant` parameter from `ClayPalette` and drop it
  from the three `TabletFire` / `TabletRed` / `TabletBlue` call sites.
- [x] 1.3 Confirm the parchment `Light` theme is untouched (it authors its own `OnSurfaceVariant` and is
  not built through `ClayPalette`). No change to accent/secondary/surfaces/borders on any palette.
- [x] 1.4 `dotnet build` clean; `dotnet test` (Core suite) still green — no Core changes expected.
  *(Build clean; 283/283 Core tests pass.)*

## 2. Raise the placeholder alpha floor (fixes the screenshot symptom)

- [x] 2.1 **DEVIATION — no code change made. Do the scope check (2.2) first.** The seam-scope check below
  found that the tablet never reaches the `0.55` placeholder alpha, so raising it would darken ONLY the
  readable path the proposal forbids touching. See 2.2.
- [x] 2.2 Verify the placeholder rendering seam's scope: if the same widget/placeholder path also serves
  the readable Lectern/Notebook (Pixel-Art-off) surfaces, scope or gate the raise to the tablet so the
  readable path's placeholder alpha is unchanged. Check the cuneiform title field / single-line fields
  for a parallel placeholder alpha and keep them consistent (or centralize).
  **FINDING:** `ScribeMultilineField.Build` picks between two render widgets on `Widget.UseCuneiform`
  (`ScribeMultilineField.cs:1124`). The `placeholderColor: colors.OnSurfaceVariant with { W = 0.55f }`
  seam is ONLY on the non-cuneiform `ScribeMultilineFieldRenderWidget` (line 1170) — the readable
  Lectern/Notebook path. The tablet uses the cuneiform `ScribeCuneiformFieldRenderWidget` (lines
  1124–1159), which has NO placeholder param at all; the empty task-list hint the screenshot shows is a
  separate widget rendered at FULL-alpha `OnSurfaceVariant` (`ScribeEditorContent.cs:288` /
  `ScribeReadContent.cs:100`). So the screenshot symptom is the full-alpha muted role, fixed by D1's
  darker derived `OnSurfaceVariant`. Raising line 1170's `0.55` would darken ONLY the readable path,
  which the proposal's "Readable path placeholder is unchanged" scenario explicitly forbids.
  **DECISION: skip the D2 alpha raise; leave `ScribeMultilineField.cs:1170` at `0.55`.** D1 alone fixes
  the tablet symptom. (design D2 revised to record this; the ADDED spec requirement's legibility intent
  is satisfied by D1, not by an alpha edit.)

## 3. Re-export the theme gallery

- [x] 3.1 Re-export the three clay palettes to `libGUI-Theme-Library/themes/*.json` (Vector4 roles →
  `#RRGGBB[AA]`), reflecting the new derived `OnSurfaceVariant`; rebuild with `node build.mjs`. (The
  gallery cannot show the placeholder alpha — only the muted role color updates there.)
  *(Updated `OnSurfaceVariant` at seed lift +14V: fire `#66472E`→`#572C16`, red `#704238`→`#612920`,
  blue `#4D5C66`→`#354657`; `node build.mjs` rebuilt themes-data.js (26 themes). NOTE: if 4.3 finalizes a
  lift other than 14, re-run this export.)

## 4. In-game tuning pass (settle the numbers)

- [ ] 4.1 With Pixel-Art Display ON, open a **red**, a **blue**, and a **fire** tablet with an empty task
  field. Confirm the placeholder + any hint text read legibly on all three backdrops (esp. blue, the one
  cool palette with a different ink hue). *(IN-GAME — restaged 2026-08-04; needs a game relaunch. TESTING.md
  `fa4e26e8`.)*
- [ ] 4.2 Confirm the muted text still reads as *secondary* (clearly weaker than body ink) on all three —
  it must not approach body-ink weight and collapse the hierarchy. *(IN-GAME — TESTING.md `ce460e26`.)*
- [ ] 4.3 Finalize `MutedTextValueLift` (seeded `14`); the placeholder alpha raise was dropped (see D2/2.2).
  Record the chosen lift in this change (design D3) before archiving. Re-run task 3 export if the muted
  role's final value differs from the seed. *(BLOCKED on the 4.1/4.2 in-game read.)*

## 5. Verification & docs

- [x] 5.1 Update `TESTING.md` with a playtest item: "empty red/blue/fire tablet placeholder is legible;
  muted text still reads as secondary; readable-path placeholder unchanged."
  *(Added the `tune-tablet-clay-text-contrast` section with items `fa4e26e8` (empty-tablet hint legible)
  and `ce460e26` (muted stays secondary + finalize the lift).)*
- [x] 5.2 Regression check: body/title ink, accent-driven chrome, surfaces, borders, backdrops, cuneiform
  glow, and the readable/Lectern/Notebook path are visually unchanged (only clay muted+placeholder moved).
  *(Code-level confirmed: only `OnSurfaceVariant` derivation changed in `ClayPalette`; ink/accent/
  secondary/surfaces/borders/error/state-overlays untouched; the `Light` parchment theme is not built
  through `ClayPalette` and is unchanged; the `0.55` placeholder seam (readable path) is untouched.
  Final visual regression sign-off folds into the 4.x in-game pass.)*
