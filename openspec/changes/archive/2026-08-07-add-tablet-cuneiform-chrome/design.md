## Context

`add-tablet-dialog` (Proposal C) shipped `GuiDialogScribeTablet` — an always-edit, tabless
`ScribeDialogBase` subclass. Its central region stacks a display-only `CuneiformText` **title banner**
(`BuildTitleBanner()`) above the inherited editable task list (`BuildEditorContent()`). The 2026-08-02
playtest rejected the banner as a redundant second copy of the title already shown in the title bar,
and it surfaced a rendering gap: `CuneiformText` does not wrap or truncate, so a long title ran off
the edge (toggling back to the normal font wrapped it correctly).

This change pivots to the player's stronger vision: delete the banner, and make the tablet's *real*
chrome a **live cuneiform input** surface — you type in the editable title bar and the editable task
rows and the glyphs form as cuneiform, with a synthetic caret — while keeping Scribe Settings in the
normal readable font. The base-dialog seams needed for this were already opened by Proposal C:
`BuildCentralRegion()`, `BuildRightColNav()`, `BuildEditorContent()`, `ResolveTheme(bool)`,
`ShowEditorSwitchToRead`, and the `DisplayDocumentTitle` helper are all `protected` /
`protected virtual` in `src/Mod/ScribeDialogBase.Layout.cs`.

The central technical fact shaping every decision: **cuneiform is not a font.** It is rectangle stroke
geometry authored in glyph-forge, parsed by `Scribe.Core.Cuneiform`, and painted as filled quads on
the raw Skia canvas by the `CuneiformText` widget (`src/Mod/CuneiformText.cs`, a
RenderBox/RenderObjectWidget/StatefulWidget trio). There is no TTF entry in `FontRegistry` to swap a
`FontFamily` to. Making a surface "cuneiform" therefore means routing its text through a cuneiform
render path — not changing a font family string — and, for an *editable* surface, driving that render
path from the existing text buffer while drawing a synthetic caret (cuneiform has no native caret).

The enabling discovery (exploration, 2026-08-02): `ScribeMultilineField` is a three-class widget whose
`State` (the text buffer plus all keyboard, caret, and navigation handling) is **cleanly decoupled from
rendering** — it talks to its render object only through caret/selection integers and a few geometry
queries (`OffsetAtPosition`, `CaretOffsetVertical`), and the caret is a plain `DrawBox` bar independent
of how glyphs are drawn. So the same typing brain can drive a cuneiform render object. The one missing
piece is a **per-character advance map** from Core layout (see D5), which today is computed inside the
layout loop but discarded.

## Goals / Non-Goals

**Goals:**

- Remove the display-only cuneiform title banner entirely.
- Make the editable task rows a **live cuneiform input**: typing renders cuneiform in place with a
  synthetic caret; caret navigation and click-to-place work over cuneiform. Reuse the existing
  multiline-field buffer/keyboard `State`.
- Make the editable title bar a **live cuneiform input** too, consistent with the rows, with its
  pencil → focus → commit-save machinery unchanged.
- Give a focused row a **focus-driven appearance** (borderless/transparent at rest → bordered +
  background on focus) via the row's already-present container styling — no widget-type swap.
- Add a **settings gear button** in the footer, right of the info button, styled identically, opening
  Scribe Settings.
- Render the tablet button labels (e.g. "Add task") in cuneiform.
- Keep Scribe Settings in the normal readable font.
- Keep the single `ScribeTaskFont.UseCuneiform` branch point: one decision routes every tablet
  surface to cuneiform or to the resolved task font.
- Give the cuneiform path an explicit line-wrap / truncation policy AND a per-character advance map
  (for the caret / hit-testing) so cuneiform text in bounded, editable chrome behaves predictably.
- Keep the tablet ONE material-keyed parameterized type — the cuneiform work must be
  material-agnostic, never a subclass per clay/wax/fired material.

**Non-Goals:**

- Text selection over cuneiform (shift-arrow / drag / word- and line-select + highlight box) — this
  round is caret-only; selection is the deferred `explore-cuneiform-text-selection` stub.
