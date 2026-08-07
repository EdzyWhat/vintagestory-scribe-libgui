## 1. Remove the display-only cuneiform title banner

- [x] 1.1 Delete `BuildTitleBanner()` from `src/Mod/GuiDialogScribeTablet.cs` and any supporting fields/usings that become unused
- [x] 1.2 Collapse `GuiDialogScribeTablet.BuildCentralRegion()` to return `BuildEditorContent()` directly (remove the `Column`/`Expanded` banner stack)
- [x] 1.3 Update the class XML doc-comment to drop the "cuneiform title banner" bullet and describe the live cuneiform title/row rendering
- [x] 1.4 Build and open a tablet to confirm no banner renders and the editor fills the central region — Confirmed in-game 2026-08-06 (no banner; editor fills central region), folded into the §7.2/§8 tablet playtest sweep.

## 2. Core: per-character advance map + wrap in `CuneiformLineLayout`

- [x] 2.1 In `src/Core/Cuneiform/CuneiformLineLayout.cs`, surface the cumulative grid-unit X at each source-character boundary (length = text length + 1) that the `Layout` loop already computes; add helpers to map a character index → X and an X → nearest character index. Keep the existing return shape working (additive)
- [x] 2.2 Add a wrap API: lay a string out into multiple `CuneiformLine`s given a max width in grid units, breaking at word-gap (space) boundaries; keep the single-line API as the no-max default (no VS API reference in Core)
- [x] 2.3 Handle edge cases: no max width = one line identical to today; a single word wider than max width occupies its own line and does not throw; empty/whitespace input; indices stay stable across spaces and missing glyphs
- [x] 2.4 Add xUnit tests in the Core test project covering: char→X map (incl. uppercase-fold + space/missing-glyph gap stability), X→index, word-boundary wrap, no-wrap parity with current single-line output, an over-long word, and empty/whitespace input

## 3. Mod: cuneiform editable render object

- [x] 3.1 Add a cuneiform editable `RenderBox` (alongside `src/Mod/CuneiformText.cs`) that paints a text buffer as cuneiform strokes via `context.Canvas` and draws a synthetic caret bar (`DrawBox`) at the current character boundary when focused, using the Core per-character X map scaled grid→pixel
- [x] 3.2 Implement the geometry contract `ScribeMultilineFieldState` depends on: `OffsetAtPosition` (click position → character index) and `CaretOffsetVertical` (vertical caret motion), backed by the Core map; stack wrapped lines vertically for the row height
- [x] 3.3 Add a single-line variant (or mode) for the title, with truncation in a fixed-height band per design D5/D-Q1 — `ScribeCuneiformFieldRender.SingleLine` (one line, no wrap, fixed one-line height; overflow hard-clipped by an enclosing `Clip`), threaded through `ScribeCuneiformFieldRenderWidget`

## 4. Mod: route rows and title through live cuneiform

- [x] 4.1 In `src/Mod/ScribeMultilineField.cs`, select the cuneiform render object instead of the normal renderer when a `UseCuneiform` flag (threaded from the tablet) is true; keep the normal renderer when false so incumbents and the disabled-cuneiform fallback are unchanged
- [x] 4.2 Thread the `UseCuneiform` flag (plus ink color / `GlyphBundle`) from `GuiDialogScribeTablet` down to its editor rows only — the Lectern/Notebook editor passes false (default)
- [x] 4.3 Add a `protected virtual` title seam on `ScribeDialogBase` (e.g. `BuildTitleDisplay`/`BuildTitleField`) whose default returns today's `RichText`/`TextField`, leaving the Lectern and Notebooks byte-identical — done in `ScribeDialogBase.Layout.cs`: `BuildTitleBar` now calls `private protected virtual BuildTitleDisplay/BuildTitleField`; the maxlength + Enter/Escape commit logic is extracted to `private protected OnTitleFieldKeyDown`; the shared `_titleController`/`_titleFocusNode` are exposed via `private protected TitleController`/`TitleFocusNode` in `ScribeDialogBase.cs`
- [x] 4.4 Override the title seam in `GuiDialogScribeTablet` to render/edit the title as single-line cuneiform under the `UseCuneiform` branch, keeping the `_isTitleEditing` / `CommitTitleIfEditing` / `_pending*` machinery intact — done: tablet overrides `BuildTitleDisplay` (display-only `CuneiformText` in a `Clip`) and `BuildTitleField` (new `ScribeCuneiformTitleField`, a single-line cuneiform input BOUND to the shared controller/focus node so all base commit/blur/deferred-rebuild machinery is untouched; falls back to `base.*` when cuneiform disabled OR bundle not yet loaded, via `ActiveCuneiformBundle`)
- [x] 4.5 Confirm the incumbent dialogs (Lectern, both Notebooks) still render and edit their titles and rows in the normal font — deferred to the in-game playtest (task 7.2) — Confirmed 2026-08-06 playtest (incumbent dialogs unaffected; recorded in TESTING.md under add-tablet-cuneiform-chrome).

