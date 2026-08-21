## 0. Supersession ordering (do first)

- [x] 0.1 Archive order resolved + executed (2026-08-21). Archived `add-tablet-state-glow-modifier`
      normally (landed the state-aware glow: baseline `cuneiform-contrast-glow` now has "Hardened and
      fired tablets use a light halo" + "State changes the halo without changing the ink"). Archived
      `tablet-text-visibility` with `--skip-specs` because its OLDER glow delta would have REVERTED that
      state-aware model; manually landed its one still-current contribution — the ADDED tablet-dialog
      requirement "Tablet Link/Tracker/Craft rows use a distinct per-material link ink" — into the
      baseline. Both changes' remaining items were in-game tuning gates, annotated as retired/folded
      here (not passed) in their archived tasks.md. See `[[openspec-archive-order-header-drift]]`.

      **Archive-time reconciliation this change will need (do NOT try to make the delta a pre-archive
      superset — it can't be):** this change's D2 makes ink *state-varying*, which directly contradicts
      the baseline scenario "State changes the halo without changing the ink," and its per-`(material,
      state)` glow supersedes the baseline's "one shared light halo per state." The MODIFIED-scenario
      guard will therefore refuse a clean archive. When archiving THIS change, use `--skip-specs` + a
      manual sync of `cuneiform-contrast-glow` and `tablet-dialog` to this change's end-state (same move
      used here and at the v1.0.0 cut), reconciling the link-ink story into a single requirement.

## 1. The readability bundle (source of truth)

- [x] 1.1 Add a Mod-side readonly record `TabletReadability(Vector4 BodyInk, Vector4 LinkInk,
      float StrokeWeightScale, CuneiformGlow Glow)` — new file `src/Mod/TabletReadability.cs` (per D6
      it reads oddly as "theme" since it spans theme + glow + stroke). Doc-comment it as the single
      `(material, state)`-keyed source of truth, baked from the glyph-forge exports.
- [x] 1.2 Add `TabletReadability.For(string? material, TabletState state)` with the 10 baked bundles
      below (values are the glyph-forge exports; RGB 0–1, glow alpha = strength, blur/offset = fraction
      of em). `wax` resolves only wet; unrecognized material → fire bundle for the same state.

      | view | bodyInk (r,g,b) | linkInk (r,g,b) | strokeScale | glow rgb / α=strength / blur / offset |
      |---|---|---|---|---|
      | blue wet | 0.176,0.180,0.184 | 0.176,0.278,0.345 | 1.2 | 0.247,0.278,0.314 / 0.82 / 0.225 / 0 |
      | blue hard | 0.192,0.200,0.208 | 0.129,0.271,0.373 | 1.0 | 0.831,0.851,0.871 / 0.92 / 0.125 / 0.05 |
      | blue fired | 0.133,0.141,0.149 | 0.063,0.247,0.376 | 0.95 | 0.780,0.808,0.839 / 0.58 / 0.115 / 0.05 |
      | red wet | 0.149,0.133,0.129 | 0.439,0.200,0.200 | 1.2 | 0.373,0.243,0.208 / 0.86 / 0.265 / 0 |
      | red hard | 0.173,0.129,0.110 | 0.455,0.102,0.102 | 1.0 | 0.839,0.780,0.780 / 1.00 / 0.145 / 0.05 |
      | red fired | 0.192,0.125,0.086 | 0.376,0.063,0.063 | 0.95 | 0.839,0.780,0.780 / 0.44 / 0.110 / 0.04 |
      | fire wet | 0.176,0.145,0.082 | 0.471,0.282,0.071 | 1.2 | 0.294,0.243,0.165 / 0.92 / 0.235 / 0 |
      | fire hard | 0.173,0.129,0.110 | 0.455,0.275,0.102 | 1.0 | 0.839,0.816,0.780 / 1.00 / 0.145 / 0.05 |
      | fire fired | 0.133,0.067,0.027 | 0.318,0.200,0.024 | 0.95 | 0.804,0.725,0.659 / 0.70 / 0.110 / 0.04 |
      | wax wet | 0.408,0.322,0.231 | 0.604,0.455,0.137 | 1.0 | 0.965,0.949,0.898 / 0.28 / 0.215 / 0.01 |

      (values reflect the 2026-08-21 glyph-forge retune. offset X = offset Y in every export; wet + wax
      offsets are near-zero, hard/fired ~0.04–0.05.) Note in the comment that each cell is authored
      independently — the retune proved it: the three wet clays now carry their OWN tinted glows (no
      longer a shared dark halo) and the stroke scale now runs wet 1.2 / hard 1.0 / fired 0.95 (wet
      heaviest, fired lightest — inverted from the first bake), all without a code or structure change.
      Any cell may diverge in a future retune; do not simplify an apparent overlap into a shared field.

      **Second, partial retune (2026-08-21, ~12:15–12:18):** blue wet, blue hard, red wet, fire wet, and
      wax wet were re-exported again with the values now shown above (superseding the first bake for
      those 5 rows only); blue fired, red hard/fired, and fire hard/fired were NOT re-exported and still
      carry the first retune's values. Wax's ink/link held; only its glow strength (0.76→0.28) and blur
      (0.115→0.215) moved.

## 2. Glow: offset field + source from the bundle

- [x] 2.1 In `src/Mod/CuneiformGlow.cs`, extend the struct to
      `record struct CuneiformGlow(Vector4 Color, float BlurFraction, float OffsetXFraction = 0f,
      float OffsetYFraction = 0f)`. `Enabled` unchanged. Default offsets 0 keep every non-tablet caller
      (which never sets a glow) and centered halos backward-compatible.
- [x] 2.2 Repoint `CuneiformGlowTable.For(material, state)` to return `TabletReadability.For(material,
      state).Glow`. Delete the now-dead `HardHalo`/`FiredHalo`/`FireDefault`/`RedDefault`/`BlueDefault`/
      `WaxDefault` seeds and `ForWetMaterial` (their values now live in the bundle table). Keep the
      class + `For` signature so the three call sites are untouched.
- [x] 2.3 Thread the offset into the Skia paint: in the cuneiform render objects
      (`ScribeCuneiformFieldRender` + `CuneiformTextRender`), translate the **blurred halo pass** by
      `(OffsetXFraction, OffsetYFraction) * em` before painting, then draw the crisp ink pass
      un-offset. Confirm the reset-shared-paint discipline (mask filter → null, color/style restored)
      still holds and the reveal-range/overlap behavior is unchanged.

## 3. Theme: state axis for ink

- [x] 3.1 In `src/Mod/ScribeTheme.cs`, change `ForTablet(string? material, bool pixelArt)` →
      `ForTablet(string? material, TabletState state, bool pixelArt)`. Source `OnSurface`/`OnBackground`
      from `TabletReadability.For(material, state).BodyInk`; `OnSurfaceVariant` keeps deriving from it
      via the existing `ShiftBrightness` lift. Leave `Primary`/`Secondary`/`Surface*`/`Border`/
      `Background`/`Error`/state overlays per-material (state-independent). Keep the `wax`/unknown → fire
      fallback and the Pixel-Art-off → `ThemeData.Default` behavior.
- [x] 3.2 Have `ClayPalette` take the per-state ink (thread the bundle's `BodyInk` in) OR resolve ink
      inside `ForTablet` and overwrite the role after — whichever keeps `ClayPalette`'s per-material
      accent authoring intact and the diff smallest. Update the doc-comments to say ink is state-keyed.

## 4. Link ink: per-state via the existing seam

- [x] 4.1 In `src/Mod/ScribeTheme.cs`, change `ForTabletLink(string? material)` →
      `ForTabletLink(string? material, TabletState state)` sourced from
      `TabletReadability.For(material, state).LinkInk`. Delete the four `Tablet*Link` constants (now in
      the bundle). It stays a `ScribeRowStyle.LinkColor` value — NOT a `ColorScheme` role.

## 5. Stroke-weight scale into the cuneiform render

- [x] 5.1 Add a `float strokeWeightScale` arg to the cuneiform render path (the field + title render
      objects) that multiplies `GlyphStroke.Weight` at paint time. Default `1.0f` = current weight
      exactly. Applies only to the cuneiform tablet path; the normal-font path is untouched. No
      `src/Core/` change (Core keeps emitting base weights).

## 6. Wire it through the tablet dialog (resolve once, decompose)

- [x] 6.1 In `src/Mod/GuiDialogScribeTablet.cs`, resolve `var readability =
      TabletReadability.For(_material, _state);` once in the build path. Update `ResolveTheme` to call
      `ForTablet(_material, _state, pixelArt)`; update the `DecorateRowStyle` `LinkColor` to
      `ForTabletLink(_material, _state)` (or read `readability.LinkInk` directly); pass
      `readability.StrokeWeightScale` into the cuneiform render at all three glyph sites (rows, resting
      title, editing title). The three `CuneiformGlowTable.For(_material, _state)` calls stay as-is
      (they now read the bundle internally).

## 7. Update the other caller

- [x] 7.1 Update `src/Mod/GuiDialogScribeChalkboard.cs`'s `ForTablet(...)` call(s) for the new `state`
      parameter (the chalkboard passes its own fixed material/state — confirm it compiles and the
      chalkboard visuals are unchanged). Grep to confirm `ForTablet`/`ForTabletLink` have no other
      callers.

## 8. Build + tests + restage

- [x] 8.1 `dotnet build src/Mod/Mod.csproj -c Debug` — 0 warnings / 0 errors.
- [x] 8.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — confirm no NEW failures beyond the known
      pre-existing `ScribeBrightnessCurveTests`/`ScribePlayerSettingsTests` ones (Core is untouched by
      this Mod-only change).
- [x] 8.3 `build/restage.sh Debug` (only while the client is NOT running).

## 9. In-game verification (authoritative; supersedes the old glow/ink gates)

- [x] 9.1 For each clay (blue, red, fire), use `/scribe tablet hard` / `/scribe tablet fired` on a
      written tablet and confirm the cuneiform reads clearly in all three states — ink darkens on fired,
      the per-clay light halo lifts the ink on hard/fired, and the shared dark halo seats the ink on wet.
- [x] 9.2 Confirm the stroke-weight scale visibly firms up strokes from wet (0.9) → fired (1.1) without
      changing layout, and the glow offset reads as a seated drop (not a symmetric aura) on hard/fired.
- [x] 9.3 Confirm link/Tracker/Craft row names use the new per-state link ink and stay legible in each
      state; confirm wax renders unchanged (its own wet bundle) and an unknown material rides fire.
- [x] 9.4 Regression: Lectern/Notebook/Chalkboard and the non-cuneiform readable path are visually
      unchanged (no bundle applied). Pixel-Art OFF still follows the global theme.
- [x] 9.5 Re-run `TESTING.md` `00000016` (fired/hardened cuneiform readability) and record the verdict;
      retire the superseded gates in `add-tablet-state-glow-modifier` (4.1–4.4) and
      `tablet-text-visibility` (5.6) as folded into this pass.

- [x] 9.6 **Second retune re-verification (2026-08-21 ~12:15–12:18 exports).** 9.1–9.5 above verified the
      FIRST bake; blue wet, blue hard, red wet, fire wet, and wax wet then changed again (see the §1.2
      table note). Re-check just those 5 views: blue clay wet + hard, red clay wet, fire clay wet, and wax
      wet — confirm the retuned ink/link/glow still reads clearly and the wax glow (now a much fainter
      0.28-strength halo, was 0.76) doesn't wash out or under-light. Blue fired, red hard/fired, and fire
      hard/fired are unaffected and don't need re-checking.