- The clay-type / fired backdrop set (`add-tablet-clay-type-backdrops`).
- Deferred tablet mechanics: firing, water damage, carry-forward migration, wax-wipe, stylus gate.
- Changing the Scribe Settings font.
- Cuneiform on the Lectern or Notebook dialogs (tablet-only).

## Decisions

### D1 — Remove the banner; don't relocate it

Delete `BuildTitleBanner()` and collapse `GuiDialogScribeTablet.BuildCentralRegion()` back to just
the editable content (`BuildEditorContent()`, no wrapping `Column`/`Expanded` banner stack). The
title is already present in the title bar; the banner was pure duplication.

*Alternative considered:* keep the banner but shrink/restyle it. Rejected — the playtest verdict is
that any second title is redundant, and the value is a cuneiform *page*, not a cuneiform *label*.

### D2 — Rows and title are LIVE cuneiform input, driven by the existing buffer/keyboard State

The editor rows use `ScribeMultilineField`, whose `State` owns the text buffer and every keystroke,
caret, and navigation handler and is **decoupled from rendering** — it pushes caret/selection integers
into its render object and asks it a few geometry questions (`OffsetAtPosition` for click→index,
`CaretOffsetVertical` for vertical caret motion), and the caret is a `DrawBox` bar independent of glyph
drawing. The decision is therefore **not** a display/edit swap: the row stays cuneiform the whole time,
and we drive a **cuneiform render object** from that same State instead of the normal text renderer.

Concretely:

- Add a cuneiform editable render object (a `RenderBox` next to `CuneiformText`) that: (a) paints the
  buffer as cuneiform strokes via `context.Canvas` like `CuneiformText` does; (b) draws a synthetic
  caret bar at the current character boundary when focused; and (c) implements the same geometry
  contract the State expects (`OffsetAtPosition`, `CaretOffsetVertical`, caret-X), backed by the Core
  per-character advance map (D5).
- `ScribeMultilineField.Build` selects this render object instead of the normal one when `UseCuneiform`
  is true (a flag threaded from the tablet). When cuneiform is disabled, it uses the normal renderer —
  so the disable-cuneiform toggle flips rows back to a legible editable field. This is tablet-only: the
  Lectern/Notebook editor passes the flag false (its default) and is untouched.
- The title bar uses a stock LibGUI `TextField`, not `ScribeMultilineField`, so its live-cuneiform path
  is a **single-line cuneiform input** rather than a reuse of the row field's State. The title's edit
  machinery (`_isTitleEditing`, `CommitTitleIfEditing`, `_pendingTitleEditRebuild`/`_pendingTitleFocus`,
  `MaxTitleLength`) stays intact; only the rendered widget changes. Expose it behind a small
  `protected virtual` seam on the base dialog (e.g. `BuildTitleDisplay`/`BuildTitleField`) whose default
  returns today's `RichText`/`TextField`; `GuiDialogScribeTablet` overrides to the cuneiform input under
  the one `UseCuneiform` branch. Lectern and both Notebooks inherit the default and are unchanged.

*Alternative considered (rejected):* a display/edit split — cuneiform at rest, a normal legible field
while typing. Rejected because the player explicitly wants to *type in cuneiform*; a legible-while-
typing state contradicts that. Caret-only (no selection highlight) is the accepted v1 scope-cut.

*Alternative considered (rejected):* a bespoke cuneiform title bar forked into the tablet. Rejected —
it would duplicate the drag-grip / close / pencil / save chrome the base already owns, violating the
"reuse by extension" discipline Proposal C established. The seam keeps the chrome in the base.

### D3 — Button labels render cuneiform; focused rows change appearance without a widget swap

- **Button labels** (e.g. "Add task", and tablet footer labels) render cuneiform when `UseCuneiform`
  is true. These are static text; routing them through `CuneiformText` is direct.