<!-- ========================================================================================
     SESSION CHECKPOINT (2026-08-02, second session) — pick up here.

     DONE this session — ALL remaining CODE is complete; build is 0 errors, Core suite 238 pass:
       • Compile fix from prior checkpoint: added `using Gui.Rendering.Text;` to GuiDialogScribeTablet.cs.
       • 5.1 focus-driven row chrome, 5.4 cuneiform button labels, 5.5 no-material-branch verify,
         6.1 single-branch consolidation (`ActiveCuneiformBundle`), 7.1 tests — see those rows.

     ALL THAT REMAINS is manual/validation:
       • In-game playtest tasks: 1.4, 4.5, 6.2, 6.3, 7.2 — run the tablet in-game and check the
         checklist in 7.2 (type live cuneiform + caret; focus border; title truncation; row wrap;
         gear opens Settings; cuneiform labels; disable-cuneiform reverts every surface; Settings stays
         legible; clay + wax). Use the `what-to-test` skill / TESTING.md to record verdicts.
       • 7.3 VSAPI-NOTES — only if a cuneiform caret/wrap/hit-test gotcha surfaces during the playtest.
       • 7.4 `openspec validate add-tablet-cuneiform-chrome --strict` — run + reconcile.
     ======================================================================================== -->

## 5. Mod: focus appearance, gear button, cuneiform labels

- [x] 5.1 Drive the tablet row's border/background from focus state on the already-present `Container`/`BoxStyle` (borderless/transparent at rest → bordered + background on focus), using theme roles; never swap the row widget type on focus — done: `ScribeCuneiformFieldRenderWidget` now paints NO field box (`boxColor`/`borderColor` = `Vector4.Zero`, thickness 0) so the row `Container` is the sole appearance driver; `ScribeEditRowState` adds a focus-node listener (`OnFieldFocusChanged` → `SetState`, plus `UpdateWidget`/`Dispose` plumbing) and, on the cuneiform path only (`style.UseCuneiform && FocusNode.HasFocus`), the always-present `Container` gains `SurfaceHigh` fill + `Primary` 1px border + 4px `CornerRadius`. Widget type never swaps. Normal path untouched (field keeps its own border)
- [x] 5.2 Add an `OnOpenSettings` action (ctor param + property) to `ScribeEditorContent`, mirroring `OnOpenEditorReference`; wire it at `ScribeDialogBase.Layout.cs` to `modSystem.OpenSettings` — done via the `EditorSettingsGearAction` virtual seam (default null; tablet overrides)
- [x] 5.3 Append a `scribegear` `Tooltip`+`Button` after the Information button in `BuildFooterButtons`, styled identically (`ButtonStyle with { Padding = EdgeInsets.All(7) }`, icon size ~17f, `colors.OnPrimary`) — done, gated on `Widget.OnOpenSettings is not null`; tooltip reuses `scribe-gui-nav-settings`
- [x] 5.4 Route the tablet's button labels (e.g. "Add task") through the cuneiform path under the same `UseCuneiform` branch — done: new `ScribeEditorContent.BuildButtonLabel(label, style)` returns a `CuneiformText` (em = label font size, ink = label color, so it dims at the tier cap) when `Style.UseCuneiform` + bundle non-null, else the normal `Text`; wired for both "Add task" and "Done editing". Null bundle falls back to the readable label
- [x] 5.5 Confirm no per-material branching is introduced — the cuneiform body/label path is material-agnostic and takes ink color only from the resolved theme — verified: no `Material`/material reads anywhere in the cuneiform path; ink comes from `colors.OnSurface` (rows/title) or the button label color (labels), all from the resolved theme; the sole branch is `ScribeTaskFont.UseCuneiform`

