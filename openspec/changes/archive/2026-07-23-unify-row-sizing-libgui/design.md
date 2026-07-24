## Context

The lectern GUI lives entirely in `src/Mod/GuiDialogScribeLecternLibGui.cs`, built on the
LibGUI framework (modid `gui`). Task rows are rendered by two widget classes:
`ScribeReadRow` (read view: a `Checkbox` at `size: 22` + a static `Text` at `FontSize 14`,
wrapped in a `Row` with `spacing: 6`, `crossAxisAlignment: Center`, inside
`Padding(EdgeInsets.Symmetric(vertical: 4, horizontal: 2))`) and `ScribeEditRow` (editor
view: `Checkbox` at `size: 22` + a `ScribeMultilineField` at `fontSize: 15`, `Row`
`spacing: 6`, `crossAxisAlignment: Start`, same outer padding). The read view lists rows in
a virtualized `ListView` (`variableHeight: true`, `estimatedItemHeight: 34f`); the editor
lists them in a non-virtualized `SingleChildScrollView` + `Column(spacing: 6)` because each
editor row owns a focus/caret-stateful field that cannot be unmounted.

`ScribeMultilineField` (`src/Mod/ScribeMultilineField.cs`) auto-grows: its render object
computes `height = visualLines.Count * lineHeight + PadY * 2` with `PadX = 8f`, `PadY = 6f`
as `const`s, and draws a 1px border *inside* that box (adds no height).

The result: an editor task is intrinsically taller than the same read task (font 15 vs 14,
plus the field's `PadY*2` and the editor Column's 6px inter-row gap that the read `ListView`
does not add). Switching views shifts every task. Separately, the LibGUI dialog reads
`ScribeClientConfig` **zero times** — that load path died with the native GUI it replaced —
so all sizing is hardcoded and can only be changed by editing source and rebuilding.

Two prior investigations settled the mechanics: (1) a variable-height `ListView` renders each
visible row at its natural measured height with **no** inter-item gap, so it is layout-
equivalent to a `Column` *once the Column's `spacing` is removed*; (2) ConfigLib's settings
panel renders correctly on the author's Apple Silicon Mac (a float setting works; an integer
setting previously threw and broke the whole panel — see `VSAPI-NOTES.md`).

## Goals / Non-Goals

**Goals:**
- A single-line task occupies pixel-identical vertical space and position in the read and
  editor views, so switching views keeps every task pinned in place.
- Row sizing is data-driven from `ScribeClientConfig`, tunable by editing the config file and
  reopening the lectern.
- A single scaling chokepoint so a future font/UI-scale change is a one-line edit, not a
  re-plumbing. Introducing it is a behavioral no-op today.
- ConfigLib returns as an optional soft dependency exposing the sizing fields, with the mod
  fully functional when ConfigLib is absent.

**Non-Goals:**
- Pixel-identical parity for multi-line rows (the read view's static text and the editor's
  bordered field can wrap or inset differently; single-line parity is the firm requirement).
- Building the actual font-size/UI-scale user control — only the chokepoint it will hook into.
- Re-adding the VSImGui debug overlay (dead on this hardware; removed deliberately).
- Any change to Core, persistence, sync, or the document model.

## Decisions

**1. Load `ScribeClientConfig` per dialog-open in the `GuiDialogScribeLecternLibGui`
constructor.** The dialog is already constructed per-open (`BlockEntityScribeLectern.cs:272`),
so a `private readonly ScribeClientConfig config` initialized in the ctor via
`capi.LoadModConfig<ScribeClientConfig>(ScribeModSystem.ClientConfigFileName) ?? new()` is
per-open for free, adds no process-wide mutable state, and yields the edit-file-then-reopen
tuning loop the config class was written for. *Alternative rejected:* a
`ScribeModSystem`-owned shared singleton — it would introduce the mod's first client-side
shared mutable state for no consumer benefit and make "reopen picks up my edit" harder (you'd
have to decide when to reload). The archived `add-imgui-configlib-tuning` design rejected a
singleton for the same reason; that reasoning still holds here, now for a real feature.

**2. Carry sizing through one immutable `ScribeRowStyle` record struct, not per-value ctor
params or the LibGUI `Theme`.** Introduce `internal readonly record struct ScribeRowStyle`
holding the ~7 float sizing values, threaded by one ctor param down through the read/editor
content widgets into `ScribeReadRow`/`ScribeEditRow`/`ScribeMultilineField`. This matches the
file's existing "immutable data snapshot passed down" convention (`ScribeReadRowData`,
`ScribeEditRowData`) and keeps each widget testable with a literal style (no `capi`/config
needed). *Alternatives rejected:* adding 7 scalar ctor args to each widget (noisy, quadruples
plumbing); a custom LibGUI `Theme` subclass (LibGUI's `Theme` models the `ColorScheme`, not
sizing — hijacking it hides the data flow and is more invasive). Colors keep coming from
`Theme.Of(context)` as today; the struct carries only sizes.

**3. Scale at one factory: `ScribeRowStyle.FromConfig(ScribeClientConfig)`.** This factory is
the only place the scale multiply happens — it reads the existing `TextSizeScale` float
(default `1f`) and multiplies the scalable values (font size and scalable paddings) by it, so
every widget consumes already-scaled numbers. Today, with `TextSizeScale == 1f`, this is a
behavioral no-op that changes no pixels; a future scaling change becomes a one-line edit at
this factory (e.g. read the factor from a live control, or split into font-scale vs UI-scale).
This mirrors the native GUI's "scale at point of use" but centralizes it. `TextSizeScale`
stays out of the ConfigLib panel (it is a play-time knob, not a layout-tuning knob).

