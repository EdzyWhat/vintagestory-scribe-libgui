## Why

Proposal C (`add-tablet-dialog`) shipped `GuiDialogScribeTablet` with a display-only cuneiform
**title banner** stacked above the inherited editable task list. The 2026-08-02 playtest rejected
the banner: it is a redundant duplicate of the existing title bar (the same title, shown twice), and
it exposed a real gap — cuneiform text does not wrap or truncate the way normal text does, so the
banner ran off the edge. The tablet's whole point is that the *tablet itself* reads as cuneiform, not
that it grows an extra cuneiform label. The player's vision is stronger than a resting-display swap:
you **type in cuneiform** — glyphs form live as you type, in both the title and the task rows — so the
tablet feels like an ancient writing surface, not a normal note app wearing a cuneiform label. This
change removes the banner and makes the tablet's real chrome — the editable title bar and the editable
task rows, plus the button labels — a **live cuneiform input** surface, while keeping Scribe Settings
legible in the normal font.

## What Changes

- **BREAKING (supersedes `add-tablet-dialog`):** REMOVE the display-only `CuneiformText` title banner
  from `GuiDialogScribeTablet` — delete `BuildTitleBanner()` and the banner stack in
  `BuildCentralRegion()`. The tablet no longer shows a second, separate title.
