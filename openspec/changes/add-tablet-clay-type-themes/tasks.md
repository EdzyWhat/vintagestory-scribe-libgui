## 1. Per-clay-type palettes (ScribeTheme)

- [x] 1.1 In `src/Mod/ScribeTheme.cs`, add three `ThemeData` palettes — `TabletRed`, `TabletBlue`,
  `TabletFire` — authored role-by-role from the same rules as the existing `Tablet`/`Light` (including
  the two semantic hover/select inversions). Seed the per-material roles from the sampled backdrop
  colors (red `#aa6f6d`, blue `#98a6af`, fire `#ccaf89`): vary `OnSurface`/`OnBackground` (ink),
  `Primary` (accent), `OnPrimary` (button text), `Secondary` (pinned tint — author it clearly distinct
  from that palette's `Primary`, see task 2.3), `SurfaceHigh` (input background), `Border`
  (input/divider), and `Background` (panel). Keep material-neutral roles (error, state-overlay alphas)
  at shared clay values.
- [x] 1.2 Replace `ForTablet(bool pixelArt)` with `ForTablet(string? material, bool pixelArt)`:
  Pixel-Art ON → the per-material palette (`clay-red`→red, `clay-blue`→blue, `clay-fire`→fire,
  `wax`/unknown→fire); Pixel-Art OFF → `ThemeData.Default` (unchanged off-path). Keep the fallback arm
  aligned with `ScribeBackdrops.ForTablet` so theme and backdrop agree. Retain the old single `Tablet`
  as the fire base (or fold it in) — do not leave a dead unused palette.
- [x] 1.3 Update the `ForTablet` doc-comment to describe the material-keyed selection and drop the
  "placeholder / deferred contrast decision" language (the decision is now made).

## 2. Thread material into theme selection (tablet dialog)

- [x] 2.1 In `src/Mod/GuiDialogScribeTablet.cs`, change `ResolveTheme(bool pixelArt)` to pass the
  tablet's `material` variant into `ScribeTheme.ForTablet(material, pixelArt)`. Source the material the
  same way backdrop selection does (the host/`Variant["material"]` seam) so a red/blue/fire tablet
  resolves its own palette.
- [x] 2.2 Confirm the cuneiform title ink (which reads `ResolveTheme(...).ColorScheme.OnSurface`
  directly) now picks up the per-material ink, and that all descendants recolor via
  `Theme.Of(context)` with no further wiring (buttons, inputs, pinned tint, caret, selection derive
  from the scheme automatically).
- [x] 2.3 Remap the pinned-row tint from `Primary` to `Secondary`: in `src/Mod/ScribeRowConstants.cs`,
  change `PinnedTint(colors)` to return `colors.Secondary with { W = PinnedTintAlpha }` and update its
  doc-comment. This is a shared helper (tablet + Lectern/Notebook + pinned HUD), so also verify the
  parchment `Light` theme's `Secondary` reads well as a low-alpha pinned wash and is distinct from its
  `Primary` (adjust `Light.Secondary` if needed). No call sites change (all four go through the helper).

## 3. Per-stroke outer glow — display title (CuneiformText)

- [x] 3.1 Add a per-material glow parameter table near `CuneiformMetrics` (halo color, blur sigma,
  light-vs-dark polarity), plus in-memory mutable tuning state the dev command (task 5) can override.
- [x] 3.2 Thread the resolved glow parameters (per material) into `CuneiformTextRender` /
  `CuneiformTextRenderWidget` alongside the existing ink/jitter props.
- [x] 3.3 In `CuneiformTextRender.PaintInternal` (`src/Mod/CuneiformText.cs`), split the stroke loop
  into two passes over the same reveal range: pass 1 draws every revealed stroke's fill in the halo
  color with `SharedPaint.MaskFilter = context.GetOrCreateBlurMask(sigma)`; pass 2 draws every stroke's
  crisp ink fill with `MaskFilter` nulled. Apply the same jitter transform in both passes so the halo
  tracks the drawn ink.
- [x] 3.4 Enforce shared-paint hygiene: null `MaskFilter` between the passes and before returning;
  restore/save any other mutated properties. While here, fix the pre-existing `IsAntialias`-not-
  restored minor leak.

## 4. Per-stroke outer glow — editable rows (ScribeCuneiformField)

- [x] 4.1 Mirror the two-pass glow in `ScribeCuneiformFieldRender.PaintInternal`
  (`src/Mod/ScribeCuneiformField.cs`) for every wrapped line, using the same shared glow parameter
  source as task 3 (single table, not a divergent copy). Caret/selection/hit-testing continue to read
  the un-jittered, un-glowed layout.
- [x] 4.2 Apply the same MaskFilter null/restore discipline (task 3.4) here.

## 5. Dev console command for live glow tuning

- [x] 5.1 Register a client-side dot-prefixed dev command (e.g. `.cuneiformglow`) that sets the
  in-memory glow tuning state (strength/blur, halo polarity) and forces a repaint of the open tablet
  (`ForceRebuild`/rebuild path), mirroring the `.cuneiform` harness. Mutates memory only; persists
  nothing.
- [ ] 5.2 Verify a value change takes effect on an open tablet without reopening, and that the command
  is client-registered (`.` prefix), not a server command.

## 6. Verification

- [x] 6.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean; `dotnet test tests/Core.Tests` green (no
  Core change expected, but keep it passing).
- [ ] 6.2 Restage (`bash build/restage.sh Debug`); in-game, open red/blue/fire tablets with Pixel-Art
  ON and confirm each reads as its own material (ink, buttons + hover/press, input background/border,
  pinned tint, panel background) and harmonizes with its backdrop.
- [ ] 6.3 In-game: confirm the glow lifts the ink off each clay backdrop and that overlapping strokes
  within a glyph show one uniform halo (no darkened/doubled seam).
- [ ] 6.4 In-game: use `.cuneiformglow` to tune per-material glow strength/blur/polarity; report values
  back, then bake the tuned constants into the table (task 3.1).
- [ ] 6.5 In-game: confirm Pixel-Art OFF still follows the global theme (no per-clay coloring, no
  backdrop), and that the Lectern/Notebook dialogs and the readable path are visually unchanged EXCEPT
  the intended pinned-tint shift to `Secondary`.
- [ ] 6.8 In-game: focus an input on a PINNED row (tablet, and Lectern) and confirm the focus border
  (`Primary`) is clearly distinguishable from the pinned-row wash (`Secondary`) — the ambiguity this
  remap fixes.
- [ ] 6.6 Confirm no rendering regression from the shared-paint filter: text/icons drawn after
  cuneiform in the same frame are not blurred.
- [ ] 6.7 Decide whether to keep or strip the `.cuneiformglow` dev command in the shipped tree; update
  accordingly.

## 7. Export palettes to the libGUI-Theme-Library gallery

- [x] 7.1 In the sibling repo `/Users/nick.edises/claude/libGUI-Theme-Library/`, add
  `themes/scribe_clay_red.json`, `themes/scribe_clay_blue.json`, `themes/scribe_clay_fire.json` — each
  a `{ "Theme": { …17 roles… } }` file with every `ColorScheme` role converted from its authored
  `Vector4` to `#RRGGBB[AA]` hex (`round(c × 255)` per channel; include the alpha byte for the
  translucent `Border`/`OutlineVariant`/`StateHover`/`StateSelected`). Do this AFTER the palettes are
  finalized in task 1 so the hex matches the shipped floats.
- [x] 7.2 If the pinned remap (task 2.3) changed `Light.Secondary`, re-export
  `themes/scribe_parchment.json` so the gallery stays consistent with the shipped parchment theme.
  (`Light.Secondary` was NOT changed by this change — it stays `#A3804C`, matching the committed
  `scribe_parchment.json`. The parchment file was untracked in the gallery repo, so it was added in
  the same commit for completeness, but its colors are unchanged.)
- [x] 7.3 Run `node build.mjs` in that repo to rebake `themes-data.js`, open `index.html`, and confirm
  the three clay palettes render side-by-side and read distinctly. Commit the JSONs + regenerated
  `themes-data.js` in that repo separately from the mod change. (Committed as `42f7fca` in the gallery
  repo; the visual side-by-side check in `index.html` is a manual step for the user.)