## 6. Single fallback branch + settings legibility

- [x] 6.1 Evaluate `ScribeTaskFont.UseCuneiform(...)` once per tablet build and thread the boolean (plus resolved fallback family and `GlyphBundle`) into title, rows, and buttons — no per-widget `DisableCuneiformFont` reads — done: consolidated into the single `ActiveCuneiformBundle` property (evaluates `ScribeTaskFont.UseCuneiform` once, returns the bundle or null); `DecorateRowStyle` now reads it (so rows + button labels get `UseCuneiform`/bundle via `ScribeRowStyle`), and both title overrides read it. No widget reads `DisableCuneiformFont` directly (only the two dialog-level entry points via that one property)
- [x] 6.2 Verify toggling `DisableCuneiformFont` flips all tablet surfaces together (cuneiform ↔ normal editable font) and rows/title stay editable in both states — Confirmed 2026-08-06 playtest (TESTING.md add-tablet-cuneiform-chrome).
- [x] 6.3 Verify Scribe Settings renders in the normal readable font in both cuneiform states — Confirmed 2026-08-06 playtest (TESTING.md add-tablet-cuneiform-chrome).

## 7. Validation and cleanup

- [x] 7.1 Run `dotnet test` (Core suite) and confirm the new caret-map + wrap tests pass — 238 pass, 0 fail (2026-08-02)
- [x] 7.2 Manual in-game check: type live cuneiform in a row (glyphs appear with a blinking caret); arrow-nav and click-to-place the caret; row shows no border at rest and gains border+background on focus; edit the title live in cuneiform; long title truncates in the band; long rows wrap; the gear opens Settings and matches the ⓘ styling; button labels are cuneiform; disable-cuneiform reverts every surface while Settings stays legible; works on both clay and wax tablets — Confirmed 2026-08-06 playtest (TESTING.md add-tablet-cuneiform-chrome `6017abe3` type-live-cuneiform + related items).
- [x] 7.3 Update `VSAPI-NOTES.md` LibGUI section if a cuneiform caret/wrap/hit-testing gotcha is learned — DONE (conditional, satisfied): the LibGUI section of VSAPI-NOTES.md already documents the cuneiform-relevant caret/wrap/hit-testing gotchas (focus-jump-to-element-0 on recompose + caret capture/restore; GuiElementTextInput vs TextArea wrap behavior; row-kind hit-testing). No NEW gotcha surfaced this round that isn't already recorded.
- [x] 7.4 Run `openspec validate add-tablet-cuneiform-chrome --strict` and reconcile any remaining issues — valid (2026-08-02)

## 8. Playtest refinements (2026-08-02 retest)

<!-- Surfaced by the 2026-08-02 in-game retest (7/9 items pass; see TESTING.md). The balloon regression
     from the first playtest is already fixed (see task 5.4 / TESTING.md a029001d). These are the
     remaining fails + author-requested polish, folded into this change per the 2026-08-02 update. Text
     selection over cuneiform stays OUT of scope (explore-cuneiform-text-selection). -->

- [x] 8.1 Make the synthetic caret **blink** at the normal editable field's cadence (it currently renders
      static). Reuse the normal field's blink timing rather than inventing a new one. (Resolves design D-Q3;
      retests `6017abe3`.) — done: added a `Ticker` (via `Element.Owner.GetTickerProvider()`) in the SHARED
      `ScribeMultilineFieldState` toggling a `caretVisible` gate every 500ms (`CaretBlinkMs`, matching LibGUI
      `TextField.CursorBlinkMs`); reset-to-solid on edit/caret-move/click/focus-gain, paused while a selection
      is active or focus is lost. `caretVisible` threads into BOTH render objects (normal + cuneiform), so the
      Lectern/Notebook and tablet carets blink at the identical cadence. The reimplemented field never blinked
      before; both paths now do.