- Make the **task rows live cuneiform input**: as the player types, their text renders as cuneiform
  strokes in place, with a **synthetic caret** (a drawn bar at the current character boundary, since
  cuneiform has no native caret). This reuses the existing `ScribeMultilineFieldState` buffer/keyboard
  brain (which is already decoupled from rendering) driving a cuneiform render object instead of the
  normal `DrawText` renderer. Caret-only editing this round — type, backspace, arrow-navigate,
  click-to-place the caret, and **Shift+Enter to insert a line break**; the **synthetic caret blinks**
  (matching the normal field's cadence) and **advances immediately on a trailing space**. **Text
  selection over cuneiform is deferred** (captured as a separate explore stub,
  `explore-cuneiform-text-selection`).
- Make the **title bar live cuneiform input** too, consistent with the rows. The title uses a stock
  LibGUI `TextField` (not the custom field), so this needs a **single-line cuneiform input** path; its
  pencil-edit → focus → commit-save machinery stays intact — only the rendering becomes cuneiform.
- Give focused rows a **focus-driven appearance** using the row's already-present `Container`/`BoxStyle`
  seam: borderless + transparent at rest, gaining a border and a visible background when clicked/focused.
  The glyph font stays on in both states — this is an appearance change, never a widget-type swap.
- Add a **Settings gear button** to the footer "Add task" row, immediately right of the Information (ⓘ)
  button and styled identically (`scribegear` glyph, same size/padding/color), wired to open Scribe
  Settings.
- Render the **button labels** (e.g. "Add task") in the cuneiform glyph font under the same branch.
- **KEEP Scribe Settings in the normal readable font.** The cuneiform-everywhere treatment stops at
  the tablet page; settings must stay legible.
- Preserve the SINGLE `UseCuneiform` branch point
  (`ScribeTaskFont.UseCuneiform(bool disableCuneiformFont)`): when the player disables cuneiform,
  every one of these surfaces falls back to the resolved task font (`ScribeTaskFont.Resolve`) in one
  decision, not per-widget. Disabling cuneiform is the coarse escape hatch that flips the whole tablet
  (title, rows, buttons) back to a legible font.
- Close the **line-wrap / truncation gap** in the cuneiform render path: normal `Text` wraps and
  ellipsizes; `CuneiformText` does neither today. Once the title bar and body are cuneiform, this gap
  becomes real work. Add an explicit wrap-or-truncate policy to the cuneiform layout/render path so
  long cuneiform strings behave predictably in bounded chrome. The same Core work surfaces the
  **per-character X map** the synthetic caret and click-hit-testing need.
- **Bump the global cuneiform em-scale.** `CuneiformText` sizes its rendered height to exactly its em
  value, whereas normal `Text` renders at ~1.4× line-height — so at the same nominal font size a
  cuneiform label reads ~30% shorter than the surrounding readable text (the ~52px-vs-62px footer-button
  gap the 2026-08-02 playtest measured). Scale cuneiform up **globally** (every `CuneiformText` surface,
  not just the footer label) so it matches the readable text's rendered line-height, and trim the "Add
  task" button's vertical padding to close the residual gap.
- **Rename the cuneiform setting to a positive polarity.** Replace the `DisableCuneiformFont` toggle
  (default false) with a positive **`CuneiformTablets`** boolean (default **true**): "on" shows the
  custom cuneiform font, "off" reverts the tablet to the standard task font. This inverts the persisted
  client-config key and its Settings label, flips `ScribeTaskFont.UseCuneiform` and the single tablet
  branch accordingly, and migrates any existing on-disk `DisableCuneiformFont` value
  (`CuneiformTablets = !DisableCuneiformFont`, else the true default).

## Capabilities

### New Capabilities

- `tablet-cuneiform-chrome`: the tablet's real chrome is a live cuneiform surface — the editable title
  bar and editable task rows accept typing that renders as cuneiform strokes with a synthetic caret,
  the button labels render cuneiform, focused rows gain a focus-driven border/background, and a settings
  gear button sits beside the info button; Scribe Settings stays in the normal font; all surfaces share
  the one `UseCuneiform` fallback branch; and the tablet stays one material-keyed parameterized type
  (never a subclass per material).

### Modified Capabilities

- `tablet-dialog`: REMOVE the "display-only cuneiform title banner" requirement (banner rejected in
  the 2026-08-02 playtest, superseded by cuneiform title-bar rendering), and MODIFY the "single
  branch honors the disable-cuneiform setting" requirement so the one `UseCuneiform` branch now covers
  the title bar, task-row display text, and button labels instead of the banner.
- `cuneiform-glyph-font`: the cuneiform layout/render path gains (a) an explicit line-wrap / truncation
  policy so cuneiform text placed in bounded chrome (title bar, task rows) wraps or ellipsizes rather
  than overrunning — the gap the banner playtest exposed — and (b) a **per-character X map** (cumulative
  advance position per source character) so an editor can place a synthetic caret and hit-test clicks
  against cuneiform text.

## Impact

- **Removed code:** `BuildTitleBanner()` and the banner stack in
  `GuiDialogScribeTablet.BuildCentralRegion()` (`src/Mod/GuiDialogScribeTablet.cs`).
- **New code (Mod/adapter):** a **cuneiform editable render object** (a `RenderBox` alongside
  `src/Mod/CuneiformText.cs`) that paints a text buffer as cuneiform strokes plus a synthetic caret
  bar, and implements the geometry contract `ScribeMultilineFieldState` depends on (`OffsetAtPosition`,
  `CaretOffsetVertical`, caret-X). A single-line variant serves the title.
- **Modified code (Mod/adapter):** `src/Mod/GuiDialogScribeTablet.cs` (route title + body + labels
  through cuneiform, collapse central region); `src/Mod/ScribeMultilineField.cs` (select the cuneiform
  render variant from its already-separable `State` under the `UseCuneiform` branch); `src/Mod/
  ScribeEditorContent.cs` (focus-driven row appearance on the always-present `Container`, the new
  settings gear button in `BuildFooterButtons`, cuneiform button labels, and an `OnOpenSettings`
  action); `src/Mod/ScribeDialogBase.Layout.cs` (a `protected virtual` title-display seam whose default
  is unchanged for the incumbents, plus wiring the gear action to `modSystem.OpenSettings`).
- **Modified code (Core):** `src/Core/Cuneiform/CuneiformLineLayout.cs` — multi-line wrap math AND a
  per-character X map (cumulative advance per source char) for caret placement / hit-testing. Both stay
  in `Core` (unit-testable, no VS API); the VS-adapter widgets stay in `src/Mod/`.
- **Consumes:** `CuneiformText`, `ScribeTaskFont.UseCuneiform` / `.Resolve`, the registered `scribegear`
  icon, `modSystem.OpenSettings`, and the base-dialog virtual seams — all already present from earlier
  work.
- **No new dependencies, no new network packets, no document-persistence change** — task/document
  saves still route through the existing `ScribeNotebookSaveMessage` write-through (vanilla Sign
  pattern). **One client-config schema change** does widen this beyond a pure render/layout change: the
  `DisableCuneiformFont` client setting is renamed/inverted to `CuneiformTablets` (default true), which
  touches `ScribePlayerSettings`, the Settings label/lang, and a one-time migration of existing on-disk
  values. This is client-local preference only (not synced world/document state), so it carries no
  network or document-migration impact.
- **Sequencing:** this change SUPERSEDES the "display-only cuneiform title banner" requirement that
  `add-tablet-dialog` (archived 2026-08-02) folded into the `tablet-dialog` capability. Implementing
  this change retires that banner requirement rather than contradicting the new title-bar rendering.
- **Retires Proposal D (`add-tablet-pencil-toggle-row`):** D was "toggle a row between a normal legible
  input and a cuneiform output." The reframe to live cuneiform *input* removes the toggle and the
  normal-font input state entirely — the row is always cuneiform — so D no longer describes what is
  being built. D's genuinely-needed pieces (fully-custom input, focus/deferred-blur handling, the
  disable-cuneiform fallback) are all absorbed here. D was never created as an openspec change
  (plan-only), so no archive is needed. The only D concept explicitly dropped — a per-row legible
  escape hatch — is superseded by the coarse disable-cuneiform toggle (whole tablet → legible).

### Non-Goals (explicitly out of scope)

- **Text selection over cuneiform** (shift-arrow / drag / double- and triple-click select + a
  highlight box drawn over the strokes) — deferred and captured as the explore stub
  `explore-cuneiform-text-selection`. This round is caret-only editing.
- The clay-type / fired backdrop set — that is the separate followup
  `add-tablet-clay-type-backdrops`.
- Any deferred tablet mechanics: firing, water damage, carry-forward migration, wax-wipe, and the
  stylus-in-offhand edit gate.
- Changing the Scribe Settings font — settings stay in the normal readable font.