**4. Add clearly-named LibGUI-semantic float fields; reuse only `TextSizeScale`.** The
existing `ScribeClientConfig` fields (`TaskRowHeight`, `RowSpacing`, `ToggleWidth`,
`CheckboxTextGap`, ruling/affordance knobs) carry native-GUI semantics that don't map to the
LibGUI model (natural-height rows, checkbox-as-widget, no ruling, no affordance columns).
Reusing them by name would misdescribe what drives what, so leave them untouched as harmless
dead leftovers (`LoadModConfig` ignores unused keys). Add seven new floats: `RowFontSize=15`,
`RowVerticalPadding=4`, `RowHorizontalPadding=2`, `RowCheckboxTextGap=6`, `RowCheckboxSize=22`,
`FieldInnerPaddingX=8`, `FieldInnerPaddingY=6`. Prefix `Row*` for row composition and `Field*`
for the field's internals so they read as LibGUI-scoped and distinct from the leftovers.

**5. Promote `ScribeMultilineField`'s `PadX`/`PadY` from `const` to config-fed instance
values — but keep the render object framework-pure.** The render object should not read
config directly (it is the most testable unit, constructed deep in the tree). Instead promote
`PadX`/`PadY` to settable instance fields + properties on the render object (mirroring the
existing `FontSize` property pattern), add ctor args on the render-widget and the public
`ScribeMultilineField` (defaults 8/6), and assign them in `UpdateRenderObject` exactly like
`FontSize`. `ScribeEditRow` passes `style.FieldPadX/Y` in.

**6. The unification recipe.** Target font 15 (editor wins). Let `P = RowVerticalPadding`
(4), `FP = FieldInnerPaddingY` (6). A single-line task's height in both views becomes
`P*2 + FP*2 + lineHeight`.
- *`ScribeReadRow`:* `Text` uses `style.RowFontSize`; wrap the `Text` (the `Expanded` child)
  in `Padding(EdgeInsets.Symmetric(vertical: style.FieldPadY, horizontal: style.FieldPadX))`
  so its text block matches the field's internal box (height parity) and its left edge aligns
  with the editor field's text (no horizontal jump); `Checkbox` uses `style.RowCheckboxSize`;
  `Row` `spacing` = `style.RowCheckboxTextGap`; `crossAxisAlignment` → **`Start`** (was
  `Center`) to match the editor; outer padding =
  `EdgeInsets.Symmetric(vertical: style.RowVerticalPadding, horizontal: style.RowHorizontalPadding)`;
  no border.
- *`ScribeEditRow`:* `Checkbox` uses `style.RowCheckboxSize`; field uses `style.RowFontSize`,
  `PadX: style.FieldPadX`, `PadY: style.FieldPadY`; same `Row` spacing / `Start` alignment /
  outer padding; field keeps its border.
- *Editor `Column` `spacing` → `0`.* All row separation now lives in each row's own vertical
  padding, matching the read `ListView` (which adds no inter-row gap).
- *Read `ListView`* keeps `variableHeight: true`; set `estimatedItemHeight` from the style
  (≈ `RowFontSize*1.2 + FieldPadY*2 + RowVerticalPadding*2`) so the scroll estimate tracks the
  real height. It is only a scrollbar estimate, not layout-critical.
- Outer content `Column`s (`spacing: 8`) and footer buttons are chrome outside the task-row
  body and are left unchanged.

**7. ConfigLib re-add is manifest-only, float-typed, no hard dependency.** Add a
`<Reference Include="configlib"><HintPath>lib/configlib.dll</HintPath><Private>false</Private>`
to `Mod.csproj` and a `src/Mod/assets/scribe/config/configlib-patches.json` with
`"version": 1`, `"file": "scribe-client-config.json"`, and a `"settings"` array of one
`"type": "float"` entry per new field (each `"code"` = the exact field name, plus
`"default"`, `"comment"`, `"range"`). No modinfo dependency — a `"file"` manifest is inert if
ConfigLib is not installed, and Scribe calls no ConfigLib API. Every setting is float because
an integer ConfigLib setting previously threw while drawing and broke the entire panel
(`VSAPI-NOTES.md`).

## Risks / Trade-offs

- **[Risk] A 22px checkbox next to a ~18px single text line under `crossAxisAlignment: Start`
  leaves the checkbox slightly taller than the first line.** → This is the *consistent* choice
  (the editor already top-aligns) and is what parity requires; `RowCheckboxSize` is now a
  config float, so if it reads too tall it can be dialed toward the line height in-game without
  a rebuild. Do **not** revert the read row to `Center` — that re-diverges the two views.
  Confirm the look in playtest.
- **[Risk] Multi-line rows won't be pixel-identical between views** (static wrapped text vs a
  bordered field with a caret). → Accepted and scoped out; the read text now insets by
  `FieldPadX` so wrap width matches the field's `availWidth - PadX*2`, which keeps line counts
  aligned. Verify a long task in-game.
- **[Risk] An integer ConfigLib setting breaks the panel.** → Mitigation: every manifest entry
  is `"type": "float"`; never add an integer entry. Documented in `VSAPI-NOTES.md`.
- **[Risk] `LoadModConfig` returns null on first run.** → Guard with `?? new ScribeClientConfig()`.
  Optionally `StoreModConfig` once so the file exists for hand-editing/ConfigLib; deferred to
  keep the dialog read-only w.r.t. disk unless a first-open write proves needed.
- **[Trade-off] Config edits do not hot-apply to an already-open dialog** (per-open load). →
  Accepted; it matches the established behavior of this config file and keeps the model simple.

## Open Questions

- None blocking. The checkbox-vs-line-height alignment and multi-line wrap parity are
  playtest-confirmation items, not design unknowns — both are now config-tunable if they read
  wrong in-game.