- **Focus-driven row appearance:** `ScribeEditRow` already wraps its body in an always-present
  `Container`/`BoxStyle` (fill `Vector4.Zero` at idle) — the intended seam for cheap fill/border
  changes — and the field's own border already flips `Border`→`Primary` on focus. The row gains a
  visible border + background when focused/clicked and reverts when it loses focus, driven by that
  existing container and theme roles (`Primary`/`Border`, or `StateHover`/`StateSelected`). This is an
  appearance change only: the widget type never swaps, so reconciliation (and any in-progress caret /
  text) is preserved — matching the structural-stability discipline the row already documents.
- **Settings gear button:** append a `scribegear` `Tooltip`+`Button` after the Information button in
  `BuildFooterButtons` (`ScribeEditorContent.cs`), styled identically (`ButtonStyle with { Padding =
  EdgeInsets.All(7) }`, icon size ~17f, `colors.OnPrimary`). Wire it through a new `OnOpenSettings`
  action on `ScribeEditorContent` (mirroring `OnOpenEditorReference`), pointed at `modSystem.OpenSettings`
  in `ScribeDialogBase.Layout.cs`. The `scribegear` icon is already registered.

### D4 — One `UseCuneiform` branch, evaluated once per build and threaded down

Keep `ScribeTaskFont.UseCuneiform(modSystem.MySettings.DisableCuneiformFont)` as the sole decision.
Evaluate it once in the tablet's build and thread the resulting boolean (plus the resolved fallback
`TextStyle`/family and the `GlyphBundle`) into the title display, the row display, and the button
labels — so every surface agrees and disabling cuneiform flips all of them in one place. No
per-widget `DisableCuneiformFont` reads.

Settings stays out of this entirely: it is a separate window that never consults `UseCuneiform`, so
it is legible by construction (no code change needed to "keep it normal" — just don't route it
through the cuneiform path).

### D5 — Core layout gains a per-character advance map AND wrap/truncation

This is the real new engineering, and both pieces live in **Core** (unit-testable, no VS API).
`CuneiformLineLayout.Layout(text)` today returns a single `CuneiformLine` (flat stroke list +
`TotalWidth` + `LineHeight`) and `CuneiformTextRender.PerformLayout` sizes to that width ignoring
`Constraints.MaxWidth`. Two additions:

**(a) Per-character advance map (new — enables the caret and hit-testing).** The `Layout` loop already
tracks `pen` at every character boundary (it advances by `WordGapUnits` for spaces,
`MissingGlyphGapUnits` for unknown chars, and `AdvanceWidth` + `GapBetween` for glyphs) — it just
discards those positions. Surface them as a cumulative `double[]` of grid-unit X per source-character
boundary (length = text length + 1). Add helpers to map a character index → X and an X → nearest index.
The editable render object multiplies by its cached grid→pixel `scale` to place the caret bar and to
resolve clicks (`OffsetAtPosition`). Indices stay stable across spaces/missing glyphs because every
source char advances the pen. Keep the existing return shape working (additive — the map is extra data
on the result, or a parallel method).

**(b) Wrap / truncation.** Extend the layout to break a string into multiple `CuneiformLine`s at
word-gap (space) boundaries given a max width in grid units — a cuneiform analogue of soft-wrap.
Preserve the single-line API for callers that don't wrap (overload / optional `maxWidthGridUnits`,
default = no wrap, so existing behavior and the dev harness are unchanged). The Mod widget converts its
incoming pixel `Constraints.MaxWidth` into grid units via the `fontSizeEm / lineHeight` scale, asks
Core to wrap, and stacks lines vertically (height = `lineCount × lineHeight`).

For the **title bar**, a fixed-height band, prefer **single-line truncation** over wrapping (a title
should not push the band taller). Since cuneiform has no '…' glyph, truncation is a **hard clip at max
width plus a deliberate cutoff affordance** (D-Q1) rather than an ellipsis character. Editable **task
rows wrap** (rows already auto-grow with content). Note that the editable row already stacks visual
lines itself for the normal renderer; the cuneiform render object reuses the same wrapped-line stacking
against the Core wrap output.

*Alternative considered:* clamp/scale-to-fit (shrink the em size until the line fits). Rejected for the
body (unbounded text would shrink to illegibility) but noted as a possible title fallback.

