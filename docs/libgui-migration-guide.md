# Scribe → LibGUI Migration Guide

A rebuild playbook for both the author and the agent: how each piece of Scribe's current
custom-drawn lectern GUI maps onto LibGUI, in what order to do it, and the traps to avoid.

> **Read first:** [`libgui-reference.md`](libgui-reference.md) for the LibGUI model, and
> `openspec/changes/explore-libgui-adoption/proposal.md` for why this is **spike-gated** — do not
> start a migration until the spike's go/no-go checklist passes. This guide is the plan for *if we
> go*, and the input to the spike.

## The mental shift

Scribe today: **imperative, absolute, pull.** Two big `Compose*View()` methods hand-place every
element with `ElementBounds.Fixed(x, y, w, h)`, thread a manual `double y` cursor, pre-measure rows
into `rowYs[]`/`rowHeights[]`, and re-run the whole compose on nearly any change — with a
`pendingRecomposeAction` hack to defer recompose out of the mouse-dispatch loop
(`GuiDialogScribeLectern.cs:226`).

LibGUI: **declarative, relative, push.** You describe the tree once in `Build()`; you mutate state
and call `SetState`; the framework diffs, re-lays-out only what changed, and paints. No manual
coordinates, no recompose plumbing, no reentrancy hack.

```
   TODAY                                    LIBGUI
   ─────                                    ──────
   ComposeReadView()  ─┐                    Build() → Widget tree
   ComposeEditorView()─┘ absolute bounds       Column / ListView / Row / Expanded
   rowYs[] / rowHeights[] pre-pass          → RenderFlex + variable-height ListView
   y += spacing (manual cursor)             → Spacing / Padding / EdgeInsets
   BeginClip + cull + parent-fixedY shift   → ListView viewport (virtualized, free)
   pendingRecomposeAction (defer recompose) → SetState (targeted subtree rebuild)
   ~60 ScribeClientConfig knobs             → ThemeData/ColorScheme + libgui.json
```

## Mapping table

| Today (file) | LibGUI target | Notes / trap |
|---|---|---|
| `GuiDialogScribeLectern` two views (`ComposeReadView`/`ComposeEditorView`, `:21`) | One `GuiDialogBlockEntityBase` subclass; a `StatefulWidget` with a `ViewMode` field; `Build()` branches read vs. editor | Both views collapse into one reactive tree; toggling mode is `SetState`, not a recompose. |
| Manual `y +=` cursor + `rowYs[]`/`rowHeights[]` pre-pass | `Column` (fixed content) / `ListView` (the scrollable row list) | Spacing via `Column.Spacing` / `Padding`, not arithmetic. |
| `ScribeRowElement` (custom Cairo row, `:530` lines) | A `StatelessWidget` = `Row` of `[Checkbox/affordance, Expanded(Text|TextField), IconButtons]` | The single biggest deletion. Custom paint only if a look truly needs a `RenderBox`. |
| `RowTextLayout.For` column X/width math (`:164`) | `Row` + `Expanded`/`SizedBox` | Flex replaces the hand-computed column geometry entirely. |
| `ScribeHoverIconButton` hand-baked 3-texture toggle (`ScribeBlockRowCell.cs`) | `IconButton` + `ColorScheme.StateHover`/`StateSelected` | The "no styling seam" pain (hand-baking off/on/pressed) disappears — states are theme-driven. |
| Hand-drawn checkbox (`ScribeRowElement.DrawCheckboxGlyph`) | `Checkbox` (controlled-component) — or a small `RenderBox` if the skeuomorphic stamp look demands it | Keep the option open for the roadmap's checkbox-stamp animation (`AnimatedScale`/custom paint). |
| `ScribeRowTextInput` (floating `GuiElementTextArea`, `:266`) | `TextField` + `TextEditingController`; row grows via **variable-height `ListView`** / `IntrinsicHeight` | Replaces the manual re-measure-and-recompose on every keystroke. **This is the risky one — see below.** |
| `ScribeRowListScrollbar` + `BeginClip` + `OnRowListScroll` (`:243`) | `ListView` (+ optional standalone `Scrollbar`) sharing a `ScrollController` | Virtualization replaces the thrice-rewritten cull/clip. Scroll-into-view → `Scrollable.EnsureVisible` (via a `GlobalKey`). |
| ~60 `ScribeClientConfig` layout/color knobs (`:335`) | `ThemeData`/`ColorScheme` + `ModConfig/libgui.json` (hot-reload) | Keep only genuinely **behavioral** knobs (e.g. `MinTextSizePercent`, autosave interval) in `ScribeClientConfig`; move color/spacing to the theme. |
| `ScribeInspectOverlay` / `BuildInspectBoxes` (`:208` / `:297`) | LibGUI's built-in `/ui tree|bounds|paint|heatmap` | The custom overlay likely becomes redundant. Confirm the built-in is usable on Apple Silicon first. |
| `pendingRecomposeAction` deferral hack (`:226`) | Deleted — `SetState` is the reactive model | The whole "recompose corrupts the composer mid-dispatch" class of bug goes away. |
| Custom SVG icons `scribepin`/`scribegrip`/`scribeclose`/`scribeedit` | `Icon`/`VsIcon` or `Image` | `Svg.Skia` is already in LibGUI's dep set, so SVG rendering is native. |
| Server-authoritative packets (`SendBlockEntityPacket`, `ScribeEditDocumentMessage`, etc.) | **Unchanged** — `GuiDialogBlockEntityBase.SendBlockEntityPacket` is the same wire | The Core model + network + persistence are untouched; this is a Mod-layer view swap only. |
| `src/Core/` (document/task model, codec) | **Untouched** | LibGUI never enters Core — the VS-API-free invariant is preserved. |