- [x] 8.2 Handle **Shift+Enter** as a line break in a cuneiform row (it currently does nothing). Route it
      through the same buffer path the normal field uses so the row grows a wrapped line. (Retests `6017abe3`.)
      — done: the State already inserted `\n` on Shift+Enter, but the cuneiform Core layout ignored it. Made
      `CuneiformLineLayout.LayoutWrapped` paragraph-aware — it splits on `\n` FIRST (always, even with soft-wrap
      off), then word-wraps each paragraph, stamping absolute `SourceStart`s across the newline; a blank
      paragraph yields an empty line so the caret can rest on it. 3 new Core tests (hard-newline split, blank
      paragraph, newline + soft-wrap together).
- [x] 8.3 Fix the **trailing-space caret advance** in the Core per-character X map: a space typed at the end
      must advance the caret by its `WordGapUnits` immediately, not wait for the next non-space char. Add a
      Core xUnit test covering trailing/consecutive spaces in the char→X map. — done: `LayoutWrapped`'s final
      line now runs to the source end (`source.Substring(lineStart)`) so trailing whitespace is kept (interior
      wrap-separator spaces stay dropped); the single-line `Layout` already counted every char. 4 new Core tests
      (trailing/consecutive spaces single-line + wrapped final-line + interior-separator regression guard), 242 pass.
- [x] 8.4 Bump the **global cuneiform em-scale** (D7): correct `CuneiformText`'s rendered height to match the
      surrounding readable text's line-height (~1.4× ratio) so cuneiform no longer reads ~30% short, applied
      at the `CuneiformText` level (all surfaces), then trim the "Add task" button's vertical padding (~8px).
      Re-check the title bar, rows, and footer label fit/wrap after the bump (not just the footer). — done:
      added a shared `CuneiformMetrics.LineHeightRatio` (1.4f) and multiplied it into the em→pixel scale AND
      rendered height in BOTH cuneiform render objects (`CuneiformTextRender` display + `ScribeCuneiformFieldRender`
      rows/title), so every surface grows together, scale-independently. Trimmed the labelled footer buttons'
      vertical padding on the cuneiform path only (`Symmetric(10,20)` → `Symmetric(6,20)`, −8px); normal path
      keeps the default theme padding so Lectern/Notebook footers are byte-identical. Fit/wrap of the title
      band + rows is the in-game retest (8.6).
- [x] 8.5 Rename the setting `DisableCuneiformFont` → **`CuneiformTablets`** (default true) (D8): flip the
      `ScribePlayerSettings` field and every read site (incl. `ScribeTaskFont.UseCuneiform`, the tablet
      branch, `ScribeRowConstants`, the harness), the Settings checkbox + its lang label, and add a one-time
      on-disk migration `CuneiformTablets = !DisableCuneiformFont`. Add a unit test for the migration mapping.
      — done: `ScribePlayerSettings.CuneiformTablets` (default true, positive polarity) replaces the negative
      field; the old key survives as a nullable legacy shim `DisableCuneiformFont` (deserialized only from a
      pre-flip file) folded in by a new `MigrateLegacyKeys()` (`CuneiformTablets = !DisableCuneiformFont`,
      then cleared) called from `Normalized()`, and `ShouldSerializeDisableCuneiformFont() => false` keeps
      the legacy key out of any file the current code writes (no Newtonsoft ref needed in Core — just the
      ShouldSerialize convention). `ScribeTaskFont.UseCuneiform` is now a straight pass-through; the tablet
      branch (`ActiveCuneiformBundle`), harness, and Settings checkbox read the positive field; lang key
      renamed `settings-disablecuneiform` → `settings-cuneiformtablets` ("Cuneiform tablets", "Turn off …").
      5 new Core migration tests (invert / absent-key no-op / clear+idempotent / ShouldSerialize / default);
      250 Core pass, Mod build 0 errors.
- [x] 8.6 Re-run the manual playtest items (`6017abe3` type-live-cuneiform, `a029001d` label scale) after
      8.1–8.5 and record verdicts in TESTING.md. — Confirmed 2026-08-06 playtest (both re-run items recorded in TESTING.md add-tablet-cuneiform-chrome).
- [x] 8.7 Run `openspec validate add-tablet-cuneiform-chrome --strict` again after these refinements land.
      — valid (2026-08-02).