### D6 — Tablet stays one parameterized type keyed by material

The architectural directive holds: the tablet family is ONE type parameterized by a material texture
variable (unfired clay / wax / fired fire-clay / …), differing only in backdrop + font color — never
a subclass per material. All cuneiform-chrome work is material-agnostic: it reads ink color from the
resolved theme (`ResolveTheme`/`ScribeTheme.ForTablet`) and never branches on material. The clay-type
backdrop set that would exercise multiple materials is the separate `add-tablet-clay-type-backdrops`
followup; this change must not introduce a per-material code path.

### D7 — Global cuneiform em-scale so cuneiform matches surrounding readable text (2026-08-02 playtest)

`CuneiformText.PerformLayout` sizes its rendered height to exactly `FontSizeEm` (grid units scale so one
line-height maps to the em value). Normal `Text` at the same nominal font size renders at ~1.4× that in
line-height, so a cuneiform label reads ~30% shorter than adjacent readable text — the playtest measured
the footer "Add task" cuneiform label at ~52px against ~62px sibling buttons.

Decision: apply the scale correction **globally at the `CuneiformText` level** (so every cuneiform
surface — rows, title, labels — benefits), not as a per-call fudge at the footer. Bring the rendered
cuneiform height in line with the surrounding readable text's *rendered line-height* rather than its raw
font-size — the scale-independent framing (multiply em by the ~1.4 line-height ratio, or expose a
line-height-matched sizing mode) so it holds at any GUI scale rather than hardcoding a pixel target.
Measured reference: footer label em 14 → ~52px; parity ≈ em 20–26 depending on the exact ratio. Also trim
the "Add task" button's vertical padding (~8px) to close the residual gap after the scale bump.

*Alternative considered:* bump only the footer label's em. Rejected — the same short-glyph mismatch
affects the title bar and rows, and the author explicitly wants the fix applied globally.

### D8 — Flip the cuneiform setting to positive polarity, with migration (2026-08-02 playtest)

The `DisableCuneiformFont` client setting (default false) reads awkwardly. Rename it to a positive
`CuneiformTablets` boolean (default true): true = custom cuneiform font, false = standard task font.
`ScribeTaskFont.UseCuneiform` and the single tablet branch invert accordingly (the branch stays a single
decision — only its polarity flips). On load, migrate any existing on-disk client config carrying the old
key: `CuneiformTablets = !DisableCuneiformFont`; absent → the `true` default. This is client-local
preference (`ScribePlayerSettings`), not synced world/document state, so there is no network or document
migration.

*Alternative considered:* keep `DisableCuneiformFont` and only relabel the UI. Rejected — a negative
field behind a positive label invites the classic double-negative bug at every read site; inverting the
field itself is clearer at the source.

## Risks / Trade-offs

- **[Cuneiform wrap + caret map are genuinely new layout code]** → Put both in Core with xUnit
  coverage (char→X map incl. uppercase-fold + space/missing-glyph gap stability; X→index; word-boundary
  wrap; single word wider than max; empty/whitespace; no-wrap default parity with today). Only the
  pixel↔grid conversion, caret `DrawBox`, and vertical stacking live in the Mod widget.
- **[Driving a cuneiform renderer from the field State]** → Reuse `ScribeMultilineFieldState` verbatim;
  the only new surface is the render object's geometry contract (`OffsetAtPosition`, `CaretOffsetVertical`,
  caret-X), backed by the Core map. Keep the normal renderer for the disabled-cuneiform path so the
  incumbents and the fallback both use the proven code.
- **[Title focus/rebuild on the live-cuneiform swap]** → Reuse the existing deferred rebuild/focus
  machinery (`_pendingTitleEditRebuild` / `_pendingTitleFocus`) that already avoids the
  mid-pointer-dispatch unmount crash; the change is which widget renders, not the `_isTitleEditing`
  lifecycle, so no new lifecycle is introduced.
- **[Retires Proposal D]** → The live-cuneiform-input row removes D's input↔output toggle entirely (no
  normal-font input state). D's needed pieces are absorbed here; the dropped per-row legible escape
  hatch is superseded by the coarse disable-cuneiform toggle. Called out in the proposal.