## The one trap that decides everything: the editable variable-height row

The native `AddCellList`/`IGuiElementCell` path was explicitly rejected because a cell list can't
host a live typable field (`ScribeBlockRowCell.cs:306`). LibGUI's `ListView` is a different animal
— but two things must both be true, and **must be proven in the spike, not assumed**:

1. A `TextField` inside a `ListView` row can hold focus and take keystrokes (the cell-list failure
   mode). LibGUI has a real `FocusManager`, so this *should* work — verify it.
2. The row **grows as the text wraps**. This needs `ListView`'s `variableHeight: true` +
   `estimatedItemHeight` constructor (source: `ListView.cs:44`/`:88` + `ItemHeightCache`) — a path
   the wiki's *Scrolling* page omits and even contradicts ("all items must have the same height").
   **Trust the source over the wiki here.**

If (2) doesn't hold, the fallback is a non-virtualized `SingleChildScrollView` + `Column` of
`IntrinsicHeight` rows — fine for a lectern's handful of rows, losing only virtualization (which a
lectern doesn't need anyway; the notebook/desk tiers might).

## Migration order (if the spike says go)

```
   ┌─────────────────────────────────────────────────────────────┐
   │ 0. SPIKE (throwaway): read-view on LibGUI + render on ASi    │  ← gate
   ├─────────────────────────────────────────────────────────────┤
   │ 1. Read-view for real (Column/ListView, theme, packets in)   │
   │ 2. Editor-view (TextField + variable-height rows + autosave)  │
   │ 3. Affordance columns + drag-reorder (ValueKey identity)      │
   │ 4. Theme extraction (ScribeClientConfig colors → libgui.json) │
   │ 5. Retire ScribeInspectOverlay if /ui covers it              │
   ├─────────────────────────────────────────────────────────────┤
   │ LATER: held tiers (notebook/tablets/desk/HUD/board) reuse the │
   │        one LibGUI foundation — the payoff of doing this once  │
   └─────────────────────────────────────────────────────────────┘
```

Each numbered step should be its own OpenSpec change (spec-driven guardrail). Steps 1–2 supersede
the empty `own-lectern-element-bounds` stub (LibGUI *is* the "own bounds" answer, from a different
direction).

## Casualties & things to decide during migration

- **`scribe-layout-workbench`** (the sibling browser tool) mirrors the *absolute-bounds* math
  (`RowTextLayout.For`, `BuildInspectBoxes`). Under a flex/relative model that mirror no longer
  maps — the workbench would need a rethink (mirror LibGUI's flex instead) or retirement. Decide
  explicitly; don't let it silently rot.
- **`VSAPI-NOTES.md`** GuiComposer knowledge (clip/scissor/focus/reentrancy) becomes mostly moot.
  Keep it as history; new lessons go in its `## LibGUI` section.
- **The Creative-reach auto-close fix** (Scribe overrides `IsInRangeOfBlock`) must be re-checked
  against `GuiDialogBlockEntityBase.IsOutOfRange`/`InteractionRange` — the override point differs.
- **Release builds must still work without VSImGui** (Debug-only). LibGUI is a *runtime* dep, not
  Debug-only — so unlike VSImGui it ships in Release, which is exactly why the Apple-Silicon render
  gate is non-negotiable.
