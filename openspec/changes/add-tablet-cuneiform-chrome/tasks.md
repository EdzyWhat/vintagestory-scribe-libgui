## 1. Remove the display-only cuneiform title banner

- [x] 1.1 Delete `BuildTitleBanner()` from `src/Mod/GuiDialogScribeTablet.cs` and any supporting fields/usings that become unused
- [x] 1.2 Collapse `GuiDialogScribeTablet.BuildCentralRegion()` to return `BuildEditorContent()` directly (remove the `Column`/`Expanded` banner stack)
- [x] 1.3 Update the class XML doc-comment to drop the "cuneiform title banner" bullet and describe the live cuneiform title/row rendering
- [ ] 1.4 Build and open a tablet to confirm no banner renders and the editor fills the central region

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
- [ ] 4.5 Confirm the incumbent dialogs (Lectern, both Notebooks) still render and edit their titles and rows in the normal font — deferred to the in-game playtest (task 7.2)

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
- [ ] 6.2 Verify toggling `DisableCuneiformFont` flips all tablet surfaces together (cuneiform ↔ normal editable font) and rows/title stay editable in both states
- [ ] 6.3 Verify Scribe Settings renders in the normal readable font in both cuneiform states

## 7. Validation and cleanup

- [x] 7.1 Run `dotnet test` (Core suite) and confirm the new caret-map + wrap tests pass — 238 pass, 0 fail (2026-08-02)
- [ ] 7.2 Manual in-game check: type live cuneiform in a row (glyphs appear with a blinking caret); arrow-nav and click-to-place the caret; row shows no border at rest and gains border+background on focus; edit the title live in cuneiform; long title truncates in the band; long rows wrap; the gear opens Settings and matches the ⓘ styling; button labels are cuneiform; disable-cuneiform reverts every surface while Settings stays legible; works on both clay and wax tablets
- [ ] 7.3 Update `VSAPI-NOTES.md` LibGUI section if a cuneiform caret/wrap/hit-testing gotcha is learned
- [x] 7.4 Run `openspec validate add-tablet-cuneiform-chrome --strict` and reconcile any remaining issues — valid (2026-08-02)