- **[Legibility regression]** → The `UseCuneiform` fallback (one branch) must fully restore the
  normal editable field/font on every affected surface; a manual test toggles `DisableCuneiformFont`
  and verifies title bar, rows, and buttons all revert and stay editable. Settings is verified normal
  in both states.
- **[Focus appearance must not swap the widget]** → Drive the row's border/background from the
  always-present `Container`/`BoxStyle` only; never swap the row's widget type on focus, or an
  in-progress caret / text could be lost (the row's structural-stability discipline).
- **[Truncation with no ellipsis glyph looks abrupt]** → Prefer wrapping where height allows (rows);
  reserve truncation for the fixed-height title band, and pick a deliberate cutoff affordance
  (D-Q1) rather than a raw clip.
- **[Setting polarity flip could strand an existing preference]** (D8) → A player who had turned
  cuneiform OFF (`DisableCuneiformFont = true`) must land at `CuneiformTablets = false`, not the `true`
  default. The migration reads the old key once (`CuneiformTablets = !DisableCuneiformFont`) before
  falling back to the default; a Core/unit test on the migration mapping guards the inversion, and every
  read site must be audited so no lingering `DisableCuneiformFont` reference survives the rename.
- **[Global em-scale could overshoot other cuneiform surfaces]** (D7) → Scaling `CuneiformText` globally
  changes the title bar and rows too, not just the footer label; re-run the row/title/label playtest
  items after the bump to confirm the larger glyphs still fit their bands and wrap correctly, rather than
  eyeballing only the footer.
- **[ForceRebuild churn]** → The GUI rebuilds via `ForceRebuild` (see MEMORY / animation-lessons);
  the cuneiform render object is a stateless render box, so a rebuild re-lays-out cheaply — but confirm
  the wrapped-line layout + caret map don't allocate per-frame beyond the existing per-stroke path.

## Migration Plan

- No data/persistence migration: documents are unchanged; this is a render/layout change only. Saves
  still flow through `ScribeNotebookSaveMessage` (vanilla Sign write-through pattern).
- Supersede the banner requirement: `add-tablet-dialog` is already archived (2026-08-02), having
  folded the "display-only cuneiform title banner" into the `tablet-dialog` capability. This change's
  spec delta retires/replaces that banner requirement rather than contradicting the new title-bar
  rendering; when this change archives, the banner behavior leaves the canonical `tablet-dialog` spec.
- Rollback: revert the tablet's title override, the row renderer flag, and the footer gear; the
  base-dialog default seam and the `UseCuneiform=false` path return the incumbents (and the tablet) to
  the normal editable field/font with no data impact.

## Open Questions

- **D-Q1:** Title-band truncation affordance without a '…' glyph — hard clip, soft fade, or a
  cuneiform terminator stroke? (Leaning: soft fade / clip at the band's inner width.)
- **D-Q2:** Do the tablet's footer/handbook (ⓘ) affordance labels also go cuneiform, or only the
  primary "Add task" button? (Leaning: primary action labels cuneiform; icon-only affordances
  unaffected.)
- **D-Q3 (RESOLVED — 2026-08-02 playtest):** Caret behavior. The synthetic caret SHALL **blink** at the
  normal field's cadence (it currently renders static — rejected). **Shift+Enter** SHALL insert a line
  break in a cuneiform row. A **trailing space** SHALL advance the caret immediately (today the caret
  doesn't move until the next non-space char, because a trailing space contributes no glyph advance — the
  per-character X map must count the pending space's `WordGapUnits`). Click-to-place snaps to the nearest
  boundary (accepted as tested). Remaining caret width/height tuning against stroke weight is cosmetic and
  deferred to implementation. See D7 (scale) and the tasks group 8.
- **D-Q4:** Whether the cuneiform editable render object should be a distinct class or a render-mode
  flag on the existing `ScribeMultilineField` render object. (Leaning: distinct render object reusing
  the same State, to keep the normal path untouched.)
